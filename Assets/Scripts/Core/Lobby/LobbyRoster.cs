using System;

namespace Session.Core.Lobby
{
    /// <summary>
    /// Who is in the lobby and whether they are ready.
    ///
    /// Lives in Core rather than in the lobby UI because the start gate is a rule, not a
    /// presentation detail: a run cannot begin below the minimum group size, no matter what the
    /// button says. Rooms are authored so that every player holds a clue nobody else does, so
    /// starting with one player is not "hard mode" — it is an unsolvable room.
    /// </summary>
    public sealed class LobbyRoster
    {
        private struct Slot
        {
            public bool Occupied;
            public bool Ready;
            public ulong SteamId;
        }

        private readonly Slot[] _slots;
        private readonly int _minPlayers;

        public LobbyRoster(int maxPlayers = 4, int minPlayers = 2)
        {
            if (maxPlayers < minPlayers)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Max players is below the minimum.");
            }

            if (minPlayers < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minPlayers), "Session is co-op by construction; two players minimum.");
            }

            _slots = new Slot[maxPlayers];
            _minPlayers = minPlayers;
        }

        public int Capacity => _slots.Length;

        public int MinPlayers => _minPlayers;

        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Occupied)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool IsFull => Count == _slots.Length;

        /// <summary>Raised whenever membership or ready state changes.</summary>
        public event Action? Changed;

        /// <summary>
        /// The only condition under which a run may start: enough players, and all of them ready.
        /// </summary>
        public bool CanStart
        {
            get
            {
                int occupied = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (!_slots[i].Occupied)
                    {
                        continue;
                    }

                    occupied++;
                    if (!_slots[i].Ready)
                    {
                        return false;
                    }
                }

                return occupied >= _minPlayers;
            }
        }

        public bool TryAdd(ulong steamId, out int slot)
        {
            // Rejoin: an existing member keeps their slot rather than taking a second one.
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Occupied && _slots[i].SteamId == steamId)
                {
                    slot = i;
                    return true;
                }
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Occupied)
                {
                    continue;
                }

                _slots[i].Occupied = true;
                _slots[i].Ready = false;
                _slots[i].SteamId = steamId;

                slot = i;
                Changed?.Invoke();
                return true;
            }

            slot = -1;
            return false;
        }

        public void Remove(int slot)
        {
            ThrowIfOutOfRange(slot);

            if (!_slots[slot].Occupied)
            {
                return;
            }

            _slots[slot] = default;
            Changed?.Invoke();
        }

        public void SetReady(int slot, bool ready)
        {
            ThrowIfOutOfRange(slot);

            if (!_slots[slot].Occupied || _slots[slot].Ready == ready)
            {
                return;
            }

            _slots[slot].Ready = ready;
            Changed?.Invoke();
        }

        public bool IsOccupied(int slot)
        {
            ThrowIfOutOfRange(slot);
            return _slots[slot].Occupied;
        }

        public bool IsReady(int slot)
        {
            ThrowIfOutOfRange(slot);
            return _slots[slot].Occupied && _slots[slot].Ready;
        }

        public ulong SteamIdAt(int slot)
        {
            ThrowIfOutOfRange(slot);
            return _slots[slot].SteamId;
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            Changed?.Invoke();
        }

        private void ThrowIfOutOfRange(int slot)
        {
            if (slot < 0 || slot >= _slots.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), "Lobby slot " + slot + " does not exist.");
            }
        }
    }
}
