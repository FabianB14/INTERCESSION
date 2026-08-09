using NUnit.Framework;
using Session.Core.Identity;
using Session.Core.Perception;
using Session.Core.Puzzles;
using Session.Core.Rooms;

namespace Session.Tests.Core.Perception
{
    /// <summary>
    /// The invariant these tests defend is the game's premise. If one player can read every
    /// required clue in a room, that player can solve it silently and the co-op horror escape room
    /// becomes a single-player escape room with witnesses.
    /// </summary>
    public sealed class LensAssignerTests
    {
        private const int SeedSweepCount = 5000;

        [Test]
        public void NoLensIsEverSoloSolvable_AcrossManySeeds([Values(2, 3, 4)] int playerCount)
        {
            RoomDefinition room = TestRooms.Simple(roomId: 9, clueCount: 6);

            for (int seed = 0; seed < SeedSweepCount; seed++)
            {
                bool assigned = LensAssigner.TryAssign(
                    room, (ulong)seed, playerCount, DefaultLensRules.Instance,
                    out LensAssignment? assignment, out LensAssignmentFailure failure);

                Assert.IsTrue(assigned, "Seed {0} failed to assign: {1}", seed, failure);

                LensValidation validation = LensValidator.Validate(room, assignment!);
                Assert.IsTrue(
                    validation.IsValid,
                    "Seed {0}, {1} players: {2}", seed, playerCount, validation);
            }
        }

        [Test]
        public void NoLensIsEverSoloSolvable_WithTransitiveClues([Values(2, 3, 4)] int playerCount)
        {
            RoomDefinition room = TestRooms.Chained(roomId: 17);

            for (int seed = 0; seed < SeedSweepCount; seed++)
            {
                LensAssigner.TryAssign(
                    room, (ulong)seed, playerCount, DefaultLensRules.Instance,
                    out LensAssignment? assignment, out LensAssignmentFailure failure);

                Assert.AreEqual(LensAssignmentFailure.None, failure, "Seed {0}", seed);
                Assert.IsTrue(LensValidator.Validate(room, assignment!).IsValid, "Seed {0}", seed);
            }
        }

        [Test]
        public void RedundantRevealsNeverBreakTheInvariant()
        {
            // Turning redundancy up is a difficulty knob, not a correctness risk. Even at 100% the
            // per-grant guard must hold the line.
            RoomDefinition room = TestRooms.Simple(roomId: 4, clueCount: 8);
            var generous = new TestLensRules(redundantRevealPercent: 100);

            for (int seed = 0; seed < SeedSweepCount; seed++)
            {
                LensAssigner.TryAssign(
                    room, (ulong)seed, 3, generous, out LensAssignment? assignment, out _);

                LensValidation validation = LensValidator.Validate(room, assignment!);
                Assert.IsTrue(validation.IsValid, "Seed {0}: {1}", seed, validation);
            }
        }

        [Test]
        public void EveryRequiredClueIsReadableBySomeone([Values(2, 3, 4)] int playerCount)
        {
            RoomDefinition room = TestRooms.Simple(roomId: 11, clueCount: 5);

            for (int seed = 0; seed < 1000; seed++)
            {
                LensAssigner.TryAssign(
                    room, (ulong)seed, playerCount, DefaultLensRules.Instance, out LensAssignment? assignment, out _);

                var required = room.Puzzles.RequiredClues;
                for (int i = 0; i < required.Length; i++)
                {
                    bool anyoneCanRead = false;
                    for (int p = 0; p < playerCount; p++)
                    {
                        if (assignment!.For(p).CanRead(room, required[i]))
                        {
                            anyoneCanRead = true;
                            break;
                        }
                    }

                    Assert.IsTrue(anyoneCanRead, "Seed {0}: clue {1} unreadable by anyone", seed, required[i]);
                }
            }
        }

