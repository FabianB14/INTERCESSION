using Session.Core.Identity;

namespace Session.Core.Perception
{
    /// <summary>
    /// The full set of lenses for one room, one session, one group. Produced by
    /// <see cref="LensAssigner"/> and guaranteed by construction to satisfy the interdependence
    /// invariant — no player can finish this room alone.
    /// </summary>
    public sealed class LensAssignment
    {
        private readonly Lens[] _byPlayerIndex;

        public readonly RoomId Room;

        /// <summary>The session seed this was derived from. Re-deriving with it reproduces these lenses exactly.</summary>
        public readonly ulong SessionSeed;

        internal LensAssignment(RoomId room, ulong sessionSeed, Lens[] byPlayerIndex)
        {
            Room = room;
            SessionSeed = sessionSeed;
            _byPlayerIndex = byPlayerIndex;
        }

        public int PlayerCount => _byPlayerIndex.Length;

        public Lens For(int playerIndex) => _byPlayerIndex[playerIndex];

        public Lens For(PlayerId player) => _byPlayerIndex[player.Value];
    }

    public enum LensAssignmentFailure
    {
        None = 0,

        TooFewPlayers = 1,

        TooManyPlayers = 2,

        /// <summary>
        /// The room has fewer required clues than players. There is no way to give everyone
        /// something to contribute, so the room is under-authored for this group size.
        /// </summary>
        NotEnoughRequiredClues = 3,

        /// <summary>A clue-carrying prop has no variant that exposes its clue. Nobody could ever read it.</summary>
        PropMissingRevealingVariant = 4,

        /// <summary>A clue-carrying prop has no variant that conceals its clue, so the clue cannot be withheld.</summary>
        PropMissingConcealingVariant = 5,

        /// <summary>A required clue has no prop carrying it anywhere in the room.</summary>
        RequiredClueHasNoProp = 6
    }
}
