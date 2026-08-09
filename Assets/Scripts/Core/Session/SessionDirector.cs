using System;
using System.Collections.Generic;
using Session.Core.Attendant;
using Session.Core.Identity;
using Session.Core.Perception;
using Session.Core.Protocol;
using Session.Core.Puzzles;
using Session.Core.Rooms;

namespace Session.Core.Session
{
    /// <summary>
    /// The server's brain for one run. Owns every piece of authoritative state — lens assignments,
    /// puzzle progress, protocol violations, and the Attendant — and exposes commands that the
    /// netcode layer calls in response to client RPCs.
    ///
    /// Everything here is plain C#. The NetworkBehaviour that drives it is a thin adapter: it
    /// translates RPCs into these calls, and translates <see cref="DrainEvents"/> into replication.
    /// Nothing in this file knows what a NetworkVariable is, which is why the whole run can be
    /// simulated in a unit test in milliseconds.
    ///
    /// Server-only. Constructing one on a client is a bug: it holds the canonical puzzle solutions.
    /// </summary>
    public sealed class SessionDirector : IRoomProgressSource
    {
        private sealed class RoomState
        {
            public RoomDefinition Definition = null!;
            public PuzzleRuntime Puzzles = null!;
            public LensAssignment? Lenses;
            public bool EverCompleted;
        }

        private readonly Dictionary<int, RoomState> _rooms;
        private readonly ILensRules _lensRules;
        private readonly ProtocolMonitor _protocol;
        private readonly AttendantMachine _attendant;
        private readonly ulong _sessionSeed;
        private readonly int _maxPlayers;

        private readonly bool[] _playerConnected;
        private readonly RoomId[] _playerRoom;

        private readonly SessionEvent[] _events;
        private int _eventHead;
        private int _eventCount;
        private int _droppedEvents;

        private readonly ProtocolViolation[] _violationScratch;

        private float _clock;
        private int _connectedCount;

        public SessionDirector(
            ulong sessionSeed,
            IReadOnlyList<RoomDefinition> rooms,
            ILensRules lensRules,
            IAttendantProfile attendantProfile,
            int maxPlayers = 4,
            int eventCapacity = 64)
        {
            if (rooms == null || rooms.Count == 0)
            {
                throw new ArgumentException("A session needs at least one room.", nameof(rooms));
            }

            if (maxPlayers < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Session is co-op; two players minimum.");
            }

            _sessionSeed = sessionSeed;
            _lensRules = lensRules ?? throw new ArgumentNullException(nameof(lensRules));
            _maxPlayers = maxPlayers;

            _rooms = new Dictionary<int, RoomState>(rooms.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomDefinition definition = rooms[i];
                if (_rooms.ContainsKey(definition.Id.Value))
                {
                    throw new ArgumentException("Duplicate room id: " + definition.Id, nameof(rooms));
                }

                _rooms.Add(definition.Id.Value, new RoomState
                {
                    Definition = definition,
                    Puzzles = new PuzzleRuntime(definition.Puzzles)
                });
            }

            _protocol = new ProtocolMonitor(this, maxPlayers);
            _attendant = new AttendantMachine(attendantProfile);

            _playerConnected = new bool[maxPlayers];
            _playerRoom = new RoomId[maxPlayers];

            _events = new SessionEvent[eventCapacity];
            _violationScratch = new ProtocolViolation[16];
        }

        public ulong SessionSeed => _sessionSeed;

        public AttendantState AttendantState => _attendant.State;

        public AttendantIntent AttendantIntent => _attendant.Intent;

        public RoomId AttendantTargetRoom => _attendant.TargetRoom;

        public bool AttendantIsBlockedByActiveSession => _attendant.IsBlockedByActiveSession;

        public float AttendantSuspicion => _attendant.Suspicion;

        public int ConnectedPlayerCount => _connectedCount;

        public float Clock => _clock;

        public int DroppedEvents => _droppedEvents;

        // ---- players -----------------------------------------------------------------------

        public void PlayerConnected(PlayerId player)
        {
            int slot = SlotOf(player);
            if (_playerConnected[slot])
            {
                return;
            }

            _playerConnected[slot] = true;
            _playerRoom[slot] = RoomId.None;
            _connectedCount++;

            // Lens assignment is a function of player count, so the split has to be recomputed.
            // Any staged room is now stale.
            RestageAllRooms();
        }

        public void PlayerDisconnected(PlayerId player)
        {
            int slot = SlotOf(player);
            if (!_playerConnected[slot])
            {
                return;
            }

            _playerConnected[slot] = false;
            _playerRoom[slot] = RoomId.None;
            _connectedCount--;

            _protocol.PlayerDisconnected(player);
            RestageAllRooms();
        }

        public bool IsConnected(PlayerId player) => _playerConnected[SlotOf(player)];

