using System;
using Session.Core.Interaction;
using Session.Core.Text;
using Session.Runtime.Tuning;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Session.UI.Puzzles
{
    /// <summary>
    /// A diegetic keypad: world-space buttons, a small readout, and a submit key.
    ///
    /// Holds a <see cref="PuzzleInputBuffer"/> and nothing else. It cannot tell whether the entered
    /// code is right — it has no access to the solution, because the solution exists only on the
    /// server. Pressing submit raises <see cref="Submitted"/>; a binder sends the tokens and the
    /// server answers. That is golden rule 5 reaching all the way to the button the player presses.
    ///
    /// Masking the readout is deliberate. Two players are reading halves of the same code to each
    /// other out loud, so showing the digits back plainly is right — but the option is here because
    /// a shoulder-surfing variant is a real design lever for later rooms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeypadView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _readout;

        [SerializeField] private UiPaletteSO _palette;

        [Tooltip("Digit buttons 0-9, in order. Wire only the ones this keypad physically has.")]
        [SerializeField] private Button[] _digitButtons = new Button[10];

        [SerializeField] private Button _backspaceButton;

        [SerializeField] private Button _submitButton;

        [Tooltip("How many tokens this lock accepts. Must match the RoomLayoutSO's solution length.")]
        [SerializeField, Range(1, 16)] private int _codeLength = 4;

        [Tooltip("Show entered digits as dots instead of numerals.")]
        [SerializeField] private bool _maskEntry;

        [Tooltip("Seconds the readout shows a rejection before clearing.")]
        [SerializeField, Min(0.2f)] private float _rejectHoldSeconds = 1.2f;

        private PuzzleInputBuffer _input;
        private TextWriteBuffer _buffer;
        private float _rejectRemaining;
        private bool _locked;

        /// <summary>Raised when the player submits. The payload is only valid during the call.</summary>
        public event Action<ReadOnlyMemory<int>> Submitted;

        private int[] _submitScratch;

        public bool IsLocked => _locked;

        private void Awake()
        {
            _input = new PuzzleInputBuffer(_codeLength);
            _buffer = new TextWriteBuffer(new char[_codeLength + 8]);
            _submitScratch = new int[_codeLength];

            _input.Changed += Redraw;

            for (int i = 0; i < _digitButtons.Length; i++)
            {
                if (_digitButtons[i] == null)
                {
                    continue;
                }

                // Capture by value — the loop variable would otherwise be shared by every listener
                // and every button would enter the same digit.
                int digit = i;
                _digitButtons[i].onClick.AddListener(() => OnDigit(digit));
            }

            if (_backspaceButton != null)
            {
                _backspaceButton.onClick.AddListener(OnBackspace);
            }

            if (_submitButton != null)
            {
                _submitButton.onClick.AddListener(OnSubmit);
            }

            Redraw();
        }

        private void OnDestroy()
        {
            if (_input != null)
            {
                _input.Changed -= Redraw;
            }
        }

        private void OnDigit(int digit)
        {
            if (_locked)
            {
                return;
            }

            ClearRejection();
            _input.Push(digit);
        }

        private void OnBackspace()
        {
            if (_locked)
            {
                return;
            }

            ClearRejection();
            _input.Backspace();
        }

        private void OnSubmit()
        {
            if (_locked || _input.Count != _codeLength)
            {
                return;
            }

            int count = _input.Commit(_submitScratch);
            Submitted?.Invoke(new ReadOnlyMemory<int>(_submitScratch, 0, count));
        }

        /// <summary>Called by the binder when the server rejects an attempt.</summary>
        public void ShowRejected()
        {
            _rejectRemaining = _rejectHoldSeconds;

            if (_readout != null && _palette != null)
            {
                _readout.color = _palette.OxideRed;
            }
        }

        /// <summary>Called when the server accepts. The lock is open; stop taking input.</summary>
        public void ShowAccepted()
        {
            _locked = true;

            if (_readout != null && _palette != null)
            {
                _readout.color = _palette.InstitutionalGreen;
            }
        }

        private void ClearRejection()
        {
            if (_rejectRemaining <= 0f)
            {
                return;
            }

            _rejectRemaining = 0f;
            ResetReadoutColour();
        }

        private void ResetReadoutColour()
        {
            if (_readout != null && _palette != null)
            {
                _readout.color = _palette.Cream;
            }
        }

        private void Update()
        {
            if (_rejectRemaining <= 0f)
            {
                return;
            }

            _rejectRemaining -= Time.deltaTime;

            if (_rejectRemaining > 0f)
            {
                return;
            }

            _rejectRemaining = 0f;
            ResetReadoutColour();
            Redraw();
        }

        private void Redraw()
        {
            if (_readout == null)
            {
                return;
            }

            _buffer.Clear();

            ReadOnlySpan<int> tokens = _input.Tokens;
            for (int i = 0; i < _codeLength; i++)
            {
                if (i >= tokens.Length)
                {
                    _buffer.Append('_');
                    continue;
                }

                _buffer.Append(_maskEntry ? '*' : (char)('0' + Mathf.Clamp(tokens[i], 0, 9)));
            }

            _readout.SetCharArray(_buffer.Buffer, 0, _buffer.Length);
        }
    }
}