        [Test]
        public void AssignmentIsDeterministic()
        {
            RoomDefinition room = TestRooms.Simple(roomId: 9, clueCount: 6);
            const ulong seed = 0xC0FFEE_1971UL;

            LensAssigner.TryAssign(room, seed, 4, DefaultLensRules.Instance, out LensAssignment? first, out _);
            LensAssigner.TryAssign(room, seed, 4, DefaultLensRules.Instance, out LensAssignment? second, out _);

            for (int p = 0; p < 4; p++)
            {
                Lens a = first!.For(p);
                Lens b = second!.For(p);

                for (int ordinal = 0; ordinal < room.PropCount; ordinal++)
                {
                    Assert.AreEqual(a.VariantIndex(ordinal), b.VariantIndex(ordinal),
                        "Player {0}, prop ordinal {1}", p, ordinal);
                    Assert.AreEqual(a.RevealsClue(ordinal), b.RevealsClue(ordinal));
                }
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentLenses()
        {
            RoomDefinition room = TestRooms.Simple(roomId: 9, clueCount: 6);

            LensAssigner.TryAssign(room, 1UL, 2, DefaultLensRules.Instance, out LensAssignment? a, out _);
            LensAssigner.TryAssign(room, 2UL, 2, DefaultLensRules.Instance, out LensAssignment? b, out _);

            bool anyDifference = false;
            for (int ordinal = 0; ordinal < room.PropCount && !anyDifference; ordinal++)
            {
                if (a!.For(0).VariantIndex(ordinal) != b!.For(0).VariantIndex(ordinal))
                {
                    anyDifference = true;
                }
            }

            Assert.IsTrue(anyDifference, "Two different session seeds produced an identical lens.");
        }

        [Test]
        public void TwoPlayersDoNotSeeIdenticalRooms()
        {
            // Not a hard invariant of the algorithm, but if it fails often the room is not doing
            // its job. Over a sweep, at least most seeds must differentiate the two players.
            RoomDefinition room = TestRooms.Simple(roomId: 9, clueCount: 6);
            int identical = 0;

            for (int seed = 0; seed < 1000; seed++)
            {
                LensAssigner.TryAssign(room, (ulong)seed, 2, DefaultLensRules.Instance, out LensAssignment? assignment, out _);

                bool same = true;
                for (int ordinal = 0; ordinal < room.PropCount; ordinal++)
                {
                    if (assignment!.For(0).VariantIndex(ordinal) != assignment.For(1).VariantIndex(ordinal))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    identical++;
                }
            }

            Assert.AreEqual(0, identical, "{0}/1000 seeds gave both players an identical room.", identical);
        }

        [Test]
        public void PlayerLensIsIndependentOfGroupSize()
        {
            // Player 0's stream must not shift when a third player joins. Their variant choices are
            // seeded per player; only which clues they own can change.
            RoomDefinition room = TestRooms.Simple(roomId: 9, clueCount: 6);
            const ulong seed = 771971UL;

            LensAssigner.TryAssign(room, seed, 2, DefaultLensRules.Instance, out LensAssignment? pair, out _);
            LensAssigner.TryAssign(room, seed, 3, DefaultLensRules.Instance, out LensAssignment? trio, out _);

            // Set dressing carries no clue, so its appearance depends only on the per-player stream.
            for (int ordinal = 0; ordinal < room.PropCount; ordinal++)
            {
                if (room.PropAt(ordinal).CarriesClue)
                {
                    continue;
                }

                Assert.AreEqual(
                    pair!.For(0).VariantIndex(ordinal),
                    trio!.For(0).VariantIndex(ordinal),
                    "Set dressing at ordinal {0} moved when a third player joined.", ordinal);
            }
        }

        [Test]
        public void RoomWithFewerCluesThanPlayersIsRejected()
        {
            RoomDefinition room = TestRooms.Simple(roomId: 2, clueCount: 2);

            bool assigned = LensAssigner.TryAssign(
                room, 1UL, 4, DefaultLensRules.Instance, out LensAssignment? assignment, out LensAssignmentFailure failure);

            Assert.IsFalse(assigned);
            Assert.IsNull(assignment);
            Assert.AreEqual(LensAssignmentFailure.NotEnoughRequiredClues, failure);
        }

        [Test]
        public void SinglePlayerIsRejected()
        {
            RoomDefinition room = TestRooms.Simple(roomId: 2, clueCount: 6);

            bool assigned = LensAssigner.TryAssign(
                room, 1UL, 1, DefaultLensRules.Instance, out _, out LensAssignmentFailure failure);

            Assert.IsFalse(assigned);
            Assert.AreEqual(LensAssignmentFailure.TooFewPlayers, failure);
        }

        [Test]
        public void PropWithNoConcealingVariantIsRejected()
        {
            // A clue that can only ever be shown cannot be withheld, so it cannot be split.
            var alwaysVisible = new PropDefinition(
                new PropId(1),
                new ClueId(1),
                new[] { new PropVariant(new VariantId(0), 1, 1, revealsClue: true) });

            var props = new[]
            {
                alwaysVisible,
                TestRooms.ClueProp(2, 2),
                TestRooms.ClueProp(3, 3)
            };

            var exit = new PuzzleNode(
                new PuzzleNodeId(1),
                new Solution(SolutionKind.Ordered, 1),
                requiredClues: new[] { new ClueId(1), new ClueId(2), new ClueId(3) },
                requiredNodes: null,
                isRoomExit: true);

            var room = new RoomDefinition(new RoomId(3), props, new PuzzleGraph(new[] { exit }), 300f);

            bool assigned = LensAssigner.TryAssign(
                room, 1UL, 2, DefaultLensRules.Instance, out _, out LensAssignmentFailure failure);

            Assert.IsFalse(assigned);
            Assert.AreEqual(LensAssignmentFailure.PropMissingConcealingVariant, failure);
        }

        private sealed class TestLensRules : ILensRules
        {
            public TestLensRules(int redundantRevealPercent)
            {
                RedundantRevealPercent = redundantRevealPercent;
            }

            public int MinPlayers => 2;

            public int MaxPlayers => 4;

            public int RedundantRevealPercent { get; }
        }
    }
}
