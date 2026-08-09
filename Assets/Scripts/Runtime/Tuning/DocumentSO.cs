using System;
using System.Collections.Generic;
using Session.Core.Content;
using Session.Core.Documents;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// One readable paper prop: a patient file, a staff memo, a form.
    ///
    /// LORE.md ranks these as the highest story-per-pound content in the project, behind only the
    /// intake tapes. Forty-one names, the 1979-1984 drift from clinical to frightened, and the
    /// whole collapse told without a cutscene — all of it is text on paper.
    ///
    /// Tone, non-negotiable: the Institute is never evil in its own voice. Forms are polite,
    /// signage is helpful, Verity is kind. A memo that sounds sinister is wrong even when it is
    /// describing something sinister; the dread comes from the gap.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Document", fileName = "SO_Document")]
    public sealed class DocumentSO : ScriptableObject
    {
        [Serializable]
        public sealed class Block
        {
            [Tooltip("Content key, e.g. doc.file.hallidae.p1.body")]
            public string TextKey = string.Empty;

            [Tooltip("ClueBearing marks the line carrying a puzzle input. Get this wrong and the room breaks.")]
            public DocumentBlockRole Role = DocumentBlockRole.Body;
        }

        [Serializable]
        public sealed class Page
        {
            public List<Block> Blocks = new List<Block>();
        }

        [Header("Identity")]
        [Tooltip("Content key for the document's title, e.g. doc.file.hallidae.title")]
        [SerializeField] private string _titleKey = string.Empty;

        [SerializeField] private DocumentKind _kind = DocumentKind.PatientFile;

        [Header("Contents")]
        [SerializeField] private List<Page> _pages = new List<Page>();

        [Header("Presentation")]
        [Tooltip("Paper texture behind the copy. Typed forms and handwritten dictation want different stock.")]
        [SerializeField] private Sprite _paperStock;

        [Tooltip("Appended to every page. Verity's Three Principles appear on every form footer.")]
        [SerializeField] private string _footerKey = "doc.footer.principles";

        public DocumentKind Kind => _kind;

        public Sprite PaperStock => _paperStock;

        public int PageCount => _pages.Count;

        /// <summary>Build the runtime document. Allocates; call at load, not per open.</summary>
        public DocumentDefinition Build()
        {
            if (_pages.Count == 0)
            {
                throw new InvalidOperationException(
                    "Document '" + name + "' has no pages. A document needs at least one.");
            }

            int footer = ContentKey.Of(_footerKey);
            var pages = new DocumentPage[_pages.Count];

            for (int i = 0; i < _pages.Count; i++)
            {
                List<Block> authored = _pages[i].Blocks;
                bool wantsFooter = footer != ContentKey.None;

                var blocks = new DocumentBlock[authored.Count + (wantsFooter ? 1 : 0)];

                for (int b = 0; b < authored.Count; b++)
                {
                    blocks[b] = new DocumentBlock(ContentKey.Of(authored[b].TextKey), authored[b].Role);
                }

                if (wantsFooter)
                {
                    blocks[authored.Count] = new DocumentBlock(footer, DocumentBlockRole.Footer);
                }

                pages[i] = new DocumentPage(blocks);
            }

            return new DocumentDefinition(ContentKey.Of(_titleKey), _kind, pages);
        }
    }
}