        public RoomId RoomOf(PlayerId player) => _playerRoom[SlotOf(player)];

        // ---- rooms -------------------------------------------------------------------------

        /// <summary>
        /// Assign lenses for a room at the current player count. Safe to call repeatedly — the
        /// result is a pure function of (seed, room, player count), so restaging is idempotent.
        /// </summary>
        public bool TryStageRoom(RoomId room, out LensAssignmentFailure failure)
        {
            failure = LensAssignmentFailure.None;

            if (!_rooms.TryGetValue(room.Value, out RoomState state))
            {
                failure = LensAssignmentFailure.RequiredClueHasNoProp;
                return false;
            }

            if (_connectedCount < _lensRules.MinPlayers)
            {
                // Not an error: waiting in the lobby is a normal state. Rooms stage when the group
                // is large enough.
                state.Lenses = null;
                failure = LensAssignmentFailure.TooFewPlayers;
                return false;
            }

            bool assigned = LensAssigner.TryAssign(
                state.Definition, _sessionSeed, _connectedCount, _lensRules,
                out LensAssignment? assignment, out failure);

            state.Lenses = assigned ? assignment : null;
            return assigned;
        }

        /// <summary>
        /// The lens a player should render this room through. Null when the room is not staged —
        /// the client must not render props until it is, or it will show everyone variant zero.
        /// </summary>
        public Lens? LensFor(PlayerId player, RoomId room)
        {
            if (!_rooms.TryGetValue(room.Value, out RoomState state) || state.Lenses == null)
            {
                return null;
            }

            int slot = SlotOf(player);
            return slot < state.Lenses.PlayerCount ? state.Lenses.For(slot) : null;
        }

        public RoomDefinition? DefinitionOf(RoomId room)
            => _rooms.TryGetValue(room.Value, out RoomState state) ? state.Definition : null;

        /// <summary>Record that a player walked into a room. Emits protocol violations as warranted.</summary>
        public void PlayerEnteredRoom(PlayerId player, RoomId room)
        {
            int slot = SlotOf(player);

            RoomId previous = _playerRoom[slot];
            if (previous == room)
            {
                return;
            }

            if (!previous.IsNone)
            {
                _protocol.PlayerLeftRoom(player, previous, _clock);
            }

            _playerRoom[slot] = room;
            _protocol.PlayerEnteredRoom(player, room, _clock);
        }

        public void PlayerLeftRoom(PlayerId player)
        {
            int slot = SlotOf(player);
            RoomId previous = _playerRoom[slot];

            if (previous.IsNone)
            {
                return;
            }

            _playerRoom[slot] = RoomId.None;
            _protocol.PlayerLeftRoom(player, previous, _clock);
        }

        public void DoorForced(PlayerId player, RoomId room)
        {
            _protocol.DoorForced(player, room, _clock);
        }

        // ---- puzzles -----------------------------------------------------------------------

        /// <summary>
        /// Adjudicate a puzzle attempt. This is the only way puzzle state ever changes, and it takes
        /// the tokens the player entered — never a "solved" claim. A client that submits the right
        /// answer to a locked node gets <see cref="AttemptOutcome.Locked"/>, not a shortcut.
        /// </summary>
        public AttemptOutcome SubmitPuzzle(PlayerId player, RoomId room, PuzzleNodeId node, ReadOnlySpan<int> tokens)
        {
            if (!_rooms.TryGetValue(room.Value, out RoomState state))
            {
                return AttemptOutcome.UnknownNode;
            }

            // A player can only work the room they are standing in.
            if (_playerRoom[SlotOf(player)] != room)
            {
                return AttemptOutcome.Locked;
            }

            bool wasComplete = state.Puzzles.IsComplete;
            AttemptOutcome outcome = state.Puzzles.Submit(player, node, tokens);

            if (outcome == AttemptOutcome.Accepted)
            {
                Raise(new SessionEvent(SessionEventKind.PuzzleSolved, player, room, node, _clock));

                if (!wasComplete && state.Puzzles.IsComplete)
                {
                    state.EverCompleted = true;
                    Raise(new SessionEvent(SessionEventKind.RoomCompleted, player, room, node, _clock));
                }
            }

            return outcome;
        }

        public bool IsPuzzleSolved(RoomId room, PuzzleNodeId node)
            => _rooms.TryGetValue(room.Value, out RoomState state) && state.Puzzles.IsSolved(node);

        public bool IsRoomComplete(RoomId room)
            => _rooms.TryGetValue(room.Value, out RoomState state) && state.Puzzles.IsComplete;

        // ---- tick --------------------------------------------------------------------------

        /// <summary>
        /// Advance the session. Call from the server's fixed tick. Drains protocol violations into
        /// the Attendant and steps its state machine.
        /// </summary>
        public void Tick(float deltaSeconds) => Tick(deltaSeconds, null);

