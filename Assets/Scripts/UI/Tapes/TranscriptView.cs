using Session.Core.Content;
using Session.Core.Tapes;
using Session.Core.Text;
using Session.Runtime.Tuning;
using TMPro;
using UnityEngine;

namespace Session.UI.Tapes
{
    /// <summary>
    /// Subtitles for whatever tape is playing nearby.
    ///
    /// Not optional. A game whose best story asset is a single voice actor talking quietly, played
    /// through a 3D AudioSource in a room where two people are also talking over proximity voice,
    /// is a game where a lot of players will miss most of the writing. The tape validator flags a
    /// tape whose transcript covers less than half its runtime for exactly this reason.
    ///
    /// Shows the speaker and the year alongside the line, because the 1979-1984 drift only reads as
    /// a drift if players can see which year they are listening to.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TranscriptView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;

        [SerializeField] private TMP_Text _lineLabel;

        [SerializeField] private TMP_Text _attributionLabel;

        [SerializeField] private ContentTableSO _contentSource;

        [SerializeField] private UiPaletteSO _palette;

        [Tooltip("Seconds the last line lingers after a tape stops, so it is not cut mid-read.")]
        [SerializeField, Min(0f)] private float _lingerSeconds = 1.5f;

        [SerializeField, Min(64)] private int _maxLineLength = 512;

        private ContentTable _content;
        private TextWriteBuffer _lineBuffer;
        private TextWriteBuffer _attributionBuffer;
        private float _linger;
        private bool _visible;

        private void Awake()
        {
            _content = _contentSource != null ? _contentSource.Build() : null;
            _lineBuffer = new TextWriteBuffer(new char[_maxLineLength]);
            _attributionBuffer = new TextWriteBuffer(new char[64]);

            SetVisible(false);
        }

        /// <summary>
        /// Show a transcript line. <paramref name="cueIndex"/> of -1 means the tape is between
        /// lines, which is common and should not blink the subtitle away.
        /// </summary>
        public void Show(TapeDefinition tape, int cueIndex)
        {
            if (tape == null || _content == null)
            {
                return;
            }

            if (cueIndex < 0 || cueIndex >= tape.CueCount)
            {
                // Between lines. Hold the last one briefly rather than flickering on every pause
                // for breath — Verity speaks slowly, and the gaps are long.
                _linger = _lingerSeconds;
                return;
            }

            _linger = _lingerSeconds;

            _lineBuffer.Clear();
            _lineBuffer.Append(_content.Get(tape.CueAt(cueIndex).TextKey));

            if (_lineLabel != null)
            {
                _lineLabel.SetCharArray(_lineBuffer.Buffer, 0, _lineBuffer.Length);
            }

            _attributionBuffer.Clear();
            _attributionBuffer.Append(_content.Get(tape.SpeakerKey));
            _attributionBuffer.Append(',');
            _attributionBuffer.Append(' ');
            _attributionBuffer.Append(tape.RecordedYear);

            if (_attributionLabel != null)
            {
                _attributionLabel.SetCharArray(_attributionBuffer.Buffer, 0, _attributionBuffer.Length);
            }

            SetVisible(true);
        }

        public void Clear()
        {
            _linger = 0f;
            SetVisible(false);
        }

        private void Update()
        {
            if (_linger <= 0f)
            {
                return;
            }

            _linger -= Time.deltaTime;

            if (_linger <= 0f)
            {
                _linger = 0f;
                SetVisible(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
            {
                return;
            }

            _visible = visible;

            if (_group != null)
            {
                _group.alpha = visible ? 1f : 0f;
            }

            if (visible && _lineLabel != null && _palette != null)
            {
                // Cream on the letterbox. Never the accent — a subtitle is not interactable.
                _lineLabel.color = _palette.Cream;
            }
        }
    }
}
