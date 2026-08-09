using Session.Core.Identity;
using Session.Core.Tapes;
using Session.UI.Tapes;
using UnityEngine;

namespace Session.Netcode
{
    /// <summary>
    /// Connects the tape decks in a scene to the transcript overlay and the run's tape library.
    ///
    /// Keeps Session.UI free of NGO, same as the HUD binder. Also the only place that decides a
    /// tape counts as "heard" — which is when it reaches its end, not when it is started, because
    /// starting a tape and walking out is exactly what the Attendant is there to discourage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TapeBinder : MonoBehaviour
    {
        [SerializeField] private TranscriptView _transcript;

        [Tooltip("Every deck in this scene. Populated by the room prefab.")]
        [SerializeField] private TapeDeckNetBehaviour[] _decks = new TapeDeckNetBehaviour[0];

        private readonly TapeLibrary _library = new TapeLibrary();

        public TapeLibrary Library => _library;

        private void OnEnable()
        {
            for (int i = 0; i < _decks.Length; i++)
            {
                if (_decks[i] == null)
                {
                    continue;
                }

                _decks[i].CueChanged += OnCueChanged;
                _decks[i].TapeFinished += OnTapeFinished;
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _decks.Length; i++)
            {
                if (_decks[i] == null)
                {
                    continue;
                }

                _decks[i].CueChanged -= OnCueChanged;
                _decks[i].TapeFinished -= OnTapeFinished;
            }
        }

        private void OnCueChanged(TapeDefinition tape, int cueIndex)
        {
            if (_transcript == null || tape == null)
            {
                return;
            }

            // Finding a tape is the moment you first hear it speak, not the moment you touch the
            // deck — a deck someone else pressed play on across the room still counts.
            _library.MarkFound(tape.Id);
            _transcript.Show(tape, cueIndex);
        }

        private void OnTapeFinished(TapeId tape)
        {
            _library.MarkHeard(tape);
        }
    }
}
