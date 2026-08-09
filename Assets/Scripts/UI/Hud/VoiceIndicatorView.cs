using Session.Runtime.Tuning;
using UnityEngine;
using UnityEngine.UI;

namespace Session.UI.Hud
{
    /// <summary>
    /// Shows that the microphone is open, and who is currently audible.
    ///
    /// Worth more than it looks. The whole game is players describing rooms to each other, so
    /// "am I actually transmitting?" is a question that gets asked constantly, and answering it
    /// badly means someone reads out a four-digit code to nobody while the Attendant walks in.
    ///
    /// Not accent-coloured. A microphone indicator is status, not something you can interact with.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceIndicatorView : MonoBehaviour
    {
        [SerializeField] private Image _microphoneIcon;

        [SerializeField] private UiPaletteSO _palette;

        [Tooltip("One per player slot, in slot order. Lit while that player is audible to you.")]
        [SerializeField] private Image[] _speakerLights = new Image[4];

        [Tooltip("Seconds a speaker light stays lit after their last voice frame.")]
        [SerializeField, Min(0.05f)] private float _speakerHoldSeconds = 0.25f;

        private readonly float[] _speakerTimers = new float[4];
        private bool _transmitting;

        private void Awake()
        {
            SetTransmitting(false);

            for (int i = 0; i < _speakerLights.Length; i++)
            {
                if (_speakerLights[i] != null)
                {
                    _speakerLights[i].enabled = false;
                }
            }
        }

        /// <summary>Called when push-to-talk opens or closes.</summary>
        public void SetTransmitting(bool transmitting)
        {
            if (_transmitting == transmitting && _microphoneIcon != null && _microphoneIcon.enabled == transmitting)
            {
                return;
            }

            _transmitting = transmitting;

            if (_microphoneIcon != null)
            {
                _microphoneIcon.enabled = transmitting;

                if (_palette != null)
                {
                    _microphoneIcon.color = _palette.Cream;
                }
            }
        }

        /// <summary>
        /// Called on every received voice frame. <paramref name="gain"/> drives brightness, so a
        /// player two rooms away reads as faint rather than as absent — which is exactly the
        /// information someone needs to decide whether to walk closer or shout.
        /// </summary>
        public void NoteSpeaker(int slot, float gain)
        {
            if (slot < 0 || slot >= _speakerLights.Length)
            {
                return;
            }

            _speakerTimers[slot] = _speakerHoldSeconds;

            Image light = _speakerLights[slot];
            if (light == null)
            {
                return;
            }

            light.enabled = true;

            if (_palette != null)
            {
                Color colour = _palette.Cream;
                colour.a = Mathf.Clamp(gain, 0.25f, 1f);
                light.color = colour;
            }
        }

        private void Update()
        {
            float delta = Time.deltaTime;

            for (int i = 0; i < _speakerTimers.Length; i++)
            {
                if (_speakerTimers[i] <= 0f)
                {
                    continue;
                }

                _speakerTimers[i] -= delta;

                if (_speakerTimers[i] > 0f)
                {
                    continue;
                }

                _speakerTimers[i] = 0f;

                if (_speakerLights[i] != null)
                {
                    _speakerLights[i].enabled = false;
                }
            }
        }
    }
}
