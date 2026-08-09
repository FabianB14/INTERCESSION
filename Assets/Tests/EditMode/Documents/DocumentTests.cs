using System;
using System.Collections.Generic;
using NUnit.Framework;
using Session.Core.Content;
using Session.Core.Documents;

namespace Session.Tests.Core.Documents
{
    internal static class TestDocuments
    {
        internal const string BodyText = "Admitted 14 March 1979. Personal effects surrendered at intake.";
        internal const string ClueText = "Ward code 4172. Please retain this slip.";
        internal const string LeakyText = "Filed under 4-1-7-2 in the annex cabinet.";
        internal const string FooterText = "I. The room is honest. II. The room is patient. III. No room may be left unfinished.";

        internal static readonly int BodyKey = ContentKey.Of("doc.body");
        internal static readonly int ClueKey = ContentKey.Of("doc.clue");
        internal static readonly int LeakyKey = ContentKey.Of("doc.leaky");
        internal static readonly int FooterKey = ContentKey.Of("doc.footer");
        internal static readonly int StruckKey = ContentKey.Of("doc.struck");
        internal static readonly int TitleKey = ContentKey.Of("doc.title");

        internal static ContentTable Content()
        {
            return new ContentTable(
                new List<int> { BodyKey, ClueKey, LeakyKey, FooterKey, StruckKey, TitleKey },
                new List<string> { BodyText, ClueText, LeakyText, FooterText, "REDACTED", "Patient File" });
        }

        internal static DocumentDefinition Multipage(bool clueOnLastPage = true, bool includeLeak = false)
        {
            var first = new DocumentPage(new[]
            {
                new DocumentBlock(BodyKey, DocumentBlockRole.Heading),
                new DocumentBlock(BodyKey, DocumentBlockRole.Body),
                new DocumentBlock(FooterKey, DocumentBlockRole.Footer)
            });

            var middle = new DocumentPage(new[]
            {
                new DocumentBlock(includeLeak ? LeakyKey : BodyKey, DocumentBlockRole.Body),
                new DocumentBlock(FooterKey, DocumentBlockRole.Footer)
            });

            var last = new DocumentPage(new[]
            {
                new DocumentBlock(
                    clueOnLastPage ? ClueKey : BodyKey,
                    clueOnLastPage ? DocumentBlockRole.ClueBearing : DocumentBlockRole.Body),
                new DocumentBlock(FooterKey, DocumentBlockRole.Footer)
            });

            return new DocumentDefinition(TitleKey, DocumentKind.PatientFile, new[] { first, middle, last });
        }
    }

    public sealed class DocumentReaderStateTests
    {
        [Test]
        public void StartsClosed()
        {
            var state = new DocumentReaderState();

            Assert.IsFalse(state.IsOpen);
            Assert.AreEqual(0, state.PageCount);
        }

        [Test]
        public void OpensOnPageOne()
        {
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), revealsClue: true);

