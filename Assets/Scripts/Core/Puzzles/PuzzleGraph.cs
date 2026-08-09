using System;
using System.Collections.Generic;
using Session.Core.Identity;

namespace Session.Core.Puzzles
{
    /// <summary>
    /// The precondition DAG for one room. Immutable; validated once at construction so that
    /// nothing downstream has to defend against cycles or dangling references.
    ///
    /// Also computes <see cref="RequiredClues"/> — the transitive set of clues needed to reach
    /// every exit. That set is the input to lens assignment: it is exactly the information the
    /// group must hold between them, and therefore exactly what must be split so no one holds
    /// all of it.
    /// </summary>
    public sealed class PuzzleGraph
    {
        private readonly PuzzleNode[] _nodes;
        private readonly Dictionary<PuzzleNodeId, int> _ordinalById;
        private readonly int[][] _prerequisiteOrdinals;
        private readonly ClueId[] _requiredClues;

        public PuzzleGraph(PuzzleNode[] nodes)
        {
            if (nodes == null || nodes.Length == 0)
            {
                throw new ArgumentException("A puzzle graph needs at least one node.", nameof(nodes));
            }

            _nodes = nodes;
            _ordinalById = new Dictionary<PuzzleNodeId, int>(nodes.Length);

            for (int i = 0; i < nodes.Length; i++)
            {
                if (_ordinalById.ContainsKey(nodes[i].Id))
                {
                    throw new ArgumentException("Duplicate puzzle node id: " + nodes[i].Id, nameof(nodes));
                }

                _ordinalById.Add(nodes[i].Id, i);
            }

            _prerequisiteOrdinals = new int[nodes.Length][];
            for (int i = 0; i < nodes.Length; i++)
            {
                PuzzleNode node = nodes[i];
                int[] prereqs = new int[node.RequiredNodeCount];
                for (int j = 0; j < prereqs.Length; j++)
                {
                    PuzzleNodeId required = node.RequiredNodeAt(j);
                    if (!_ordinalById.TryGetValue(required, out int ordinal))
                    {
                        throw new ArgumentException(
                            "Puzzle node " + node.Id + " requires unknown node " + required + ".", nameof(nodes));
                    }

                    if (ordinal == i)
                    {
                        throw new ArgumentException("Puzzle node " + node.Id + " requires itself.", nameof(nodes));
                    }

                    prereqs[j] = ordinal;
                }

                _prerequisiteOrdinals[i] = prereqs;
            }

            ThrowIfCyclic();

            bool hasExit = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].IsRoomExit)
                {
                    hasExit = true;
                    break;
                }
            }

            if (!hasExit)
            {
                throw new ArgumentException(
                    "A room's puzzle graph must contain at least one exit node, or the door never opens.",
                    nameof(nodes));
            }

            _requiredClues = ComputeRequiredClues();
        }

        public int NodeCount => _nodes.Length;

        public PuzzleNode NodeAt(int ordinal) => _nodes[ordinal];

        public bool TryGetOrdinal(PuzzleNodeId id, out int ordinal) => _ordinalById.TryGetValue(id, out ordinal);

        /// <summary>Ordinals of the nodes this one depends on. Empty for entry points.</summary>
        public ReadOnlySpan<int> PrerequisiteOrdinals(int ordinal) => _prerequisiteOrdinals[ordinal];

        /// <summary>
        /// Every clue needed, transitively, to open the exit. Deduplicated, in stable ascending
        /// order so that lens assignment is reproducible regardless of node authoring order.
        /// </summary>
        public ReadOnlySpan<ClueId> RequiredClues => _requiredClues;

        public int RequiredClueCount => _requiredClues.Length;

        private void ThrowIfCyclic()
        {
            // Kahn's algorithm. Runs once at load, so clarity beats cleverness.
            int[] indegree = new int[_nodes.Length];
            for (int i = 0; i < _nodes.Length; i++)
            {
                indegree[i] = _prerequisiteOrdinals[i].Length;
            }

            var ready = new Stack<int>(_nodes.Length);
            for (int i = 0; i < indegree.Length; i++)
            {
                if (indegree[i] == 0)
                {
                    ready.Push(i);
                }
            }

            int settled = 0;
            while (ready.Count > 0)
            {
                int current = ready.Pop();
                settled++;

                for (int i = 0; i < _nodes.Length; i++)
                {
                    int[] prereqs = _prerequisiteOrdinals[i];
                    for (int j = 0; j < prereqs.Length; j++)
                    {
                        if (prereqs[j] != current)
                        {
                            continue;
                        }

                        indegree[i]--;
                        if (indegree[i] == 0)
                        {
                            ready.Push(i);
                        }
                    }
                }
            }

            if (settled != _nodes.Length)
            {
                throw new ArgumentException(
                    "Puzzle graph contains a cycle — some node transitively requires itself, so the room can never be finished.");
            }
        }

        private ClueId[] ComputeRequiredClues()
        {
            var collected = new SortedSet<int>();
            var visited = new bool[_nodes.Length];
            var pending = new Stack<int>(_nodes.Length);

            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i].IsRoomExit)
                {
                    pending.Push(i);
                }
            }

            while (pending.Count > 0)
            {
                int ordinal = pending.Pop();
                if (visited[ordinal])
                {
                    continue;
                }

                visited[ordinal] = true;

                PuzzleNode node = _nodes[ordinal];
                for (int i = 0; i < node.RequiredClueCount; i++)
                {
                    ClueId clue = node.RequiredClueAt(i);
                    if (!clue.IsNone)
                    {
                        collected.Add(clue.Value);
                    }
                }

                int[] prereqs = _prerequisiteOrdinals[ordinal];
                for (int i = 0; i < prereqs.Length; i++)
                {
                    pending.Push(prereqs[i]);
                }
            }

            var result = new ClueId[collected.Count];
            int index = 0;
            foreach (int value in collected)
            {
                result[index++] = new ClueId(value);
            }

            return result;
        }
    }
}
