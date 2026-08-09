using System;

namespace Session.Core.Documents
{
    /// <summary>
    /// What the player currently has open, which page they are on, and which blocks they may read.
    ///
    /// The rule this exists to guarantee: <b>a lens that conceals a prop's clue must not be able to
    /// read the clue on any page of that prop's document.</b> Getting that wrong on page three of a
    /// four-page patient file quietly hands one player the whole room, and it would never show up
    /// in a play test because the player who can see it has no idea the other one cannot.
    ///
    /// So visibility is answered here, for any page, by one function — not by whatever the view
    /// happened to build when it laid out the current page.
    /// </summary>
    public sealed class DocumentReaderState
    {
        private DocumentDefinition? _document;
        private bool _revealsClue;
        private int _pageIndex;

        /// <summary>Raised on open, close, and every page turn.</summary>
        public event Action? Changed;

        public bool IsOpen => _document != null;

        public DocumentDefinition? Document => _document;

        public int PageIndex => _pageIndex;

        public int PageCount => _document?.PageCount ?? 0;

        /// <summary>Whether this player's lens exposes the clue on the prop this document belongs to.</summary>
        public bool RevealsClue => _revealsClue;

        public bool HasNextPage => _document != null && _pageIndex < _document.PageCount - 1;

        public bool HasPreviousPage => _document != null && _pageIndex > 0;

        /// <summary>
        /// Open a document. <paramref name="revealsClue"/> comes from the reader's own lens — the
        /// same bool <c>PropView</c> was handed — and is fixed for as long as the document is open.
        /// </summary>
        public void Open(DocumentDefinition document, bool revealsClue)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _revealsClue = revealsClue;
            _pageIndex = 0;

            Changed?.Invoke();
        }

        public void Close()
        {
            if (_document == null)
            {
                return;
            }

            _document = null;
            _revealsClue = false;
            _pageIndex = 0;

            Changed?.Invoke();
        }

        public bool NextPage()
        {
            if (!HasNextPage)
            {
                return false;
            }

            _pageIndex++;
            Changed?.Invoke();
            return true;
        }

        public bool PreviousPage()
        {
            if (!HasPreviousPage)
            {
                return false;
            }

            _pageIndex--;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Jump to a page. Out-of-range indices are clamped rather than throwing.</summary>
        public bool GoToPage(int pageIndex)
        {
            if (_document == null)
            {
                return false;
            }

            int clamped = pageIndex < 0 ? 0
                : pageIndex >= _document.PageCount ? _document.PageCount - 1
                : pageIndex;

            if (clamped == _pageIndex)
            {
                return false;
            }

            _pageIndex = clamped;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Whether a block on a given page is legible to this reader. The single source of truth
        /// for the conceal rule — no page-specific path can bypass it.
        /// </summary>
        public bool IsBlockLegible(int pageIndex, int blockIndex)
        {
            if (_document == null || pageIndex < 0 || pageIndex >= _document.PageCount)
            {
                return false;
            }

            DocumentPage page = _document.PageAt(pageIndex);
            if (blockIndex < 0 || blockIndex >= page.BlockCount)
            {
                return false;
            }

            DocumentBlock block = page.BlockAt(blockIndex);

            // Struck copy is visible as a redaction but never readable, whatever the lens says.
            // That is the Institute's own censorship, not the perception split.
            if (block.Role == DocumentBlockRole.Struck)
            {
                return false;
            }

            return !block.CarriesClue || _revealsClue;
        }

        /// <summary>Convenience for the current page.</summary>
        public bool IsBlockLegible(int blockIndex) => IsBlockLegible(_pageIndex, blockIndex);

        /// <summary>
        /// Total clue-bearing blocks this reader can actually read across the whole document.
        /// Zero through a concealing lens — asserted in tests, because it is the invariant.
        /// </summary>
        public int LegibleClueBlockCount()
        {
            if (_document == null)
            {
                return 0;
            }

            int count = 0;
            for (int page = 0; page < _document.PageCount; page++)
            {
                DocumentPage current = _document.PageAt(page);
                for (int block = 0; block < current.BlockCount; block++)
                {
                    if (current.BlockAt(block).CarriesClue && IsBlockLegible(page, block))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
