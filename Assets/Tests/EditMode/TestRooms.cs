using System.Collections.Generic;
using Session.Core.Identity;
using Session.Core.Puzzles;
using Session.Core.Rooms;

namespace Session.Tests.Core
{
    /// <summary>
    /// Fixture builders. Rooms here are structurally representative of shipped rooms but carry no
    /// content — content keys are indices, not strings, precisely so tests never depend on copy.
    /// </summary>
    internal static class TestRooms
    {
        /// <summary>A clue-carrying prop with one revealing and one concealing variant.</summary>
        internal static PropDefinition ClueProp(int propId, int clueId, int variantCount = 2)
        {
            var variants = new List<PropVariant>(variantCount);

            // Alternate so both kinds always exist, and both appear at more than one index for
            // rooms that ask for several.
            for (int i = 0; i < variantCount; i++)
            {
                bool reveals = i % 2 == 0;
                variants.Add(new PropVariant(
                    new VariantId(i),
                    displayNameKey: propId * 100 + i,
                    surfaceTextKey: reveals ? clueId * 1000 + i : 0,
                    revealsClue: reveals));
            }

            return new PropDefinition(new PropId(propId), new ClueId(clueId), variants.ToArray());
        }

        /// <summary>Set dressing. No clue, several appearances.</summary>
        internal static PropDefinition DressingProp(int propId, int variantCount = 3)
        {
            var variants = new PropVariant[variantCount];
            for (int i = 0; i < variantCount; i++)
            {
                variants[i] = new PropVariant(new VariantId(i), propId * 100 + i, 0, false);
            }

            return new PropDefinition(new PropId(propId), ClueId.None, variants);
        }

        /// <summary>
        /// A room with <paramref name="clueCount"/> required clues feeding a single exit node,
        /// plus some set dressing. The shape of an early Institute room.
        /// </summary>
        internal static RoomDefinition Simple(int roomId, int clueCount, int dressingCount = 3, float timeAllowance = 300f)
        {
            var props = new List<PropDefinition>(clueCount + dressingCount);
            var clues = new ClueId[clueCount];

            for (int i = 0; i < clueCount; i++)
            {
                int clueId = roomId * 1000 + i + 1;
                clues[i] = new ClueId(clueId);
                props.Add(ClueProp(roomId * 100 + i + 1, clueId));
            }

            for (int i = 0; i < dressingCount; i++)
            {
                props.Add(DressingProp(roomId * 100 + 500 + i));
            }

            var exit = new PuzzleNode(
                new PuzzleNodeId(1),
                new Solution(SolutionKind.Ordered, 4, 1, 7, 2),
                requiredClues: clues,
                requiredNodes: null,
                isRoomExit: true);

            return new RoomDefinition(new RoomId(roomId), props.ToArray(), new PuzzleGraph(new[] { exit }), timeAllowance);
        }

        /// <summary>
        /// A room whose exit depends on an intermediate node, so required clues have to be gathered
        /// transitively. Verifies the graph walk, not just the direct edge.
        /// </summary>
        internal static RoomDefinition Chained(int roomId, float timeAllowance = 300f)
        {
            var props = new List<PropDefinition>
            {
                ClueProp(roomId * 100 + 1, roomId * 1000 + 1),
                ClueProp(roomId * 100 + 2, roomId * 1000 + 2),
                ClueProp(roomId * 100 + 3, roomId * 1000 + 3),
                ClueProp(roomId * 100 + 4, roomId * 1000 + 4),
                DressingProp(roomId * 100 + 501)
            };

            var first = new PuzzleNode(
                new PuzzleNodeId(1),
                new Solution(SolutionKind.Unordered, 3, 9),
                requiredClues: new[] { new ClueId(roomId * 1000 + 1), new ClueId(roomId * 1000 + 2) });

            var exit = new PuzzleNode(
                new PuzzleNodeId(2),
                new Solution(SolutionKind.Ordered, 4, 1, 7, 2),
                requiredClues: new[] { new ClueId(roomId * 1000 + 3), new ClueId(roomId * 1000 + 4) },
                requiredNodes: new[] { new PuzzleNodeId(1) },
                isRoomExit: true);

            return new RoomDefinition(
                new RoomId(roomId), props.ToArray(), new PuzzleGraph(new[] { first, exit }), timeAllowance);
        }
    }
}
