using System;
using Session.Core.Content;
using Session.Core.Documents;
using Session.Core.Text;
using Session.Runtime.Tuning;
using Session.Runtime.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Session.UI.Documents
{
    /// <summary>
    /// The paper reader: hold a document up, turn pages, put it down.
    ///
    /// Two things this does that are easy to get wrong:
    ///
    /// 1. Legibility is asked per block, for the page being drawn, from
    ///    <see cref="DocumentReaderState"/>. There is no path in this file that renders a block's
    ///    text without going through it. A concealed clue is concealed on page four as reliably as
    ///    on page one.
    ///
    /// 2. Reading does not pause anything. The Attendant keeps walking, the room's time allowance
    ///    keeps running, and your partner keeps talking. Standing still with a form held up to your
    ///    face is a commitment, and it should feel like one — see <see cref="ReadingStarted"/>,
    ///    which the player rig uses to slow movement rather than to freeze the world.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DocumentReaderView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;

        [SerializeField] private TMP_Text _titleLabel;

        [SerializeField] private TMP_Text _bodyLabel;

        [SerializeField] private TMP_Text _pageLabel;

        [SerializeField] private Image _paperImage;

        [SerializeField] private Button _nextButton;

        [SerializeField] private Button _previousButton;

        [SerializeField] private Button _closeButton;

        [SerializeField] private ContentTableSO _contentSource;

        [SerializeField] private UiPaletteSO _palette;

        [Header("Redaction")]
        [Tooltip("Drawn in place of struck copy. The redaction is the content.")]
        [SerializeField] private string _struckGlyph = "████████";

        [Header("Buffers")]
        [Tooltip("Characters reserved for a rendered page. Long memos need headroom.")]
        [SerializeField, Min(256)] private int _maxPageLength = 4096;

        private readonly DocumentReaderState _state = new DocumentReaderState();
        private ContentTable _content;
        private char[] _pageBuffer;
        private int _pageLength;
        private char[] _pageNumberBuffer;

        /// <summary>Raised when a document is opened. The player rig slows movement while true.</summary>
        public event Action ReadingStarted;

        public event Action ReadingEnded;

        public bool IsOpen => _state.IsOpen;

        private void Awake()
        {
            _content = _contentSource != null ? _contentSource.Build() : null;
            _pageBuffer = new char[_maxPageLength];
            _pageNumberBuffer = new char[16];

            _state.Changed += Redraw;

            if (_nextButton != null)
            {
                _nextButton.onClick.AddListener(() => _state.NextPage());
            }

            if (_previousButton != null)
            {
                _previousButton.onClick.AddListener(() => _state.PreviousPage());
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }

            SetVisible(false);
        }

        private void OnDestroy()
        {
            _state.Changed -= Redraw;
        }

        /// <summary>Open the document on a paper prop, using that prop's lens state.</summary>
        public void Open(PaperPropView paper)
        {
            if (paper == null)
            {
                return;
            }

            DocumentDefinition document = paper.Document;
            if (document == null)
            {
                Debug.LogWarning("[Session] Paper prop '" + paper.name + "' has no document for the current lens.");
                return;
            }

            if (_paperImage != null && paper.Source != null && paper.Source.PaperStock != null)
            {
                _paperImage.sprite = paper.Source.PaperStock;
            }

            // RevealsClue comes from the prop, which got it from the lens. The reader never decides
            // this for itself.
            _state.Open(document, paper.RevealsClue);

            SetVisible(true);
            ReadingStarted?.Invoke();
        }

        public void Close()
        {
            if (!_state.IsOpen)
            {
                return;
            }

            _state.Close();
            SetVisible(false);
            ReadingEnded?.Invoke();
        }

        /// <summary>Page turning from the Input System, so a controller works as well as the buttons.</summary>
        public void TurnPage(int direction)
        {
            if (direction > 0)
            {
                _state.NextPage();
            }
            else if (direction < 0)
            {
                _state.PreviousPage();
            }
        }

        private void Redraw()
        {
            if (!_state.IsOpen || _content == null)
            {
                return;
            }

            DocumentDefinition document = _state.Document;

            if (_titleLabel != null)
            {
                _titleLabel.text = _content.Get(document.TitleKey);
            }

            RenderPage(document);
            RenderPageNumber();

            if (_nextButton != null)
            {
                _nextButton.interactable = _state.HasNextPage;
            }

            if (_previousButton != null)
            {
                _previousButton.interactable = _state.HasPreviousPage;
            }
        }

        private void RenderPage(DocumentDefinition document)
        {
            if (_bodyLabel == null)
            {
                return;
            }

            _pageLength = 0;

            int pageIndex = _state.PageIndex;
            DocumentPage page = document.PageAt(pageIndex);

            for (int block = 0; block < page.BlockCount; block++)
            {
                DocumentBlock entry = page.BlockAt(block);

                // Struck copy is visible as a redaction: the player sees that something was removed,
                // which is the point. Illegible clue blocks are absent entirely, because on this
                // player's variant that line was never written.
                if (entry.Role == DocumentBlockRole.Struck)
                {
                    AppendLine(_struckGlyph);
                    continue;
                }

                if (!_state.IsBlockLegible(pageIndex, block))
                {
                    continue;
                }

                AppendLine(_content.Get(entry.TextKey));
            }

            _bodyLabel.SetCharArray(_pageBuffer, 0, _pageLength);
        }

        private void AppendLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Blank line between blocks, once there is something to separate.
            if (_pageLength > 0)
            {
                if (_pageLength + 2 > _pageBuffer.Length)
                {
                    return;
                }

                _pageBuffer[_pageLength++] = '\n';
                _pageBuffer[_pageLength++] = '\n';
            }

            if (_pageLength + text.Length > _pageBuffer.Length)
            {
                Debug.LogWarning(
                    "[Session] A document page exceeded the reader's " + _pageBuffer.Length +
                    "-character buffer and was truncated. Raise Max Page Length or split the page.");
                return;
            }

            for (int i = 0; i < text.Length; i++)
            {
                _pageBuffer[_pageLength++] = text[i];
            }
        }

        private void RenderPageNumber()
        {
            if (_pageLabel == null)
            {
                return;
            }

            var buffer = new TextWriteBuffer(_pageNumberBuffer);
            buffer.Append(_state.PageIndex + 1);
            buffer.Append(' ');
            buffer.Append('/');
            buffer.Append(' ');
            buffer.Append(_state.PageCount);

            _pageLabel.SetCharArray(buffer.Buffer, 0, buffer.Length);
        }

        private void SetVisible(bool visible)
        {
            if (_group != null)
            {
                _group.alpha = visible ? 1f : 0f;
                _group.blocksRaycasts = visible;
                _group.interactable = visible;
            }

            if (_bodyLabel != null && _palette != null)
            {
                // Ink on paper. Never the accent — the document is not the interactable, the prop
                // you picked it up from was.
                _bodyLabel.color = _palette.OxideRed;
            }
        }
    }
}
