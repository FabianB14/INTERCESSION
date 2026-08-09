using System;

namespace Session.Core.Documents
{
    public enum DocumentKind
    {
        /// <summary>One of the forty-one intake files. The names players connect across rooms.</summary>
        PatientFile = 0,

        /// <summary>1979-1984. The drift from clinical to frightened, told without a cutscene.</summary>
        StaffMemo = 1,

        /// <summary>A form. The scariest asset in the game is a correctly filled-out one.</summary>
        IntakeForm = 2,

        /// <summary>Signage, labels, stencils. Helpful in tone, always.</summary>
        Notice = 3,

        /// <summary>Verity's own hand. Rare, and the only place the Institute sounds uncertain.</summary>
        Dictation = 4
    }

    public enum DocumentBlockRole
    {
        /// <summary>Ordinary copy. Always legible.</summary>
        Body = 0,

        Heading = 1,

        /// <summary>
        /// Carries a puzzle input. Legible only through a lens that reveals this prop's clue.
        /// Marking a block correctly is load-bearing — see <see cref="DocumentAudit"/>.
        /// </summary>
        ClueBearing = 2,

        /// <summary>
        /// Verity's Three Principles, the form number, the file footer. Appears on every form.
        /// </summary>
        Footer = 3,

        /// <summary>
        /// Struck through by the Institute itself. Always visible, always unreadable — the
        /// redaction is the content.
        /// </summary>
        Struck = 4
    }

    public readonly struct DocumentBlock
    {
        /// <summary>Content key resolved through the ContentTable at render time.</summary>
        public readonly int TextKey;

        public readonly DocumentBlockRole Role;

        public DocumentBlock(int textKey, DocumentBlockRole role)
        {
            TextKey = textKey;
            Role = role;
        }

        public bool CarriesClue => Role == DocumentBlockRole.ClueBearing;
    }

    public sealed class DocumentPage
    {
        private readonly DocumentBlock[] _blocks;

        public DocumentPage(DocumentBlock[] blocks)
        {
            _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        }

        public int BlockCount => _blocks.Length;

        public DocumentBlock BlockAt(int index) => _blocks[index];

        public bool HasClueBlock
        {
            get
            {
                for (int i = 0; i < _blocks.Length; i++)
                {
                    if (_blocks[i].CarriesClue)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// A readable paper prop. Immutable after load.
    ///
    /// A document is a <i>prop variant</i>, not a thing in its own right — the same physical sheet
    /// on the same desk reads as an admission form to one player and a discharge checklist to
    /// another, because that is what the perception system does. The reveal/conceal split therefore
    /// lands here as blocks that some lenses can read and some cannot.
    ///
    /// Never replicated. Documents are derived locally from the lens, like every other part of
    /// perception, so no document text ever crosses the network.
    /// </summary>
    public sealed class DocumentDefinition
    {
        private readonly DocumentPage[] _pages;

        public readonly int TitleKey;

        public readonly DocumentKind Kind;

        public DocumentDefinition(int titleKey, DocumentKind kind, DocumentPage[] pages)
        {
            if (pages == null || pages.Length == 0)
            {
                throw new ArgumentException("A document needs at least one page.", nameof(pages));
            }

            TitleKey = titleKey;
            Kind = kind;
            _pages = pages;
        }

        public int PageCount => _pages.Length;

        public DocumentPage PageAt(int index) => _pages[index];

        /// <summary>Whether any page carries a puzzle input.</summary>
        public bool HasClueBlocks
        {
            get
            {
                for (int i = 0; i < _pages.Length; i++)
                {
                    if (_pages[i].HasClueBlock)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
