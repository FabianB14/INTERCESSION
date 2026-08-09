using System;
using Session.Core.Identity;
using Session.Core.Tapes;
using Session.Runtime.Tuning;
using Unity.Netcode;
using UnityEngine;

namespace Session.Netcode
{
    /// <summary>
    /// A tape recorder in the world. Server owns the transport; every client runs the same clock
    /// locally and only re-seeks when it drifts audibly.
    ///
    /// Playback is shared on purpose. The deck is a physical object in a room, so its AudioSource is
    /// 3D and everyone nearby hears it — which is the point. A tape is the one moment in a run when
    /// two players who are seeing different rooms are hearing exactly the same thing, and it costs
    /// them both the same ninety seconds of standing still while the Attendant is somewhere in the
    /// building.
    ///
    /// Position is replicated at a slow tick rather than every frame. Audio does not need
    /// twenty updates a second to stay in sync; it needs to not be yanked around.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class TapeDeckNetBehaviour : NetworkBehaviour
    {
        [SerializeField] private TapeSO _tape;

        [Tooltip("Server position broadcasts per second. Audio is forgiving; do not raise this.")]
        [SerializeField, Range(1, 10)] private int _syncsPerSecond = 2;

        [Tooltip("Seconds of drift tolerated before a client re-seeks its AudioSource.")]
        [SerializeField, Range(0.05f, 1f)] private float _resyncTolerance = TapePlaybackState.DefaultResyncToleranceSeconds;

        private readonly NetworkVariable<float> _networkPosition = new NetworkVariable<float>();

        private readonly NetworkVariable<byte> _networkTransport =
            new NetworkVariable<byte>((byte)TapeTransport.Stopped);

        private readonly TapePlaybackState _playback = new TapePlaybackState();

        private AudioSource _source;
        private TapeDefinition _definition;
        private float _syncAccumulator;

        /// <summary>Raised on every peer when the spoken line changes. -1 means between lines.</summary>
        public event Action<TapeDefinition, int> CueChanged;

        /// <summary>Raised on every peer when the tape runs to its end.</summary>
        public event Action<TapeId> TapeFinished;

        public TapeDefinition Definition => _definition;

        public TapePlaybackState Playback => _playback;

        public bool IsPlaying => _playback.IsPlaying;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
        }

        public override void OnNetworkSpawn()
        {
            if (_tape == null)
            {
                Debug.LogError("[Session] Tape deck '" + name + "' has no tape assigned.");
                enabled = false;
                return;
            }

            try
            {
                _definition = _tape.Build();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Session] Tape deck '" + name + "': " + exception.Message);
                enabled = false;
                return;
            }

            _playback.Load(_definition);
            _playback.CueChanged += OnCueChanged;
            _playback.Finished += OnFinished;

            _source.clip = _tape.Clip;
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 1f;

            _networkTransport.OnValueChanged += OnTransportReplicated;
        }

        public override void OnNetworkDespawn()
        {
            _playback.CueChanged -= OnCueChanged;
            _playback.Finished -= OnFinished;
            _networkTransport.OnValueChanged -= OnTransportReplicated;
        }

        // ---- commands ----------------------------------------------------------------------

        /// <summary>Called by the interaction system when a player presses play on the deck.</summary>
        [Rpc(SendTo.Server)]
        public void TogglePlayRpc(RpcParams rpcParams = default)
        {
            if (_definition == null)
            {
                return;
            }

            if (_playback.IsPlaying)
            {
                _playback.Pause();
            }
            else
            {
                _playback.Play();
            }

            PublishTransport();
        }

        [Rpc(SendTo.Server)]
        public void StopRpc(RpcParams rpcParams = default)
        {
            if (_playback.Stop())
            {
                PublishTransport();
            }
        }

        private void PublishTransport()
        {
            _networkTransport.Value = (byte)_playback.Transport;
            _networkPosition.Value = _playback.PositionSeconds;
        }

        // ---- tick --------------------------------------------------------------------------

        private void Update()
        {
            if (_definition == null)
            {
                return;
            }

            float delta = Time.deltaTime;

            // Everyone runs the clock. The server's is authoritative; clients' are predictions that
            // get corrected below, which keeps subtitles in step without a message per line.
            _playback.Tick(delta);

            if (IsServer)
            {
                TickServerSync(delta);
            }
            else
            {
                _playback.SyncTo(_networkPosition.Value, (TapeTransport)_networkTransport.Value, _resyncTolerance);
            }

            DriveAudioSource();
        }

        private void TickServerSync(float delta)
        {
            _syncAccumulator += delta;

            float interval = 1f / _syncsPerSecond;
            if (_syncAccumulator < interval)
            {
                return;
            }

            _syncAccumulator = 0f;

            _networkPosition.Value = _playback.PositionSeconds;
            _networkTransport.Value = (byte)_playback.Transport;
        }

        private void DriveAudioSource()
        {
            if (_source.clip == null)
            {
                return;
            }

            if (!_playback.IsPlaying)
            {
                if (_source.isPlaying)
                {
                    _source.Pause();
                }

                return;
            }

            if (!_source.isPlaying)
            {
                _source.time = Mathf.Clamp(_playback.PositionSeconds, 0f, Mathf.Max(0f, _source.clip.length - 0.01f));
                _source.Play();
                return;
            }

            // Only nudge the AudioSource when it has drifted from the model audibly. Assigning
            // .time every frame restarts the decoder and produces a continuous click.
            if (TapePlaybackState.ShouldResync(_source.time, _playback.PositionSeconds, _resyncTolerance))
            {
                _source.time = Mathf.Clamp(_playback.PositionSeconds, 0f, Mathf.Max(0f, _source.clip.length - 0.01f));
            }
        }

        private void OnTransportReplicated(byte previous, byte current)
        {
            if (IsServer)
            {
                return;
            }

            _playback.SyncTo(_networkPosition.Value, (TapeTransport)current, _resyncTolerance);
        }

        private void OnCueChanged(int cueIndex) => CueChanged?.Invoke(_definition, cueIndex);

        private void OnFinished() => TapeFinished?.Invoke(_definition.Id);
    }
}
