using System;
using System.Collections.Generic;
using NUnit.Framework;
using Session.Core.Attendant;
using Session.Core.Identity;
using Session.Core.Perception;
using Session.Core.Puzzles;
using Session.Core.Rooms;
using Session.Core.Session;

namespace Session.Tests.Core.Session
{
    public sealed class SessionDirectorTests
    {
        private static readonly PlayerId Alice = new PlayerId(0);
        private static readonly PlayerId Bren = new PlayerId(1);
        private static readonly RoomId Nine = new RoomId(9);
        private static readonly RoomId Seventeen = new RoomId(17);

        private static SessionDirector Fresh(ulong seed = 1971UL)
        {
            var rooms = new List<RoomDefinition>
            {
                TestRooms.Simple(9, clueCount: 6),
                TestRooms.Simple(17, clueCount: 6)
            };

            return new SessionDirector(
                seed, rooms, DefaultLensRules.Instance, DefaultAttendantProfile.Instance);
        }

        private static SessionDirector WithTwoPlayers()
        {
            SessionDirector director = Fresh();
            director.PlayerConnected(Alice);
            director.PlayerConnected(Bren);
            return director;
        }

        private static SessionEvent[] Drain(SessionDirector director)
        {
            Span<SessionEvent> buffer = stackalloc SessionEvent[32];
            int count = director.DrainEvents(buffer);

            var result = new SessionEvent[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = buffer[i];
            }

            return result;
        }

        [Test]
        public void RoomsStageOnceEnoughPlayersConnect()
        {
            SessionDirector director = Fresh();

            director.PlayerConnected(Alice);
            Assert.IsNull(director.LensFor(Alice, Nine), "One player is not a session.");

            director.PlayerConnected(Bren);
            Assert.IsNotNull(director.LensFor(Alice, Nine));
            Assert.IsNotNull(director.LensFor(Bren, Nine));
        }

        [Test]
        public void StagedLensesSatisfyTheInterdependenceInvariant()
        {
            SessionDirector director = WithTwoPlayers();
            RoomDefinition room = director.DefinitionOf(Nine);

            Assert.IsNotNull(room);

            LensAssigner.TryAssign(room, director.SessionSeed, 2, DefaultLensRules.Instance,
                out LensAssignment assignment, out _);

            Assert.IsTrue(LensValidator.Validate(room, assignment).IsValid);
        }

        [Test]
        public void ClientsDeriveTheSameLensesTheServerStaged()
        {
            // This is what lets the server replicate a single seed instead of per-prop variant ids.
            SessionDirector director = WithTwoPlayers();
            RoomDefinition room = director.DefinitionOf(Nine);

            LensAssigner.TryAssign(room, director.SessionSeed, 2, DefaultLensRules.Instance,
                out LensAssignment clientSide, out _);

            Lens serverLens = director.LensFor(Alice, Nine);
            Lens clientLens = clientSide.For(0);

            for (int ordinal = 0; ordinal < room.PropCount; ordinal++)
            {
                Assert.AreEqual(
                    serverLens.VariantIndex(ordinal), clientLens.VariantIndex(ordinal),
                    "Server and client disagree at prop ordinal {0}", ordinal);
            }
        }

        [Test]
        public void PuzzleCannotBeSubmittedFromAnotherRoom()
        {
            SessionDirector director = WithTwoPlayers();
            director.PlayerEnteredRoom(Alice, Seventeen);

            AttemptOutcome outcome = director.SubmitPuzzle(
                Alice, Nine, new PuzzleNodeId(1), stackalloc int[] { 4, 1, 7, 2 });

            Assert.AreEqual(AttemptOutcome.Locked, outcome);
            Assert.IsFalse(director.IsRoomComplete(Nine));
        }

        [Test]
        public void CorrectSubmissionCompletesTheRoomAndEmitsEvents()
        {
            SessionDirector director = WithTwoPlayers();
            director.PlayerEnteredRoom(Alice, Nine);
            Drain(director);

            AttemptOutcome outcome = director.SubmitPuzzle(
                Alice, Nine, new PuzzleNodeId(1), stackalloc int[] { 4, 1, 7, 2 });

            Assert.AreEqual(AttemptOutcome.Accepted, outcome);
            Assert.IsTrue(director.IsRoomComplete(Nine));

            SessionEvent[] events = Drain(director);
            Assert.AreEqual(2, events.Length);
            Assert.AreEqual(SessionEventKind.PuzzleSolved, events[0].Kind);
            Assert.AreEqual(SessionEventKind.RoomCompleted, events[1].Kind);
        }

