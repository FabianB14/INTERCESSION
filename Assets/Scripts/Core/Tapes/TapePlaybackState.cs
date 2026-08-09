using System;

namespace Session.Core.Tapes
{
    public enum TapeTransport
    {
        Stopped = 0,
        Playing = 1,
        Paused = 2
    }

    /// <summary>
    /// The deck's clock. Runs on the server as the authority and on every client as a local
    /// prediction, with <see cref="SyncTo"/> reconciling the two.
    ///
    /// Two decisions worth naming:
    ///
    /// <b>Pausing resumes; stopping rewinds.</b> A tape is ninety seconds of someone talking
    /// slowly, and the Attendant does not stop walking while you listen. If every interruption cost
    /// you the whole tape, players would learn to never start one, and the best story asset in the
    /// project would go unheard. So the deck remembers where it was.
    ///
    /// <b>Sync corrects drift, it does not chase it.</b> Snapping the audio to the server's
    /// position on every update would make a tape stutter continuously over a relay. It only jumps
    /// when the gap is large enough to actually hear.
    /// </summary>
    public sealed class TapePlaybackState
    {
        /// <summary>
        /// Seconds of divergence tolerated before a client re-seeks. Below roughly a fifth of a
        /// second a listener cannot tell; above it, two players in a room hear an audible echo of
        /// each other's speakers.
        /// </summary>
        public const float DefaultResyncToleranceSeconds = 0.2f;

        private TapeDefinition? _tape;
        private float _position;
        private TapeTransport _transport;
        private int _cueIndex = -1;
        private bool _finished;

        /// <summary>Raised when the visible transcript line changes, including to nothing.</summary>
        public event Action<int>? CueChanged;

        /// <summary>Raised once when the tape reaches its end under its own power.</summary>
        public event Action? Finished;

        public TapeDefinition? Tape => _tape;

        public TapeTransport Transport => _transport;

        public bool IsPlaying => _transport == TapeTransport.Playing;

        public bool IsLoaded => _tape != null;

        public float PositionSeconds => _position;

        /// <summary>Index of the transcript line currently spoken, or -1 between lines.</summary>
        public int CueIndex => _cueIndex;

        /// <summary>True once the tape has run to its end. Cleared by <see cref="Stop"/>.</summary>
        public bool HasFinished => _finished;

        public float RemainingSeconds => _tape == null ? 0f : Math.Max(0f, _tape.DurationSeconds - _position);

        public float NormalisedPosition =>
            _tape == null || _tape.DurationSeconds <= 0f ? 0f : _position / _tape.DurationSeconds;

        public void Load(TapeDefinition tape)
        {
            _tape = tape ?? throw new ArgumentNullException(nameof(tape));
            _position = 0f;
            _transport = TapeTransport.Stopped;
            _finished = false;
            SetCue(-1);
        }

        public void Unload()
        {
            _tape = null;
            _position = 0f;
            _transport = TapeTransport.Stopped;
            _finished = false;
            SetCue(-1);
        }

        /// <summary>Start, or resume from wherever it was paused.</summary>
        public bool Play()
        {
            if (_tape == null || _transport == TapeTransport.Playing)
            {
                return false;
            }

            // Pressing play on a finished tape starts it over rather than doing nothing, which is
            // what someone reaching for a tape recorder expects.
            if (_finished || _position >= _tape.DurationSeconds)
            {
                _position = 0f;
                _finished = false;
            }

            _transport = TapeTransport.Playing;
            RefreshCue();
            return true;
        }

        /// <summary>Hold position. The tape resumes from here.</summary>
        public bool Pause()
        {
            if (_transport != TapeTransport.Playing)
            {
                return false;
            }

            _transport = TapeTransport.Paused;
            return true;
        }

        /// <summary>Stop and rewind.</summary>
        public bool Stop()
        {
            if (_tape == null || _transport == TapeTransport.Stopped)
            {
                return false;
            }

            _transport = TapeTransport.Stopped;
            _position = 0f;
            _finished = false;
            SetCue(-1);
            return true;
        }

        public bool Seek(float seconds)
        {
            if (_tape == null)
            {
                return false;
            }

            float clamped = seconds < 0f ? 0f
                : seconds > _tape.DurationSeconds ? _tape.DurationSeconds
                : seconds;

            if (Math.Abs(clamped - _position) < 0.0001f)
            {
                return false;
            }

            _position = clamped;
            _finished = false;
            RefreshCue();
            return true;
        }

        /// <summary>Advance the clock. Call from the server tick, and locally on clients.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            if (_tape == null || _transport != TapeTransport.Playing)
            {
                return;
            }

            _position += deltaSeconds;

            if (_position < _tape.DurationSeconds)
            {
                RefreshCue();
                return;
            }

            _position = _tape.DurationSeconds;
            _transport = TapeTransport.Paused;
            _finished = true;

            SetCue(-1);
            Finished?.Invoke();
        }

        /// <summary>
        /// Reconcile against the server. Only seeks when the gap is audible — see the note on
        /// <see cref="DefaultResyncToleranceSeconds"/>.
        /// </summary>
        public bool SyncTo(
            float authoritativePosition,
            TapeTransport authoritativeTransport,
            float toleranceSeconds = DefaultResyncToleranceSeconds)
        {
            if (_tape == null)
            {
                return false;
            }

            bool changed = false;

            if (_transport != authoritativeTransport)
            {
                _transport = authoritativeTransport;
                changed = true;
            }

            if (ShouldResync(_position, authoritativePosition, toleranceSeconds))
            {
                _position = authoritativePosition < 0f ? 0f
                    : authoritativePosition > _tape.DurationSeconds ? _tape.DurationSeconds
                    : authoritativePosition;

                changed = true;
            }

            if (changed)
            {
                _finished = _position >= _tape.DurationSeconds && _transport != TapeTransport.Playing;
                RefreshCue();
            }

            return changed;
        }

        public static bool ShouldResync(float local, float authoritative, float toleranceSeconds)
            => Math.Abs(local - authoritative) > toleranceSeconds;

        private void RefreshCue()
        {
            if (_tape == null)
            {
                SetCue(-1);
                return;
            }

            SetCue(_tape.CueIndexAt(_position));
        }

        private void SetCue(int index)
        {
            if (_cueIndex == index)
            {
                return;
            }

            _cueIndex = index;
            CueChanged?.Invoke(index);
        }
    }
}
