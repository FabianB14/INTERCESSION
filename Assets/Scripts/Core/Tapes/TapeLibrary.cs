using System;
using System.Collections.Generic;
using Session.Core.Identity;

namespace Session.Core.Tapes
{
    /// <summary>
    /// Which tapes the group has found, and which they have actually heard through.
    ///
    /// The distinction matters. Finding a tape is a discovery; hearing it to the end is a
    /// commitment made with the Attendant walking somewhere in the building. Tracking them
    /// separately is what lets a collection screen show "9 found, 4 heard" — which is a far more
    /// honest picture of a run than a single counter, and per LORE.md the tapes are the story, so
    /// the difference is worth surfacing.
    ///
    /// Session-wide and shared: tapes are canonical, so one player finding a tape means the group
    /// has found it.
    /// </summary>
    public sealed class TapeLibrary
    {
        [Flags]
        private enum TapeStatus
        {
            Unknown = 0,
            Found = 1 << 0,
            Heard = 1 << 1
        }

        private readonly Dictionary<int, TapeStatus> _status = new Dictionary<int, TapeStatus>();
        private int _foundCount;
        private int _heardCount;

        /// <summary>Raised when a tape is first found, or first heard through.</summary>
        public event Action<TapeId>? Changed;

        public int FoundCount => _foundCount;

        public int HeardCount => _heardCount;

        public bool IsFound(TapeId tape) => Has(tape, TapeStatus.Found);

        public bool IsHeard(TapeId tape) => Has(tape, TapeStatus.Heard);

        /// <summary>Returns true only the first time this tape is found.</summary>
        public bool MarkFound(TapeId tape)
        {
            if (tape.IsNone || Has(tape, TapeStatus.Found))
            {
                return false;
            }

            _status.TryGetValue(tape.Value, out TapeStatus current);
            _status[tape.Value] = current | TapeStatus.Found;
            _foundCount++;

            Changed?.Invoke(tape);
            return true;
        }

        /// <summary>
        /// Returns true only the first time this tape is heard through. Implies found — a tape can
        /// be heard without ever having been formally picked up, and a run that recorded the second
        /// but not the first would report nonsense.
        /// </summary>
        public bool MarkHeard(TapeId tape)
        {
            if (tape.IsNone || Has(tape, TapeStatus.Heard))
            {
                return false;
            }

            _status.TryGetValue(tape.Value, out TapeStatus current);

            if ((current & TapeStatus.Found) == 0)
            {
                _foundCount++;
            }

            _status[tape.Value] = current | TapeStatus.Found | TapeStatus.Heard;
            _heardCount++;

            Changed?.Invoke(tape);
            return true;
        }

        public void Clear()
        {
            _status.Clear();
            _foundCount = 0;
            _heardCount = 0;
        }

        private bool Has(TapeId tape, TapeStatus flag)
            => !tape.IsNone && _status.TryGetValue(tape.Value, out TapeStatus current) && (current & flag) != 0;
    }
}
