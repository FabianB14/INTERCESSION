using Session.Core.Voice;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// Proximity voice falloff.
    ///
    /// Treat these as puzzle-design numbers, not audio-mix numbers. The whole game is players
    /// describing what they see to each other, so if the full-volume radius is too tight or the
    /// through-wall multiplier too low, rooms silently become unsolvable. That failure is nearly
    /// invisible in a play test, because players just move closer and talk louder.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Voice Rules", fileName = "SO_VoiceRules")]
    public sealed class VoiceRulesSO : ScriptableObject, IVoiceRules
    {
        [Header("Same room")]
        [Tooltip("Within this distance a speaker is at full volume. Conversational range.")]
        [SerializeField, Min(0.5f)] private float _fullVolumeRadiusMeters = 3f;

        [Tooltip("Beyond this, a speaker in the same room is inaudible.")]
        [SerializeField, Min(1f)] private float _falloffEndMeters = 18f;

        [Header("Through walls")]
        [Tooltip("Gain multiplier when speaker and listener are in different rooms. Muffled, not soundproof.")]
        [SerializeField, Range(0f, 1f)] private float _throughWallMultiplier = 0.35f;

        [Tooltip("Beyond this, a speaker in a different room is cut entirely.")]
        [SerializeField, Min(1f)] private float _throughWallCutoffMeters = 8f;

        [Header("Mixing")]
        [Tooltip("Gains below this are treated as silence so the channel can be skipped.")]
        [SerializeField, Range(0f, 0.2f)] private float _silenceThreshold = 0.01f;

        public float FullVolumeRadiusMeters => _fullVolumeRadiusMeters;

        public float FalloffEndMeters => _falloffEndMeters;

        public float ThroughWallMultiplier => _throughWallMultiplier;

        public float ThroughWallCutoffMeters => _throughWallCutoffMeters;

        public float SilenceThreshold => _silenceThreshold;

        private void OnValidate()
        {
            if (_falloffEndMeters <= _fullVolumeRadiusMeters)
            {
                _falloffEndMeters = _fullVolumeRadiusMeters + 1f;
            }

            if (_throughWallCutoffMeters <= _fullVolumeRadiusMeters)
            {
                _throughWallCutoffMeters = _fullVolumeRadiusMeters + 1f;
            }
        }
    }
}