            Assert.IsTrue(state.IsOpen);
            Assert.AreEqual(0, state.PageIndex);
            Assert.AreEqual(3, state.PageCount);
        }

        [Test]
        public void PageTurningRespectsBounds()
        {
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), true);

            Assert.IsFalse(state.PreviousPage(), "Already on the first page.");
            Assert.IsTrue(state.NextPage());
            Assert.IsTrue(state.NextPage());
            Assert.AreEqual(2, state.PageIndex);
            Assert.IsFalse(state.NextPage(), "Already on the last page.");
            Assert.IsTrue(state.PreviousPage());
            Assert.AreEqual(1, state.PageIndex);
        }

        [Test]
        public void GoToPageClampsInsteadOfThrowing()
        {
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), true);

            state.GoToPage(99);
            Assert.AreEqual(2, state.PageIndex);

            state.GoToPage(-5);
            Assert.AreEqual(0, state.PageIndex);
        }

        [Test]
        public void ReopeningResetsToPageOne()
        {
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), true);
            state.NextPage();
            state.Close();
            state.Open(TestDocuments.Multipage(), true);

            Assert.AreEqual(0, state.PageIndex, "Picking a document back up should not resume mid-file.");
        }

        [Test]
        public void RevealingLensCanReadTheClue()
        {
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), revealsClue: true);

            Assert.AreEqual(1, state.LegibleClueBlockCount());
        }

        [Test]
        public void ConcealingLensCannotReadTheClueOnAnyPage()
        {
            // The invariant. A clue on page three must be as concealed as one on page one — the
            // bug this guards against is a renderer that only redacts the page it happens to be
            // drawing, which no play test would ever surface.
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), revealsClue: false);

            Assert.AreEqual(0, state.LegibleClueBlockCount());

            for (int page = 0; page < state.PageCount; page++)
            {
                DocumentPage current = state.Document.PageAt(page);
                for (int block = 0; block < current.BlockCount; block++)
                {
                    if (current.BlockAt(block).CarriesClue)
                    {
                        Assert.IsFalse(
                            state.IsBlockLegible(page, block),
                            "Clue legible through a concealing lens at page {0}, block {1}", page, block);
                    }
                }
            }
        }

        [Test]
        public void ConcealingLensStillReadsOrdinaryCopy()
        {
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), revealsClue: false);

            Assert.IsTrue(state.IsBlockLegible(0, 0), "Withholding a clue must not blank the whole document.");
            Assert.IsTrue(state.IsBlockLegible(0, 1));
        }

        [Test]
        public void StruckCopyIsNeverLegibleEvenWithARevealingLens()
        {
            var page = new DocumentPage(new[]
            {
                new DocumentBlock(TestDocuments.StruckKey, DocumentBlockRole.Struck),
                new DocumentBlock(TestDocuments.BodyKey, DocumentBlockRole.Body)
            });

            var state = new DocumentReaderState();
            state.Open(new DocumentDefinition(TestDocuments.TitleKey, DocumentKind.StaffMemo, new[] { page }), true);

            Assert.IsFalse(state.IsBlockLegible(0, 0), "The Institute's own redaction is not lens-dependent.");
            Assert.IsTrue(state.IsBlockLegible(0, 1));
        }

        [Test]
        public void OutOfRangeQueriesReturnFalseRatherThanThrow()
        {
            var state = new DocumentReaderState();
            state.Open(TestDocuments.Multipage(), true);

            Assert.IsFalse(state.IsBlockLegible(99, 0));
            Assert.IsFalse(state.IsBlockLegible(0, 99));
            Assert.IsFalse(state.IsBlockLegible(-1, -1));
        }

        [Test]
        public void ClosedReaderIsInert()
        {
            var state = new DocumentReaderState();

            Assert.IsFalse(state.NextPage());
            Assert.IsFalse(state.PreviousPage());
            Assert.IsFalse(state.GoToPage(1));
            Assert.IsFalse(state.IsBlockLegible(0, 0));
            Assert.AreEqual(0, state.LegibleClueBlockCount());
        }

        [Test]
        public void ChangedFiresOnOpenTurnAndClose()
        {
            var state = new DocumentReaderState();
            int changes = 0;
            state.Changed += () => changes++;

            state.Open(TestDocuments.Multipage(), true);  // 1
            state.NextPage();                             // 2
            state.PreviousPage();                         // 3
            state.PreviousPage();                         // already first, no event
            state.Close();                                // 4
            state.Close();                                // already closed, no event

            Assert.AreEqual(4, changes);
        }

        [Test]
        public void EmptyDocumentIsRejectedAtConstruction()
        {
            Assert.Throws<ArgumentException>(
                () => new DocumentDefinition(1, DocumentKind.Notice, Array.Empty<DocumentPage>()));
        }
    }

    public sealed class DocumentAuditTests
    {
        private static readonly int[] Solution = { 4, 1, 7, 2 };

        [Test]
        public void CleanConcealingDocumentPasses()
        {
            DocumentAuditResult result = DocumentAudit.Audit(
                TestDocuments.Multipage(clueOnLastPage: false),
                TestDocuments.Content(),
                Solution,
                revealsClue: false);

            Assert.IsTrue(result.IsClean, result.ToString());
        }

        [Test]
        public void LeakedAnswerInConcealingDocumentIsCaught()
        {
            // "Filed under 4-1-7-2 in the annex cabinet." on a variant that is supposed to be
            // withholding 4172. This is the bug that silently kills the co-op premise.
            DocumentAuditResult result = DocumentAudit.Audit(
                TestDocuments.Multipage(clueOnLastPage: false, includeLeak: true),
                TestDocuments.Content(),
                Solution,
                revealsClue: false);

            Assert.IsFalse(result.IsClean);
            Assert.AreNotEqual(0, result.Issues & DocumentIssue.AnswerLeakedThroughConcealingLens);
            Assert.AreEqual(1, result.PageIndex, "Should point at the offending page.");
        }

        [Test]
        public void SameTextOnARevealingVariantIsFine()
        {
            // The revealing lens is supposed to contain the answer. Only concealing variants leak.
            DocumentAuditResult result = DocumentAudit.Audit(
                TestDocuments.Multipage(clueOnLastPage: true, includeLeak: true),
                TestDocuments.Content(),
                Solution,
                revealsClue: true);

            Assert.AreEqual(0, result.Issues & DocumentIssue.AnswerLeakedThroughConcealingLens);
        }

        [Test]
        public void RevealingVariantWithNoClueBlockIsCaught()
        {
            DocumentAuditResult result = DocumentAudit.Audit(
                TestDocuments.Multipage(clueOnLastPage: false),
                TestDocuments.Content(),
                Solution,
                revealsClue: true);

            Assert.AreNotEqual(0, result.Issues & DocumentIssue.RevealingVariantHasNoClueBlock);
        }

        [Test]
        public void MissingCopyIsCaught()
        {
            var page = new DocumentPage(new[]
            {
                new DocumentBlock(ContentKey.Of("doc.never.authored"), DocumentBlockRole.Body)
            });

            DocumentAuditResult result = DocumentAudit.Audit(
                new DocumentDefinition(TestDocuments.TitleKey, DocumentKind.StaffMemo, new[] { page }),
                TestDocuments.Content(),
                Solution,
                revealsClue: false);

            Assert.AreNotEqual(0, result.Issues & DocumentIssue.MissingCopy);
        }

        [Test]
        public void NormalisedSearchIgnoresSeparators()
        {
            Assert.IsTrue(DocumentAudit.ContainsNormalized("the code is 4172.", "4172"));
            Assert.IsTrue(DocumentAudit.ContainsNormalized("the code is 4-1-7-2.", "4172"));
            Assert.IsTrue(DocumentAudit.ContainsNormalized("the code is 4 1 7 2.", "4172"));
            Assert.IsTrue(DocumentAudit.ContainsNormalized("4/1/7/2", "4172"));
        }

        [Test]
        public void NormalisedSearchDoesNotFlagInnocentDigits()
        {
            // Digits-only normalisation would turn this into "4172" and cry wolf. Keeping letters
            // in the haystack is what makes the check usable rather than something people disable.
            Assert.IsFalse(DocumentAudit.ContainsNormalized("Room 4, bed 172", "4172"));
            Assert.IsFalse(DocumentAudit.ContainsNormalized("Ward 41 admitted 72 patients", "4172"));
        }

        [Test]
        public void SingleTokenSolutionsAreNotSearched()
        {
            // A one-digit answer would match nearly every page of every document. Flagging all of
            // them would train everyone to ignore this tool.
            DocumentAuditResult result = DocumentAudit.Audit(
                TestDocuments.Multipage(clueOnLastPage: false, includeLeak: true),
                TestDocuments.Content(),
                new[] { 4 },
                revealsClue: false);

            Assert.AreEqual(0, result.Issues & DocumentIssue.AnswerLeakedThroughConcealingLens);
        }

        [Test]
        public void ClueBlockOnConcealingVariantIsNotedButNotTreatedAsALeak()
        {
            DocumentAuditResult result = DocumentAudit.Audit(
                TestDocuments.Multipage(clueOnLastPage: true),
                TestDocuments.Content(),
                Solution,
                revealsClue: false);

            Assert.AreNotEqual(0, result.Issues & DocumentIssue.ConcealingVariantContainsClueBlock);
            Assert.AreEqual(
                0, result.Issues & DocumentIssue.AnswerLeakedThroughConcealingLens,
                "A hidden clue block is not readable, so it is not a leak.");
        }

        [Test]
        public void EmptySolutionSkipsTheLeakCheck()
        {
            DocumentAuditResult result = DocumentAudit.Audit(
                TestDocuments.Multipage(clueOnLastPage: false, includeLeak: true),
                TestDocuments.Content(),
                Array.Empty<int>(),
                revealsClue: false);

            Assert.AreEqual(0, result.Issues & DocumentIssue.AnswerLeakedThroughConcealingLens);
        }
    }
}
