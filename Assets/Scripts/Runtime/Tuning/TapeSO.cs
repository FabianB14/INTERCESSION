using System;
using System.Collections.Generic;
using Session.Core.Content;
using Session.Core.Identity;
using Session.Core.Tapes;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// One intake tape: the clip, the label, and the transcript.
    ///
    /// Verity's voice, per LORE.md: warm, unhurried, faintly apologetic about the paperwork. He
    /// should be the most likeable thing in the game. The horror is not that he was cruel — it is
    /// that he was right about the mechanism and wrong about the cost.
    ///
    /// His final dictation should not play as a reveal. Just a man going quiet.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Intake Tape", fileName = "SO_Tape")]
    public sealed class TapeSO : ScriptableObject
    {
        [Serializable]
        public sealed class Cue
        {
            [Min(0f)] public float StartSeconds;

            [Min(0f)] public float EndSeconds;

            [Tooltip("Content key for the spoken line, e.g. tape.intake.03.line.07")]
            public string TextKey = string.Empty;
        }

        [Header("Identity")]
        [Tooltip("Unique per tape. Used by the library to track found/heard.")]
        [SerializeField, Min(1)] private int _tapeId = 1;

        [Tooltip("Content key for the label on the cassette.")]
        [SerializeField] private string _titleKey = string.Empty;

        [Tooltip("Content key for the speaker's name. Almost always Verity.")]
        [SerializeField] private string _speakerKey = "tape.speaker.verity";

        [Tooltip("Year on the label. 1971-1984.")]
        [SerializeField, Range(1971, 1984)] private int _recordedYear = 1974;

        [Header("Audio")]
        [SerializeField] private AudioClip _clip;

        [Tooltip("Falls back to the clip's own length when left at zero.")]
        [SerializeField, Min(0f)] private float _durationOverrideSeconds;

        [Header("Transcript")]
        [Tooltip("Ordered, non-overlapping. Subtitles are not optional — see the tape validator.")]
        [SerializeField] private List<Cue> _cues = new List<Cue>();

        public AudioClip Clip => _clip;

        public TapeId Id => new TapeId(_tapeId);

        public float DurationSeconds
        {
            get
            {
                if (_durationOverrideSeconds > 0f)
                {
                    return _durationOverrideSeconds;
                }

                return _clip != null ? _clip.length : 0f;
            }
        }

        /// <summary>Build the runtime tape. Allocates; call at load.</summary>
        public TapeDefinition Build()
        {
            float duration = DurationSeconds;

            if (duration <= 0f)
            {
                throw new InvalidOperationException(
                    "Tape '" + name + "' has no clip and no duration override, so it has no length.");
            }

            var cues = new TranscriptCue[_cues.Count];
            for (int i = 0; i < _cues.Count; i++)
            {
                Cue cue = _cues[i];
                cues[i] = new TranscriptCue(cue.StartSeconds, cue.EndSeconds, ContentKey.Of(cue.TextKey));
            }

            return new TapeDefinition(
                new TapeId(_tapeId), ContentKey.Of(_titleKey), ContentKey.Of(_speakerKey),
                _recordedYear, duration, cues);
        }

        private void OnValidate()
        {
            // Keep cues ordered as they are authored, so the runtime constructor's ordering check
            // is a backstop rather than the first time anyone finds out.
            for (int i = 0; i < _cues.Count; i++)
            {
                if (_cues[i].EndSeconds > 0f && _cues[i].EndSeconds < _cues[i].StartSeconds)
                {
                    _cues[i].EndSeconds = _cues[i].StartSeconds;
                }
            }
        }
    }
}
