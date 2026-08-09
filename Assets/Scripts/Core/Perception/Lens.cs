using System;
using Session.Core.Identity;
using Session.Core.Rooms;

namespace Session.Core.Perception
{
    /// <summary>
    /// One player's mapping from prop to variant, for one room.
    ///
    /// Flat arrays indexed by prop ordinal: lookups are a bounds check and an index, so the
    /// rendering layer can query this per-prop without allocating.
    /// </summary>
    public sealed class Lens
    {
        private readonly byte[] _variantIndexByOrdinal;
        private readonly bool[] _revealsClueByOrdinal;

        public readonly RoomId Room;
        public readonly PlayerId Owner;

        internal Lens(RoomId room, PlayerId owner, byte[] variantIndexByOrdinal, bool[] revealsClueByOrdinal)
        {
            Room = room;
            Owner = owner;
            _variantIndexByOrdinal = variantIndexByOrdinal;
            _revealsClueByOrdinal = revealsClueByOrdinal;
        }

        public int PropCount => _variantIndexByOrdinal.Length;

        /// <summary>Which variant of this prop the owner sees.</summary>
        public int VariantIndex(int propOrdinal) => _variantIndexByOrdinal[propOrdinal];

        /// <summary>Whether the owner can read this prop's clue. False for set dressing.</summary>
        public bool RevealsClue(int propOrdinal) => _revealsClueByOrdinal[propOrdinal];

        /// <summary>
        /// Whether the owner can read the given clue anywhere in the room. Used by the validator
        /// and by tests; not on any per-frame path.
        /// </summary>
        public bool CanRead(RoomDefinition room, ClueId clue)
        {
            if (clue.IsNone)
            {
                return false;
            }

            return room.TryGetPropOrdinalForClue(clue, out int ordinal) && _revealsClueByOrdinal[ordinal];
        }

        /// <summary>How many of the room's required clues this lens exposes.</summary>
        public int CountReadableRequiredClues(RoomDefinition room)
        {
            ReadOnlySpan<ClueId> required = room.Puzzles.RequiredClues;
            int count = 0;
            for (int i = 0; i < required.Length; i++)
            {
                if (CanRead(room, required[i]))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
