using System;
using Session.Core.Content;

namespace Session.Core.Documents
{
    [Flags]
    public enum DocumentIssue
    {
        None = 0,

        /// <summary>
        /// The puzzle's answer is written out in copy this reader can legibly read, on a variant
        /// that is supposed to be withholding it. The single worst content bug in the project.
        /// </summary>
        AnswerLeakedThroughConcealingLens = 1 << 0,

        /// <summary>
        /// A revealing variant with nothing marked ClueBearing. The player who is supposed to hold
        /// this clue cannot read it anywhere, so the room is unsolvable.
        /// </summary>
        RevealingVariantHasNoClueBlock = 1 << 1,

        /// <summary>A block's content key resolves to nothing. Ships as "[no copy]" on a prop.</summary>
        MissingCopy = 1 << 2,

        /// <summary>A page with no blocks. Renders blank.</summary>
        EmptyPage = 1 << 3,

        /// <summary>
        /// A clue-bearing block on a concealing variant. Not fatal — the reader hides it — but it
        /// means the two variants are the same document with a hole in it rather than two honest
        /// different documents, which is a weaker version of the mechanic.
        /// </summary>
        ConcealingVariantContainsClueBlock = 1 << 4
    }

    public readonly struct DocumentAuditResult
    {
        public readonly DocumentIssue Issues;

        /// <summary>Where the first issue was found, or -1.</summary>
        public readonly int PageIndex;

        public readonly int BlockIndex;

        public DocumentAuditResult(DocumentIssue issues, int pageIndex, int blockIndex)
        {
            Issues = issues;
            PageIndex = pageIndex;
            BlockIndex = blockIndex;
        }

        public bool IsClean => Issues == DocumentIssue.None;

        public override string ToString()
        {
            return IsClean
                ? "Document clean."
                : "Document issues: " + Issues + " (page " + PageIndex + ", block " + BlockIndex + ")";
        }
    }

    /// <summary>
    /// Checks authored documents for the mistakes that break the perception split.
    ///
    /// The one that matters: a document shown through a <i>concealing</i> lens must not contain the
    /// puzzle's answer anywhere a player can read it. It is very easy to author this wrong — you
    /// write the withheld version of a patient file, mark the obvious line as ClueBearing, and
    /// forget that the same four digits appear two paragraphs down as a ward number. The player
    /// who is supposed to need their partner just... doesn't, and nobody finds out, because the
    /// player who can see it has no idea anyone else can't.
    ///
    /// <b>Known limit:</b> this is a literal-match check. It catches the answer written out —
    /// "4172", "4-1-7-2", "4 1 7 2" — and it will not catch it paraphrased ("the year the annex
    /// opened"). That is a real gap and a human still has to read the copy. It catches the common
    /// case mechanically, which is worth having.
    /// </summary>
    public static class DocumentAudit
    {
        private const int MaxNeedleLength = 128;

        public static DocumentAuditResult Audit(
            DocumentDefinition document,
            ContentTable content,
            ReadOnlySpan<int> solutionTokens,
            bool revealsClue)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            DocumentIssue issues = DocumentIssue.None;
            int firstPage = -1;
            int firstBlock = -1;

            void Note(DocumentIssue issue, int page, int block)
            {
                issues |= issue;
                if (firstPage >= 0)
                {
                    return;
                }

                firstPage = page;
                firstBlock = block;
            }

            Span<char> needle = stackalloc char[MaxNeedleLength];
            int needleLength = BuildNeedle(solutionTokens, needle);
            bool canCheckLeak = !revealsClue && needleLength > 1;

            bool sawClueBlock = false;

            for (int page = 0; page < document.PageCount; page++)
            {
                DocumentPage current = document.PageAt(page);

                if (current.BlockCount == 0)
                {
                    Note(DocumentIssue.EmptyPage, page, -1);
                    continue;
                }

                for (int block = 0; block < current.BlockCount; block++)
                {
                    DocumentBlock entry = current.BlockAt(block);

                    if (entry.CarriesClue)
                    {
                        sawClueBlock = true;

                        if (!revealsClue)
                        {
                            Note(DocumentIssue.ConcealingVariantContainsClueBlock, page, block);
                        }
                    }

                    string text = content.Get(entry.TextKey);

                    if (entry.Role != DocumentBlockRole.Struck &&
                        (string.IsNullOrEmpty(text) || ReferenceEquals(text, ContentTable.MissingPlaceholder)))
                    {
                        Note(DocumentIssue.MissingCopy, page, block);
                        continue;
                    }

                    // Only copy this reader can legibly read can leak. A clue-bearing block on a
                    // concealing variant is hidden by the reader, so its text is not a leak — it is
                    // the separate, milder issue noted above.
                    if (!canCheckLeak || entry.CarriesClue || entry.Role == DocumentBlockRole.Struck)
                    {
                        continue;
                    }

                    if (ContainsNormalized(text, needle.Slice(0, needleLength)))
                    {
                        Note(DocumentIssue.AnswerLeakedThroughConcealingLens, page, block);
                    }
                }
            }

            if (revealsClue && !sawClueBlock)
            {
                Note(DocumentIssue.RevealingVariantHasNoClueBlock, -1, -1);
            }

            return new DocumentAuditResult(issues, firstPage, firstBlock);
        }

        /// <summary>
        /// Flatten solution tokens into the digit run they would be written as. Returns 0 when
        /// there is nothing worth searching for — a single token is too short to match on without
        /// flagging every page that happens to contain that digit.
        /// </summary>
        private static int BuildNeedle(ReadOnlySpan<int> tokens, Span<char> destination)
        {
            if (tokens.Length < 2)
            {
                return 0;
            }

            int length = 0;

            // Hoisted: stackalloc inside a loop is not released until the method returns, so
            // allocating per token would grow the frame with every digit.
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

        /// <summary>
        /// Substring search that ignores case and every non-alphanumeric character in the haystack,
        /// so "4-1-7-2", "4 1 7 2" and "4172" all match a needle of "4172".
        ///
        /// Skipping punctuation rather than stripping to digits alone is deliberate: digits-only
        /// would turn "Room 4, bed 172" into "4172" and flag a false positive on innocent copy.
        /// </summary>
        public static bool ContainsNormalized(string haystack, ReadOnlySpan<char> needle)
        {
            if (string.IsNullOrEmpty(haystack) || needle.Length == 0)
            {
                return false;
            }

            for (int start = 0; start < haystack.Length; start++)
            {
                if (!IsAlphanumeric(haystack[start]))
                {
                    continue;
                }

                int h = start;
                int n = 0;

                while (h < haystack.Length && n < needle.Length)
                {
                    char c = haystack[h];

                    if (!IsAlphanumeric(c))
                    {
                        h++;
                        continue;
                    }

                    if (char.ToLowerInvariant(c) != char.ToLowerInvariant(needle[n]))
                    {
                        break;
                    }

                    h++;
                    n++;
                }

                if (n == needle.Length)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAlphanumeric(char value)
            => (value >= '0' && value <= '9') || (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
    }
}
