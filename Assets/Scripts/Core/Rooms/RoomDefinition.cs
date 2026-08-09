using System;
using System.Collections.Generic;
using Session.Core.Identity;
using Session.Core.Puzzles;

namespace Session.Core.Rooms
{
    /// <summary>
    /// The canonical room the server holds: an id, a set of props, and the puzzle graph.
    /// One of these per room, shared by every player in it. Immutable after load.
    ///
    /// Props are addressed by ordinal internally so that lenses can be flat arrays and lookups
    /// stay allocation-free.
    /// </summary>
    public sealed class RoomDefinition
    {
        private readonly PropDefinition[] _props;
        private readonly Dictionary<PropId, int> _ordinalByProp;
        private readonly Dictionary<ClueId, int> _propOrdinalByClue;

        public readonly RoomId Id;

        public readonly PuzzleGraph Puzzles;

        /// <summary>
        /// Seconds the group may spend here before the Attendant treats it as a protocol violation.
        /// Sourced from RoomLayoutSO — never a literal in code.
        /// </summary>
        public readonly float TimeAllowanceSeconds;

        public RoomDefinition(RoomId id, PropDefinition[] props, PuzzleGraph puzzles, float timeAllowanceSeconds)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("Room id must not be None.", nameof(id));
            }

            if (props == null || props.Length == 0)
            {
                throw new ArgumentException("A room needs at least one prop.", nameof(props));
            }

            Id = id;
            Puzzles = puzzles ?? throw new ArgumentNullException(nameof(puzzles));
            TimeAllowanceSeconds = timeAllowanceSeconds;
            _props = props;

            _ordinalByProp = new Dictionary<PropId, int>(props.Length);
            _propOrdinalByClue = new Dictionary<ClueId, int>(props.Length);

            for (int i = 0; i < props.Length; i++)
            {
                PropDefinition prop = props[i];

                if (_ordinalByProp.ContainsKey(prop.Id))
                {
                    throw new ArgumentException("Duplicate prop id in room " + id + ": " + prop.Id, nameof(props));
                }

                _ordinalByProp.Add(prop.Id, i);

                if (!prop.CarriesClue)
                {
                    continue;
                }

                if (_propOrdinalByClue.ContainsKey(prop.Clue))
                {
                    // Two props carrying the same clue would let a player read it from whichever one
                    // their lens happened to reveal, silently defeating the split.
                    throw new ArgumentException(
                        "Clue " + prop.Clue + " is carried by more than one prop in room " + id +
                        ". Each clue must have exactly one source prop.", nameof(props));
                }

                _propOrdinalByClue.Add(prop.Clue, i);
            }
        }

        public int PropCount => _props.Length;

        public PropDefinition PropAt(int ordinal) => _props[ordinal];

        public bool TryGetOrdinal(PropId id, out int ordinal) => _ordinalByProp.TryGetValue(id, out ordinal);

        /// <summary>Which prop carries this clue. False if no prop in the room does.</summary>
        public bool TryGetPropOrdinalForClue(ClueId clue, out int ordinal)
            => _propOrdinalByClue.TryGetValue(clue, out ordinal);
    }
}
