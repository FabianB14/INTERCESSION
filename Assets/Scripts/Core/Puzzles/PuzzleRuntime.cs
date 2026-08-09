using System;
using Session.Core.Identity;

namespace Session.Core.Puzzles
{
    public enum AttemptOutcome
    {
        /// <summary>Correct. The node is now solved.</summary>
        Accepted = 0,

        /// <summary>Wrong tokens. Nothing changed.</summary>
        Rejected = 1,

        /// <summary>Prerequisite nodes are unsolved. The player should not have been able to interact at all.</summary>
        Locked = 2,

        /// <summary>Already solved. Idempotent, not an error — expect this from duplicated RPCs.</summary>
        AlreadySolved = 3,

        /// <summary>No such node in this room. A malformed or hostile client message.</summary>
        UnknownNode = 4
    }

    /// <summary>
    /// Mutable solve state for one room. Server-side only.
    ///
    /// This type deliberately exposes no way to set a node solved directly. The only path from
    /// unsolved to solved is <see cref="Submit"/> with tokens that match the canonical solution the
    /// server holds. A client can say "I entered 4-1-7-2"; it can never say "the puzzle is solved".
    /// Keep it that way — adding a SetSolved method would hand every cheater the exit door.
    /// </summary>
    public sealed class PuzzleRuntime
    {
        private readonly PuzzleGraph _graph;
        private readonly bool[] _solved;
        private int _solvedCount;
        private int _exitsRemaining;

        public PuzzleRuntime(PuzzleGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _solved = new bool[graph.NodeCount];
            Reset();
        }

        /// <summary>All exit nodes solved. The door opens; the room is finished for Attendant purposes.</summary>
        public bool IsComplete => _exitsRemaining == 0;

        public int SolvedCount => _solvedCount;

        public int NodeCount => _graph.NodeCount;

        public void Reset()
        {
            Array.Clear(_solved, 0, _solved.Length);
            _solvedCount = 0;
            _exitsRemaining = 0;

            for (int i = 0; i < _graph.NodeCount; i++)
            {
                if (_graph.NodeAt(i).IsRoomExit)
                {
                    _exitsRemaining++;
                }
            }
        }

        public bool IsSolved(PuzzleNodeId id)
        {
            return _graph.TryGetOrdinal(id, out int ordinal) && _solved[ordinal];
        }

        /// <summary>Prerequisites satisfied, so the node can currently be attempted.</summary>
        public bool IsUnlocked(PuzzleNodeId id)
        {
            return _graph.TryGetOrdinal(id, out int ordinal) && IsUnlockedByOrdinal(ordinal);
        }

        /// <summary>
        /// The only mutation path. <paramref name="actor"/> is recorded for the Attendant and for
        /// telemetry, and is deliberately not trusted for anything else.
        /// </summary>
        public AttemptOutcome Submit(PlayerId actor, PuzzleNodeId id, ReadOnlySpan<int> tokens)
        {
            if (!_graph.TryGetOrdinal(id, out int ordinal))
            {
                return AttemptOutcome.UnknownNode;
            }

            if (_solved[ordinal])
            {
                return AttemptOutcome.AlreadySolved;
            }

            if (!IsUnlockedByOrdinal(ordinal))
            {
                return AttemptOutcome.Locked;
            }

            PuzzleNode node = _graph.NodeAt(ordinal);
            if (!node.Solution.Matches(tokens))
            {
                return AttemptOutcome.Rejected;
            }

            _solved[ordinal] = true;
            _solvedCount++;

            if (node.IsRoomExit)
            {
                _exitsRemaining--;
            }

            return AttemptOutcome.Accepted;
        }

        private bool IsUnlockedByOrdinal(int ordinal)
        {
            ReadOnlySpan<int> prereqs = _graph.PrerequisiteOrdinals(ordinal);
            for (int i = 0; i < prereqs.Length; i++)
            {
                if (!_solved[prereqs[i]])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
