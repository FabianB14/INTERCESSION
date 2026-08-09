using System;
using Session.Core.Identity;

namespace Session.Core.Tapes
{
    /// <summary>One line of transcript, with the window it is spoken in.</summary>
    public readonly struct TranscriptCue
    {
        public readonly float StartSeconds;
        public readonly float EndSeconds;

        /// <summary>Content key for the spoken line.</summary>
        public readonly int TextKey;

        public TranscriptCue(float startSeconds, float endSeconds, int textKey)
        {
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
            TextKey = textKey;
        }

        public float Duration => EndSeconds - StartSeconds;

        public bool Contains(float seconds) => seconds >= StartSeconds && seconds < EndSeconds;
    }

    /// <summary>
    /// An intake tape. Verity's voice, and per LORE.md the highest story-per-pound asset in the
    /// project — one VO actor, a good mic, and a room's worth of blankets.
    ///
    /// <b>Tapes are canonical, not lensed.</b> Every player hears the same words, because a tape is
    /// a recording of a real man talking in 1974, not something the building reconstructed from a
    /// questionnaire. That is a deliberate narrative asymmetry: the tapes are the one thing in the
    /// Institute that is the same for everyone, and they are also the only place the building
    /// speaks in a voice that was ever kind.
    ///
    /// It also has a hard mechanical consequence. Because every player hears a tape identically, a
    /// tape can never carry a puzzle input — if it did, both players would hold the same clue and
    /// the room would need no co-operation at all. There is deliberately no clue-bearing concept in
    /// this file, and <see cref="TapeAudit"/> checks that no transcript leaks an answer anyway.
    /// </summary>
    public sealed class TapeDefinition
    {
        private readonly TranscriptCue[] _cues;

        public readonly TapeId Id;

        public readonly int TitleKey;

        /// <summary>Content key for who is speaking. Almost always Verity; occasionally a nurse.</summary>
        public readonly int SpeakerKey;

        /// <summary>Year on the label. The 1979-1984 drift is told partly through these.</summary>
        public readonly int RecordedYear;

        public readonly float DurationSeconds;

        public TapeDefinition(
            TapeId id, int titleKey, int speakerKey, int recordedYear, float durationSeconds, TranscriptCue[] cues)
        {
            if (id.IsNone)
            {
                throw new ArgumentException("Tape id must not be None.", nameof(id));
            }

            if (durationSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "A tape must have a positive length.");
            }

            _cues = cues ?? Array.Empty<TranscriptCue>();

            // Ordered and non-overlapping, so cue lookup can binary search and so a transcript can
            // never show two lines at once.
            for (int i = 0; i < _cues.Length; i++)
            {
                if (_cues[i].EndSeconds <= _cues[i].StartSeconds)
                {
                    throw new ArgumentException(
                        "Transcript cue " + i + " ends before it starts.", nameof(cues));
                }

                if (_cues[i].StartSeconds < 0f || _cues[i].EndSeconds > durationSeconds)
                {
                    throw new ArgumentException(
                        "Transcript cue " + i + " falls outside the tape's length.", nameof(cues));
                }

                if (i > 0 && _cues[i].StartSeconds < _cues[i - 1].EndSeconds)
                {
                    throw new ArgumentException(
                        "Transcript cue " + i + " overlaps the one before it. Cues must be ordered and disjoint.",
                        nameof(cues));
                }
            }

            Id = id;
            TitleKey = titleKey;
            SpeakerKey = speakerKey;
            RecordedYear = recordedYear;
            DurationSeconds = durationSeconds;
        }

        public int CueCount => _cues.Length;

        public TranscriptCue CueAt(int index) => _cues[index];

        /// <summary>
        /// Index of the cue spoken at <paramref name="seconds"/>, or -1 in a gap, before the first
        /// line, or after the last. Binary search — no allocation, safe to call every frame.
        /// </summary>
        public int CueIndexAt(float seconds)
        {
            int low = 0;
            int high = _cues.Length - 1;

            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                TranscriptCue cue = _cues[middle];

                if (seconds < cue.StartSeconds)
                {
                    high = middle - 1;
                }
                else if (seconds >= cue.EndSeconds)
                {
                    low = middle + 1;
                }
                else
                {
                    return middle;
                }
            }

            return -1;
        }
    }
}
