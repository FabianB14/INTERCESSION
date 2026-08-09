using System;
using System.Collections.Generic;
using Session.Core.Content;
using Session.Core.Identity;
using Session.Core.Puzzles;
using Session.Core.Rooms;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// Authoring data for one room. Designers edit this; <see cref="Build"/> turns it into the
    /// immutable <see cref="RoomDefinition"/> that Session.Core reasons about.
    ///
    /// Rooms are numbered, never named — see LORE.md. The <c>_designerNote</c> field is for the
    /// team and never reaches the player.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Room Layout", fileName = "SO_RoomLayout")]
    public sealed class RoomLayoutSO : ScriptableObject
    {
        [Serializable]
        public sealed class VariantEntry
        {
            [Tooltip("Stable id within this prop. Changing it re-rolls what players see.")]
            public int VariantId;

            [Tooltip("Content key for the name this player would use out loud, e.g. prop.curtain.hospital")]
            public string DisplayNameKey = string.Empty;

            [Tooltip("Content key for legible surface text. Leave empty on concealing variants.")]
            public string SurfaceTextKey = string.Empty;

            [Tooltip("Does this rendering expose the prop's clue to whoever sees it?")]
            public bool RevealsClue;
        }

        [Serializable]
        public sealed class PropEntry
        {
            public int PropId;

            [Tooltip("0 for set dressing. Otherwise the clue this prop carries — exactly one prop per clue.")]
            public int ClueId;

            public List<VariantEntry> Variants = new List<VariantEntry>();
        }

        [Serializable]
        public sealed class PuzzleNodeEntry
        {
            public int NodeId;

            public SolutionKind SolutionKind = SolutionKind.Ordered;

            [Tooltip("The canonical answer. Same for every player, whatever surface they read it from.")]
            public List<int> SolutionTokens = new List<int>();

            [Tooltip("Clues needed to know this answer.")]
            public List<int> RequiredClueIds = new List<int>();

            [Tooltip("Nodes that must be solved before this one can be attempted.")]
            public List<int> RequiredNodeIds = new List<int>();

            [Tooltip("Solving this opens the room's exit door.")]
            public bool IsRoomExit;
        }

        [Header("Identity")]
        [SerializeField, Min(1)] private int _roomNumber = 1;

        [SerializeField, TextArea] private string _designerNote = string.Empty;

        [Header("Pacing")]
        [Tooltip("Seconds before the Attendant treats the stay as a protocol violation. 0 = no limit.")]
        [SerializeField, Min(0f)] private float _timeAllowanceSeconds = 300f;

        [Header("Contents")]
        [SerializeField] private List<PropEntry> _props = new List<PropEntry>();

        [SerializeField] private List<PuzzleNodeEntry> _puzzleNodes = new List<PuzzleNodeEntry>();

        public int RoomNumber => _roomNumber;

        public float TimeAllowanceSeconds => _timeAllowanceSeconds;

        /// <summary>
        /// Convert authoring data into the runtime model. Throws with a message naming this asset
        /// if the authoring is inconsistent — the Editor validator calls this so problems surface
        /// at author time rather than when four players are already in the room.
        /// </summary>
        public RoomDefinition Build()
        {
            try
            {
                var props = new PropDefinition[_props.Count];
                for (int i = 0; i < _props.Count; i++)
                {
                    props[i] = BuildProp(_props[i]);
                }

                var nodes = new PuzzleNode[_puzzleNodes.Count];
                for (int i = 0; i < _puzzleNodes.Count; i++)
                {
                    nodes[i] = BuildNode(_puzzleNodes[i]);
                }

                return new RoomDefinition(
                    new RoomId(_roomNumber), props, new PuzzleGraph(nodes), _timeAllowanceSeconds);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Room layout '" + name + "' (Room " + _roomNumber + ") is invalid: " + exception.Message,
                    exception);
            }
        }

        private static PropDefinition BuildProp(PropEntry entry)
        {
            var variants = new PropVariant[entry.Variants.Count];
            for (int i = 0; i < entry.Variants.Count; i++)
            {
                VariantEntry variant = entry.Variants[i];
                variants[i] = new PropVariant(
                    new VariantId(variant.VariantId),
                    ContentKey.Of(variant.DisplayNameKey),
                    ContentKey.Of(variant.SurfaceTextKey),
                    variant.RevealsClue);
            }

            return new PropDefinition(new PropId(entry.PropId), new ClueId(entry.ClueId), variants);
        }

        private static PuzzleNode BuildNode(PuzzleNodeEntry entry)
        {
            var clues = new ClueId[entry.RequiredClueIds.Count];
            for (int i = 0; i < clues.Length; i++)
            {
                clues[i] = new ClueId(entry.RequiredClueIds[i]);
            }

            var prerequisites = new PuzzleNodeId[entry.RequiredNodeIds.Count];
            for (int i = 0; i < prerequisites.Length; i++)
            {
                prerequisites[i] = new PuzzleNodeId(entry.RequiredNodeIds[i]);
            }

            return new PuzzleNode(
                new PuzzleNodeId(entry.NodeId),
                new Solution(entry.SolutionKind, entry.SolutionTokens.ToArray()),
                clues,
                prerequisites,
                entry.IsRoomExit);
        }
    }
}
