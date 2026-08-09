using Session.Core.Identity;

namespace Session.Core.Determinism
{
    /// <summary>
    /// Derives independent sub-seeds from one session seed. SplitMix64 finalizer.
    ///
    /// Why this exists: a player's lens must not shift when a different player joins, leaves,
    /// or is assigned first. Each (session, room, player) triple gets its own stream, so adding
    /// a fourth player cannot perturb the first three's rooms.
    /// </summary>
    public static class SeedMixer
    {
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

        public static ulong Mix(ulong z)
        {
            unchecked
            {
                z += GoldenGamma;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public static ulong Combine(ulong a, ulong b)
        {
            unchecked
            {
                return Mix(Mix(a) ^ (b + GoldenGamma));
            }
        }

        /// <summary>Stream for room-wide decisions: which clues exist, who owns them.</summary>
        public static ulong ForRoom(ulong sessionSeed, RoomId room)
        {
            return Combine(sessionSeed, (ulong)(uint)room.Value ^ 0x5F3A_C001UL);
        }

        /// <summary>
        /// Stream for one player's variant choices inside one room. Independent of player count
        /// and of the order players were assigned in.
        /// </summary>
        public static ulong ForPlayer(ulong sessionSeed, RoomId room, PlayerId player)
        {
            ulong roomSeed = ForRoom(sessionSeed, room);
            return Combine(roomSeed, (ulong)(uint)player.Value ^ 0xA11E_11A5UL);
        }
    }
}
