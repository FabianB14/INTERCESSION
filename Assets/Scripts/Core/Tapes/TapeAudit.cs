using System;
using Session.Core.Content;
using Session.Core.Documents;

namespace Session.Core.Tapes
{
    [Flags]
    public enum TapeIssue
    {
        None = 0,

        /// <summary>
        /// A room's canonical answer is spoken on the tape. Because every player hears a tape
        /// identically, this hands the whole group the same clue and the room stops needing
        /// anyone to talk to anyone.
        /// </summary>
        AnswerSpokenOnTape = 1 << 0,

        /// <summary>A cue's content key resolves to nothing. Ships as silence with no subtitle.</summary>
        MissingTranscriptCopy = 1 << 1,

        /// <summary>No cues at all. The tape is unsubtitled, which is an accessibility failure.</summary>
        NoTranscript = 1 << 2,

        /// <summary>
        /// Transcript covers noticeably less of the runtime than expected — usually a sign someone
        /// timed the first half and stopped.
        /// </summary>
        TranscriptCoverageLow = 1 << 3
    }

    public readonly struct TapeAuditResult
    {
        public readonly TapeIssue Issues;

        /// <summary>Index of the first offending cue, or -1.</summary>
        public readonly int CueIndex;

        /// <summary>Fraction of the tape's runtime covered by transcript, 0..1.</summary>
        public readonly float TranscriptCoverage;

        public TapeAuditResult(TapeIssue issues, int cueIndex, float transcriptCoverage)
        {
            Issues = issues;
            CueIndex = cueIndex;
            TranscriptCoverage = transcriptCoverage;
        }

        public bool IsClean => Issues == TapeIssue.None;

        public override string ToString()
        {
            return IsClean
                ? "Tape clean."
                : "Tape issues: " + Issues + " (cue " + CueIndex + ", coverage " +
                  (TranscriptCoverage * 100f).ToString("0") + "%)";
        }
    }

    /// <summary>
    /// Checks an intake tape against the rules that keep it story rather than a solution.
    ///
    /// The one that matters is peculiar to tapes. Paper props can carry clues because each player
    /// reads a different document; a tape is a recording, so everyone hears the same words. Putting
    /// an answer on a tape therefore does not just leak it to one player — it gives it to the whole
    /// group at once, and the room instantly requires no co-operation from anybody.
    ///
    /// That failure is even quieter than the paper equivalent. Nobody involved experiences anything
    /// odd; the room simply turns out to be easy, and a play test reads that as tuning rather than
    /// as the central mechanic having switched itself off.
    ///
    /// Shares the normalised literal matching in <see cref="DocumentAudit"/>, and shares its limit:
    /// it finds an answer spoken as digits, not an answer paraphrased.
    /// </summary>
    public static class TapeAudit
    {
        /// <summary>Below this fraction of the runtime subtitled, flag it.</summary>
        public const float MinimumTranscriptCoverage = 0.5f;

        private const int MaxNeedleLength = 128;

        public static TapeAuditResult Audit(
            TapeDefinition tape,
            ContentTable content,
            ReadOnlySpan<int> forbiddenSolution)
        {
            if (tape == null)
            {
                throw new ArgumentNullException(nameof(tape));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            TapeIssue issues = TapeIssue.None;
            int firstCue = -1;

            if (tape.CueCount == 0)
            {
                return new TapeAuditResult(TapeIssue.NoTranscript, -1, 0f);
            }

            Span<char> needle = stackalloc char[MaxNeedleLength];
            int needleLength = BuildNeedle(forbiddenSolution, needle);

            float covered = 0f;

            for (int i = 0; i < tape.CueCount; i++)
            {
                TranscriptCue cue = tape.CueAt(i);
                covered += cue.Duration;

                string text = content.Get(cue.TextKey);

                if (string.IsNullOrEmpty(text) || ReferenceEquals(text, ContentTable.MissingPlaceholder))
                {
                    issues |= TapeIssue.MissingTranscriptCopy;
                    if (firstCue < 0)
                    {
                        firstCue = i;
                    }

                    continue;
                }

                if (needleLength > 1 && DocumentAudit.ContainsNormalized(text, needle.Slice(0, needleLength)))
                {
                    issues |= TapeIssue.AnswerSpokenOnTape;
                    if (firstCue < 0)
                    {
                        firstCue = i;
                    }
                }
            }

            float coverage = tape.DurationSeconds > 0f ? covered / tape.DurationSeconds : 0f;

            if (coverage < MinimumTranscriptCoverage)
            {
                issues |= TapeIssue.TranscriptCoverageLow;
            }

            return new TapeAuditResult(issues, firstCue, coverage);
        }

        private static int BuildNeedle(ReadOnlySpan<int> tokens, Span<char> destination)
        {
            if (tokens.Length < 2)
            {
                return 0;
            }

            int length = 0;
            Span<char> digits = stackalloc char[10];

            for (int i = 0; i < tokens.Length; i++)
            {
                int value = tokens[i];
                if (value < 0)
                {
                    return 0;
                }

                int count = 0;

                if (value == 0)
                {
                    digits[count++] = '0';
                }
                else
                {
                    while (value != 0)
                    {
                        digits[count++] = (char)('0' + (value % 10));
                        value /= 10;
                    }
                }

                if (length + count > destination.Length)
                {
                    return 0;
                }

                for (int d = count - 1; d >= 0; d--)
                {
                    destination[length++] = digits[d];
                }
            }

            return length;
        }
    }
}
