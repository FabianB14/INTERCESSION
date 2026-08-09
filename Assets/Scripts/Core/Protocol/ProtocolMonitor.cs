using System;
using Session.Core.Attendant;
using Session.Core.Identity;

namespace Session.Core.Protocol
{
    /// <summary>
    /// Room facts the monitor needs. Backed on the server by the live PuzzleRuntime set.
    /// </summary>
    public interface IRoomProgressSource
    {
        /// <summary>The room's exit puzzles are solved right now.</summary>
        bool IsComplete(RoomId room);

        /// <summary>Seconds the group may spend in this room before it counts as a violation.</summary>
        float TimeAllowanceSeconds(RoomId room);
    }

    /// <summary>
    /// Watches player movement between rooms and emits protocol violations. This is the only place
    /// that decides what counts as breaking the Protocol; <see cref="AttendantMachine"/> decides
    /// what to do about it.
    ///
    /// Fixed-capacity throughout: player state is an array indexed by player slot, and violations
    /// land in a ring buffer that the server drains each tick. Nothing here allocates after
    /// construction.
    /// </summary>
    public sealed class ProtocolMonitor
    {
        private struct PlayerTracking
        {
            public RoomId CurrentRoom;
            public float EnteredAt;
            public bool TimeViolationRaised;
            public bool Active;
        }

        private readonly IRoomProgressSource _progress;
        private readonly PlayerTracking[] _players;
        private readonly ProtocolViolation[] _pending;
        private int _pendingHead;
        private int _pendingCount;
        private int _droppedViolations;

        public ProtocolMonitor(IRoomProgressSource progress, int maxPlayers = 4, int violationCapacity = 32)
        {
            if (maxPlayers <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            }

            if (violationCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(violationCapacity));
            }

            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _players = new PlayerTracking[maxPlayers];
            _pending = new ProtocolViolation[violationCapacity];
        }

        /// <summary>Violations discarded because the buffer filled. Should always be zero; assert on it in dev builds.</summary>
        public int DroppedViolations => _droppedViolations;

        public int PendingCount => _pendingCount;

        public void PlayerEnteredRoom(PlayerId player, RoomId room, float time)
        {
            int slot = SlotOf(player);

            // Walking back into a room they already finished. Patients do not do this.
            if (_progress.IsComplete(room))
            {
                Raise(new ProtocolViolation(ViolationKind.BacktrackedIntoCompletedRoom, player, room, time));
            }

            _players[slot].CurrentRoom = room;
            _players[slot].EnteredAt = time;
            _players[slot].TimeViolationRaised = false;
            _players[slot].Active = true;
        }

        public void PlayerLeftRoom(PlayerId player, RoomId room, float time)
        {
            int slot = SlotOf(player);

            // The third principle. No room may be left unfinished.
            if (!room.IsNone && !_progress.IsComplete(room))
            {
                Raise(new ProtocolViolation(ViolationKind.LeftRoomUnfinished, player, room, time));
            }

            if (_players[slot].CurrentRoom == room)
            {
                _players[slot].CurrentRoom = RoomId.None;
                _players[slot].TimeViolationRaised = false;
            }
        }

        public void DoorForced(PlayerId player, RoomId room, float time)
        {
            Raise(new ProtocolViolation(ViolationKind.ForcedDoor, player, room, time));
        }

        public void PlayerDisconnected(PlayerId player)
        {
            int slot = SlotOf(player);
            _players[slot] = default;
        }

        /// <summary>
        /// Call once per server tick. Raises the time-allowance violation at most once per stay,
        /// so a group that overruns is escalated once rather than every frame.
        /// </summary>
        public void Tick(float time)
        {
            for (int slot = 0; slot < _players.Length; slot++)
            {
                ref PlayerTracking tracking = ref _players[slot];

                if (!tracking.Active || tracking.CurrentRoom.IsNone || tracking.TimeViolationRaised)
                {
                    continue;
                }

                if (_progress.IsComplete(tracking.CurrentRoom))
                {
                    continue;
                }

                float allowance = _progress.TimeAllowanceSeconds(tracking.CurrentRoom);
                if (allowance <= 0f || time - tracking.EnteredAt < allowance)
                {
                    continue;
                }

                tracking.TimeViolationRaised = true;
                Raise(new ProtocolViolation(
                    ViolationKind.TimeAllowanceExceeded, new PlayerId(slot), tracking.CurrentRoom, time));
            }
        }

        /// <summary>
        /// Move buffered violations into <paramref name="destination"/> in the order they occurred.
        /// Returns how many were written.
        /// </summary>
        public int Drain(Span<ProtocolViolation> destination)
        {
            int count = Math.Min(destination.Length, _pendingCount);

            for (int i = 0; i < count; i++)
            {
                destination[i] = _pending[(_pendingHead + i) % _pending.Length];
            }

            _pendingHead = (_pendingHead + count) % _pending.Length;
            _pendingCount -= count;

            return count;
        }

        private void Raise(in ProtocolViolation violation)
        {
            if (_pendingCount == _pending.Length)
            {
                _droppedViolations++;
                return;
            }

            _pending[(_pendingHead + _pendingCount) % _pending.Length] = violation;
            _pendingCount++;
        }

        private int SlotOf(PlayerId player)
        {
            if (player.Value < 0 || player.Value >= _players.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(player), "Player slot " + player.Value + " is outside the configured max players.");
            }

            return player.Value;
        }
    }
}
