using System;
using Steamworks;
using UnityEngine;

namespace Session.Steam
{
    /// <summary>
    /// Plays one remote player's voice through a spatialised AudioSource.
    ///
    /// Steam hands back 16-bit signed mono PCM. This converts to float and feeds a ring buffer that
    /// a streaming AudioClip drains on the audio thread. The ring buffer is the important part:
    /// network frames arrive in bursts on the main thread while the audio thread pulls at a steady
    /// rate, and the two must never block each other.
    ///
    /// Attach to each player rig. The AudioSource should be 3D with spatialBlend at 1 — proximity
    /// gain from the router multiplies on top of Unity's own distance attenuation, which is
    /// deliberate: the router decides who is entitled to hear, Unity decides how it sits in space.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class VoicePlayback : MonoBehaviour
    {
        [Tooltip("Player slot this rig represents. Set by the spawn logic.")]
        [SerializeField] private int _slot = -1;

        [Tooltip("Ring buffer length in seconds. Larger absorbs more jitter at the cost of latency.")]
        [SerializeField, Range(0.1f, 1f)] private float _bufferSeconds = 0.4f;

        private AudioSource _source;
        private float[] _ring;
        private int _writeIndex;
        private int _readIndex;
        private int _sampleRate;

        // Incremented on the main thread as frames arrive, decremented on the audio thread as they
        // play. `volatile` would not be enough — ++ and -- are read-modify-write, so two threads
        // can interleave and lose a count. Interlocked is the only correct option here.
        private int _available;

        public int Slot
        {
            get => _slot;
            set => _slot = value;
        }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            _sampleRate = (int)SteamUser.OptimalSampleRate;
            if (_sampleRate <= 0)
            {
                _sampleRate = 24000;
            }

            int ringSamples = Mathf.Max(1024, Mathf.CeilToInt(_sampleRate * _bufferSeconds));
            _ring = new float[ringSamples];
            _writeIndex = 0;
            _readIndex = 0;
            _available = 0;

            // Streaming clip: Unity pulls from PcmRead on the audio thread as it needs samples.
            AudioClip clip = AudioClip.Create(
                "SessionVoice_" + _slot, ringSamples, 1, _sampleRate, true, PcmRead);

            _source.clip = clip;
            _source.loop = true;
            _source.spatialBlend = 1f;
            _source.Play();

            if (SteamVoiceRelayLocator.Relay != null)
            {
                SteamVoiceRelayLocator.Relay.VoiceFrameReceived += OnVoiceFrame;
            }
        }

        private void OnDisable()
        {
            if (SteamVoiceRelayLocator.Relay != null)
            {
                SteamVoiceRelayLocator.Relay.VoiceFrameReceived -= OnVoiceFrame;
            }

            if (_source != null)
            {
                _source.Stop();
            }
        }

        private void OnVoiceFrame(int speakerSlot, float gain, ArraySegment<byte> pcm)
        {
            if (speakerSlot != _slot || _ring == null)
            {
                return;
            }

            _source.volume = gain;

            byte[] bytes = pcm.Array;
            int offset = pcm.Offset;
            int sampleCount = pcm.Count / 2;

            for (int i = 0; i < sampleCount; i++)
            {
                int index = offset + (i * 2);
                short sample = (short)(bytes[index] | (bytes[index + 1] << 8));

                _ring[_writeIndex] = sample * (1f / 32768f);
                _writeIndex = (_writeIndex + 1) % _ring.Length;

                if (System.Threading.Volatile.Read(ref _available) < _ring.Length)
                {
                    System.Threading.Interlocked.Increment(ref _available);
                }
                else
                {
                    // Overrun: the network is ahead of playback. Drop the oldest sample rather than
                    // let latency grow without bound — a voice that lags further behind every
                    // second is worse than one that clips occasionally.
                    _readIndex = (_readIndex + 1) % _ring.Length;
                }
            }
        }

        private void PcmRead(float[] data)
        {
            float[] ring = _ring;
            if (ring == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (System.Threading.Volatile.Read(ref _available) <= 0)
                {
                    // Underrun. Silence is the correct fill; repeating the last sample buzzes.
                    data[i] = 0f;
                    continue;
                }

                data[i] = ring[_readIndex];
                _readIndex = (_readIndex + 1) % ring.Length;
                System.Threading.Interlocked.Decrement(ref _available);
            }
        }
    }

    /// <summary>
    /// Lets playback components find the relay without a scene reference, since the relay lives on
    /// the network manager rig and playbacks live on player rigs spawned later.
    /// </summary>
    public static class SteamVoiceRelayLocator
    {
        public static SteamVoiceRelay Relay { get; set; }
    }
}
