using Session.Core.Content;
using Session.Core.Interaction;
using Session.Core.Text;
using Session.Runtime.Tuning;
using TMPro;
using UnityEngine;

namespace Session.UI.Hud
{
    /// <summary>
    /// Draws the "what am I looking at" prompt.
    ///
    /// The colour decision is not made here. <see cref="PromptResolver"/> returns a bool and this
    /// obeys it, which is the whole point — there is no code path in this file that can paint the
    /// accent onto something inert, because it has no way to know what "interactable" means.
    ///
    /// Allocation-free per golden rule 6: text is built into a reused char buffer and pushed with
    /// SetCharArray, and it only pushes when the text actually changed. Looking at the same object
    /// for ten seconds costs nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;

        [SerializeField] private TMP_Text _label;

        [SerializeField] private UiPaletteSO _palette;

        [SerializeField] private ContentTableSO _contentSource;

        [Tooltip("Content keys for verb copy. Institute tone: plain, unhurried, never a command.")]
        [SerializeField] private string _examineKey = "ui.verb.examine";

        [SerializeField] private string _useKey = "ui.verb.use";

        [SerializeField] private string _readKey = "ui.verb.read";

        [SerializeField] private string _openKey = "ui.verb.open";

        [SerializeField, Min(16)] private int _maxLabelLength = 96;

        private ContentTable _content;
        private TextWriteBuffer _buffer;
        private char[] _lastPushed;
        private int _lastPushedLength;
        private int _examine;
        private int _use;
        private int _read;
        private int _open;
        private bool _visible;

        private void Awake()
        {
            // Everything resolved once. Golden rule 6 forbids doing any of this per frame.
            _buffer = new TextWriteBuffer(new char[_maxLabelLength]);
            _lastPushed = new char[_maxLabelLength];

            _content = _contentSource != null ? _contentSource.Build() : null;

            _examine = ContentKey.Of(_examineKey);
            _use = ContentKey.Of(_useKey);
            _read = ContentKey.Of(_readKey);
            _open = ContentKey.Of(_openKey);

            SetVisible(false);
        }

        /// <summary>
        /// Push the current focus. Call from the interaction raycaster every frame; this is cheap
        /// and self-debouncing.
        /// </summary>
        public void Show(in InteractionCandidate candidate)
        {
            InteractionPrompt prompt = PromptResolver.Resolve(in candidate);

            if (!prompt.Visible)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            if (_label != null && _palette != null)
            {
                _label.color = prompt.UseAccentColour ? _palette.Accent : _palette.Dimmed;
            }

            _buffer.Clear();

            string verb = VerbCopy(prompt.Verb);
            if (!string.IsNullOrEmpty(verb))
            {
                _buffer.Append(verb);
                _buffer.Append(' ');
            }

            _buffer.Append(_content != null ? _content.Get(prompt.SubjectNameKey) : string.Empty);

            Push();
        }

        public void Hide() => SetVisible(false);

        private string VerbCopy(InteractionVerb verb)
        {
            if (_content == null)
            {
                return string.Empty;
            }

            switch (verb)
            {
                case InteractionVerb.Examine:
                    return _content.Get(_examine);
                case InteractionVerb.Use:
                    return _content.Get(_use);
                case InteractionVerb.Read:
                    return _content.Get(_read);
                case InteractionVerb.Open:
                    return _content.Get(_open);
                default:
                    return string.Empty;
            }
        }

        private void Push()
        {
            if (_label == null || !_buffer.DiffersFrom(_lastPushed, _lastPushedLength))
            {
                return;
            }

            _label.SetCharArray(_buffer.Buffer, 0, _buffer.Length);

            System.Array.Copy(_buffer.Buffer, _lastPushed, _buffer.Length);
            _lastPushedLength = _buffer.Length;
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
        }
    }
}