        [Test]
        public void WrongSubmissionEmitsNothing()
        {
            SessionDirector director = WithTwoPlayers();
            director.PlayerEnteredRoom(Alice, Nine);
            Drain(director);

            director.SubmitPuzzle(Alice, Nine, new PuzzleNodeId(1), stackalloc int[] { 0, 0, 0, 0 });

            Assert.AreEqual(0, Drain(director).Length, "A failed guess must not be broadcast.");
        }

        [Test]
        public void LeavingAnUnfinishedRoomWakesTheAttendant()
        {
            SessionDirector director = WithTwoPlayers();
            director.PlayerEnteredRoom(Alice, Nine);
            director.Tick(0.05f);

            Assert.AreEqual(AttendantState.Dormant, director.AttendantState);

            director.PlayerEnteredRoom(Alice, Seventeen); // left Room 9 unfinished
            director.Tick(0.05f);

            Assert.AreNotEqual(AttendantState.Dormant, director.AttendantState);
        }

        [Test]
        public void FinishingARoomBeforeLeavingKeepsItDormant()
        {
            SessionDirector director = WithTwoPlayers();
            director.PlayerEnteredRoom(Alice, Nine);
            director.SubmitPuzzle(Alice, Nine, new PuzzleNodeId(1), stackalloc int[] { 4, 1, 7, 2 });

            director.PlayerEnteredRoom(Alice, Seventeen);

            for (int i = 0; i < 100; i++)
            {
                director.Tick(0.05f);
            }

            Assert.AreEqual(AttendantState.Dormant, director.AttendantState);
        }

        [Test]
        public void ProtocolViolationsSurfaceAsEvents()
        {
            SessionDirector director = WithTwoPlayers();
            director.PlayerEnteredRoom(Alice, Nine);
            Drain(director);

            director.DoorForced(Alice, Nine);
            director.Tick(0.05f);

            SessionEvent[] events = Drain(director);

            bool found = false;
            foreach (SessionEvent e in events)
            {
                if (e.Kind == SessionEventKind.ProtocolViolation &&
                    e.Payload == (int)ViolationKind.ForcedDoor)
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "Forcing a door should reach clients as an event.");
        }

        [Test]
        public void DisconnectRestagesRoomsForTheSmallerGroup()
        {
            SessionDirector director = Fresh();
            director.PlayerConnected(Alice);
            director.PlayerConnected(Bren);
            director.PlayerConnected(new PlayerId(2));

            Lens beforeThreePlayers = director.LensFor(Alice, Nine);
            Assert.IsNotNull(beforeThreePlayers);

            director.PlayerDisconnected(new PlayerId(2));

            Lens afterTwoPlayers = director.LensFor(Alice, Nine);
            Assert.IsNotNull(afterTwoPlayers, "Rooms must restage, not go dark, when someone leaves.");

            RoomDefinition room = director.DefinitionOf(Nine);
            LensAssigner.TryAssign(room, director.SessionSeed, 2, DefaultLensRules.Instance,
                out LensAssignment expected, out _);

            for (int ordinal = 0; ordinal < room.PropCount; ordinal++)
            {
                Assert.AreEqual(expected.For(0).VariantIndex(ordinal), afterTwoPlayers.VariantIndex(ordinal));
            }
        }

        [Test]
        public void SessionIsFullyDeterministicForAGivenSeed()
        {
            SessionDirector a = Fresh(4242UL);
            SessionDirector b = Fresh(4242UL);

            foreach (SessionDirector director in new[] { a, b })
            {
                director.PlayerConnected(Alice);
                director.PlayerConnected(Bren);
                director.PlayerEnteredRoom(Alice, Nine);
                director.DoorForced(Alice, Nine);

                for (int i = 0; i < 200; i++)
                {
                    director.Tick(0.05f);
                }
            }

            Assert.AreEqual(a.AttendantState, b.AttendantState);
            Assert.AreEqual(a.AttendantSuspicion, b.AttendantSuspicion);
            Assert.AreEqual(a.Clock, b.Clock);
        }

        [Test]
        public void OutOfRangePlayerSlotIsRejected()
        {
            SessionDirector director = Fresh();

            Assert.Throws<ArgumentOutOfRangeException>(() => director.PlayerConnected(new PlayerId(99)));
        }
    }
}