        /// <summary>
        /// Tick with navigation truth supplied by the Runtime layer, which is the accurate form.
        /// Use this from the Attendant's NetworkBehaviour once a NavMeshAgent exists; the overload
        /// without it infers arrival from room occupancy, which is good enough for tests and for
        /// running headless.
        /// </summary>
        public void Tick(float deltaSeconds, bool attendantHasReachedTarget)
            => Tick(deltaSeconds, (bool?)attendantHasReachedTarget);

        private void Tick(float deltaSeconds, bool? attendantHasReachedTarget)
        {
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            _clock += deltaSeconds;
            _protocol.Tick(_clock);

            int drained = _protocol.Drain(_violationScratch);
            for (int i = 0; i < drained; i++)
            {
                ref ProtocolViolation violation = ref _violationScratch[i];
                _attendant.Report(in violation);
                Raise(new SessionEvent(
                    SessionEventKind.ProtocolViolation, violation.Player, violation.Room,
                    PuzzleNodeId.None, violation.AtTime, (int)violation.Kind));
            }

            AttendantContext context = BuildAttendantContext();
            if (attendantHasReachedTarget.HasValue)
            {
                context = new AttendantContext(
                    context.OffenderRoom,
                    context.TargetRoomIsBeingWorked,
                    attendantHasReachedTarget.Value,
                    context.OffenderReturnedToSession);
            }

            AttendantState previousState = _attendant.State;
            _attendant.Tick(context, deltaSeconds);

            if (_attendant.State != previousState)
            {
                Raise(new SessionEvent(
                    SessionEventKind.AttendantStateChanged, _attendant.Offender, _attendant.TargetRoom,
                    PuzzleNodeId.None, _clock, (int)_attendant.State));
            }
        }

        private AttendantContext BuildAttendantContext()
        {
            PlayerId offender = _attendant.Offender;
            RoomId offenderRoom = RoomId.None;
            bool offenderWorking = false;

            if (!offender.IsNone && _playerConnected[offender.Value])
            {
                offenderRoom = _playerRoom[offender.Value];
                offenderWorking = !offenderRoom.IsNone && !IsRoomComplete(offenderRoom);
            }

            RoomId target = offenderRoom.IsNone ? _attendant.TargetRoom : offenderRoom;

            // The rule the whole game rests on: a room with players still working it cannot be
            // entered. "Being worked" means someone is inside and the puzzles are unfinished.
            bool targetBeingWorked = !target.IsNone && !IsRoomComplete(target) && AnyoneIn(target);

            // Reaching the target is a navigation fact the Runtime layer owns; Core assumes arrival
            // once the offender is not moving between rooms. The NetBehaviour overrides this with
            // real NavMesh data via TickWithNavigation.
            bool reached = !target.IsNone && offenderRoom == target;

            return new AttendantContext(offenderRoom, targetBeingWorked, reached, offenderWorking);
        }

        private bool AnyoneIn(RoomId room)
        {
            for (int slot = 0; slot < _maxPlayers; slot++)
            {
                if (_playerConnected[slot] && _playerRoom[slot] == room)
                {
                    return true;
                }
            }

            return false;
        }

        // ---- events ------------------------------------------------------------------------

        public int PendingEventCount => _eventCount;

        /// <summary>Move buffered events out in order. The netcode layer calls this each tick and replicates them.</summary>
        public int DrainEvents(Span<SessionEvent> destination)
        {
            int count = Math.Min(destination.Length, _eventCount);

            for (int i = 0; i < count; i++)
            {
                destination[i] = _events[(_eventHead + i) % _events.Length];
            }

            _eventHead = (_eventHead + count) % _events.Length;
            _eventCount -= count;

            return count;
        }

        private void Raise(in SessionEvent sessionEvent)
        {
            if (_eventCount == _events.Length)
            {
                _droppedEvents++;
                return;
            }

            _events[(_eventHead + _eventCount) % _events.Length] = sessionEvent;
            _eventCount++;
        }

        // ---- IRoomProgressSource -----------------------------------------------------------

        bool IRoomProgressSource.IsComplete(RoomId room) => IsRoomComplete(room);

        float IRoomProgressSource.TimeAllowanceSeconds(RoomId room)
            => _rooms.TryGetValue(room.Value, out RoomState state) ? state.Definition.TimeAllowanceSeconds : 0f;

        // ---- internals ---------------------------------------------------------------------

        private void RestageAllRooms()
        {
            foreach (KeyValuePair<int, RoomState> entry in _rooms)
            {
                TryStageRoom(new RoomId(entry.Key), out _);
            }
        }

        private int SlotOf(PlayerId player)
        {
            if (player.Value < 0 || player.Value >= _maxPlayers)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(player), "Player slot " + player.Value + " is outside max players.");
            }

            return player.Value;
        }
    }
}
