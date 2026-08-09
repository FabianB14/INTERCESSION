using Session.Core.Identity;

namespace Session.Core.Puzzles
{
    /// <summary>
    /// One step of "the honest question". Immutable after load.
    ///
    /// A node is <i>unlocked</i> when its prerequisite nodes are solved, and <i>solvable in
    /// practice</i> when the players between them can read every clue it requires. Core enforces
    /// the first; the lens validator guarantees the second is only ever true for the group, never
    /// for one player alone.
    /// </summary>
    public sealed class PuzzleNode
    {
        private static readonly PuzzleNodeId[] NoNodes = new PuzzleNodeId[0];
        private static readonly ClueId[] NoClues = new ClueId[0];

        private readonly PuzzleNodeId[] _requiredNodes;
        private readonly ClueId[] _requiredClues;

        public readonly PuzzleNodeId Id;

        /// <summary>The canonical answer. Never sent to clients.</summary>
        public readonly Solution Solution;

        /// <summary>Solving this node opens the room's exit door.</summary>
        public readonly bool IsRoomExit;

        public PuzzleNode(
            PuzzleNodeId id,
            Solution solution,
            ClueId[]? requiredClues = null,
            PuzzleNodeId[]? requiredNodes = null,
            bool isRoomExit = false)
        {
            if (id.IsNone)
            {
                throw new System.ArgumentException("Puzzle node id must not be None.", nameof(id));
            }

            if (!solution.IsValid)
            {
                throw new System.ArgumentException("Puzzle node needs a valid solution.", nameof(solution));
            }

            Id = id;
            Solution = solution;
            IsRoomExit = isRoomExit;
            _requiredClues = requiredClues ?? NoClues;
            _requiredNodes = requiredNodes ?? NoNodes;
        }

        public int RequiredNodeCount => _requiredNodes.Length;

        public PuzzleNodeId RequiredNodeAt(int index) => _requiredNodes[index];

        public int RequiredClueCount => _requiredClues.Length;

        public ClueId RequiredClueAt(int index) => _requiredClues[index];
    }
}
