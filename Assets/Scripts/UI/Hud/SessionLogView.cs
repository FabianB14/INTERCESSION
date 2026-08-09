using Session.Core.Content;
using Session.Core.Text;
using Session.Runtime.Tuning;
using TMPro;
using UnityEngine;

namespace Session.UI.Hud
{
    /// <summary>
    /// The Institute's voice on screen: one short line at a time, fading out.
    ///
    /// Tone rules from LORE.md are load-bearing here more than anywhere else in the UI. This
    /// surface never warns and never threatens. "Room 9 remains in session." is correct;
    /// "THE ATTENDANT IS COMING" is not, however accurate it might be. The dread is supposed to
    /// come from the gap between how politely the building talks and what it is actually doing.
    ///
    /// Deliberately not a scrolling log. Players are reading props and talking to each other; a
    /// wall of text competes with both, and this game's information channel is meant to be voice.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionLogView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _group;

        [SerializeField] private TMP_Text _label;

        [SerializeField] private ContentTableSO _contentSource;

        [SerializeField] private UiPaletteSO _palette;

        [Tooltip("Seconds a line stays at full opacity before fading.")]
        [SerializeField, Min(0.5f)] private float _holdSeconds = 4f;

        [SerializeField, Min(0.1f)] private float _fadeSeconds = 1.5f;

        [SerializeField, Min(32)] private int _maxLineLength = 160;

        private ContentTable _content;
        private TextWriteBuffer _buffer;
        private float _remaining;

        private void Awake()
        {
            _buffer = new TextWriteBuffer(new char[_maxLineLength]);
            _content = _contentSource != null ? _contentSource.Build() : null;

            if (_group != null)
            {
                _group.alpha = 0f;
            }

            if (_label != null && _palette != null)
            {
                // Never the accent. This is not something the player can interact with.
                _label.color = _palette.Cream;
            }
        }

        /// <summary>Show a line by content key. Replaces whatever is on screen.</summary>
        public void Post(int contentKey)
        {
            if (_label == null || _content == null)
            {
                return;
            }

            _buffer.Clear();
            _buffer.Append(_content.Get(contentKey));

            _label.SetCharArray(_buffer.Buffer, 0, _buffer.Length);
            _remaining = _holdSeconds + _fadeSeconds;

            if (_group != null)
            {
                _group.alpha = 1f;
            }
        }

        /// <summary>Show a line with a room number appended, e.g. "Room 9 remains in session."</summary>
        public void PostWithRoom(int contentKey, int roomNumber)
        {
            if (_label == null || _content == null)
            {
                return;
            }

            _buffer.Clear();
            _buffer.Append(_content.Get(contentKey));
            _buffer.Append(' ');
            _buffer.Append(roomNumber);

            _label.SetCharArray(_buffer.Buffer, 0, _buffer.Length);
            _remaining = _holdSeconds + _fadeSeconds;

            if (_group != null)
            {
                _group.alpha = 1f;
            }
        }

        private void Update()
        {
            if (_remaining <= 0f || _group == null)
            {
                return;
            }

            _remaining -= Time.deltaTime;

            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _group.alpha = 0f;
                return;
            }

            if (_remaining < _fadeSeconds)
            {
                _group.alpha = _remaining / _fadeSeconds;
            }
        }
    }
}
