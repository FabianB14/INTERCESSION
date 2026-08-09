using System;
using System.IO;
using Session.Core.Identity;
using Session.Core.Session;
using Session.Core.Spatial;
using Session.Core.Voice;
using Session.Netcode;
using Session.Runtime.Tuning;
using Steamworks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Session.Steam
{
    /// <summary>
    /// Proximity voice over Steam Voice, relayed through the server.
    ///
    /// Flow: the local player captures compressed Steam voice frames and sends them to the server.
    /// The server asks <see cref="ProximityVoiceRouter"/> — which lives in Session.Core and is unit
    /// tested — how loudly each other player should hear them, and forwards the frame only to those
    /// who can hear anything at all, with the gain attached. Receivers decompress and play.
    ///
    /// Relaying through the server rather than peer-to-peer costs a hop and buys the thing this game
    /// cannot do without: a client only ever receives audio it is entitled to hear. Peer-to-peer
    /// voice would let a modified client listen to the whole building, and since the entire game is
    /// players describing rooms to each other, that is not a cheat — it is a full solution.
    ///
    /// Uses NGO named messages rather than RPCs. Voice runs at 20-50 packets per second per speaker,
    /// and RPC array parameters allocate on every receive — exactly the garbage golden rule 6 exists
    /// to prevent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamVoiceRelay : NetworkBehaviour
    {
        private const string InboundMessage = "session.voice.in";
        private const string OutboundMessage = "session.voice.out";
        private const int MaxFrameBytes = 8 * 1024;
        private const int MaxPcmBytes = 64 * 1024;

        [SerializeField] private SessionCatalogSO _catalog;

        [Tooltip("Off = open mic. On = hold the key. Push-to-talk is kinder to a horror game's mix.")]
        [SerializeField] private bool _pushToTalk = true;

        [SerializeField] private KeyCode _pushToTalkKey = KeyCode.V;

        [Tooltip("Capture polls per second. Steam buffers internally; this only sets how often we drain it.")]
        [SerializeField, Min(5)] private int _capturePollsPerSecond = 25;

        private IVoiceRules _voiceRules;

        // Capture path (client -> server). Owned by the local speaker.
        private MemoryStream _captureCompressed;

        // Relay path (server). On a host these must not share storage with the client path, or a
        // player who speaks and listens in the same frame corrupts one of the two.
        private byte[] _relayScratch;

        // Playback path (client). Compressed in, PCM out.
        private byte[] _receiveScratch;
        private MemoryStream _receiveCompressed;
        private MemoryStream _receivePcm;

        private float _pollAccumulator;
        private bool _recording;

        private readonly VoiceListener[] _listeners = new VoiceListener[SessionDirectorNetBehaviour.MaxPlayers];

        /// <summary>
        /// Raised on receiving peers: speaker slot, gain in [0,1], and decompressed 16-bit mono PCM
        /// at <see cref="SteamUser.OptimalSampleRate"/>. The segment is only valid for the duration
        /// of the call — copy what you need.
        /// </summary>
        public event Action<int, float, ArraySegment<byte>> VoiceFrameReceived;

        public override void OnNetworkSpawn()
        {
            _voiceRules = _catalog != null && _catalog.VoiceRules != null
                ? _catalog.VoiceRules
                : DefaultVoiceRules.Instance;

            // Player rigs spawn later than this component, so they find it here rather than
            // through a scene reference.
            SteamVoiceRelayLocator.Relay = this;

            CustomMessagingManager messaging = NetworkManager.CustomMessagingManager;

            if (IsServer)
            {
                _relayScratch = new byte[MaxFrameBytes];
                messaging.RegisterNamedMessageHandler(InboundMessage, OnServerReceivedVoice);
            }

            if (IsClient)
            {
                _captureCompressed = new MemoryStream(MaxFrameBytes);
                _receiveScratch = new byte[MaxFrameBytes];
                _receiveCompressed = new MemoryStream(MaxFrameBytes);
                _receivePcm = new MemoryStream(MaxPcmBytes);

                messaging.RegisterNamedMessageHandler(OutboundMessage, OnClientReceivedVoice);

                SteamUser.VoiceRecord = !_pushToTalk;
                _recording = !_pushToTalk;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null && NetworkManager.CustomMessagingManager != null)
            {
                if (IsServer)
                {
                    NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(InboundMessage);
                }

                if (IsClient)
                {
                    NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(OutboundMessage);
                }
            }

            if (SteamVoiceRelayLocator.Relay == this)
            {
                SteamVoiceRelayLocator.Relay = null;
            }

            SteamUser.VoiceRecord = false;
            _recording = false;
        }

        private void Update()
        {
            if (!IsClient)
            {
                return;
            }

            UpdateRecordingState();

            if (!_recording)
            {
                return;
            }

            _pollAccumulator += Time.deltaTime;
            float interval = 1f / _capturePollsPerSecond;
            if (_pollAccumulator < interval)
            {
                return;
            }

            _pollAccumulator = 0f;
            CaptureAndSend();
        }

        private void UpdateRecordingState()
        {
            if (!_pushToTalk)
            {
                return;
            }

            bool wanted = Input.GetKey(_pushToTalkKey);
            if (wanted == _recording)
            {
                return;
            }

            _recording = wanted;
            SteamUser.VoiceRecord = wanted;
        }

        private void CaptureAndSend()
        {
            if (!SteamUser.HasVoiceData)
            {
                return;
            }

            _captureCompressed.Position = 0;
            _captureCompressed.SetLength(0);

            int compressed = SteamUser.ReadVoiceData(_captureCompressed);
            if (compressed <= 0 || compressed > MaxFrameBytes)
            {
                return;
            }

            // GetBuffer avoids the copy ToArray would make on every single voice frame.
            byte[] raw = _captureCompressed.GetBuffer();

            using var writer = new FastBufferWriter(compressed + sizeof(int), Allocator.Temp);
            writer.WriteValueSafe(compressed);
            writer.WriteBytesSafe(raw, compressed);

            NetworkManager.CustomMessagingManager.SendNamedMessage(
                InboundMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
        }

        // ---- server relay ------------------------------------------------------------------

        private void OnServerReceivedVoice(ulong senderClientId, FastBufferReader reader)
        {
            SessionDirectorNetBehaviour director = SessionDirectorNetBehaviour.Instance;
            if (director == null || director.Director == null)
            {
                return;
            }

            if (!director.TryGetSlot(senderClientId, out int speakerSlot))
            {
                return;
            }

            reader.ReadValueSafe(out int length);
            if (length <= 0 || length > MaxFrameBytes)
            {
                return;
            }

            reader.ReadBytesSafe(ref _relayScratch, length);

            BuildListenerSnapshot(director);
            VoiceListener speaker = _listeners[speakerSlot];

            for (int slot = 0; slot < _listeners.Length; slot++)
            {
                float gain = ProximityVoiceRouter.GainFor(in speaker, in _listeners[slot], _voiceRules);
                if (gain <= 0f)
                {
                    continue;
                }

                using var writer = new FastBufferWriter(
                    length + sizeof(int) + sizeof(float) + sizeof(int), Allocator.Temp);

                writer.WriteValueSafe(speakerSlot);
                writer.WriteValueSafe(gain);
                writer.WriteValueSafe(length);
                writer.WriteBytesSafe(_relayScratch, length);

                NetworkManager.CustomMessagingManager.SendNamedMessage(
                    OutboundMessage, director.ClientIdForSlot(slot), writer, NetworkDelivery.Unreliable);
            }
        }

        private void BuildListenerSnapshot(SessionDirectorNetBehaviour director)
        {
            SessionDirector session = director.Director;

            for (int slot = 0; slot < _listeners.Length; slot++)
            {
                var player = new PlayerId(slot);
                bool connected = session.IsConnected(player);

                // Positions come from the movement checker, which is the server's validated view.
                // Trusting a client-reported position for voice would let someone claim to be
                // standing in a room they are not in and listen to it.
                Vec3 position = connected ? director.Movement.PositionOf(player) : Vec3.Zero;

                _listeners[slot] = new VoiceListener(
                    player, position, connected ? session.RoomOf(player) : RoomId.None, connected);
            }
        }

        // ---- client playback ---------------------------------------------------------------

        private void OnClientReceivedVoice(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int speakerSlot);
            reader.ReadValueSafe(out float gain);
            reader.ReadValueSafe(out int length);

            if (length <= 0 || length > MaxFrameBytes)
            {
                return;
            }

            reader.ReadBytesSafe(ref _receiveScratch, length);

            // Use the (Stream, length, Stream) overload. The byte[] overload decompresses the whole
            // array, and _receiveScratch is a fixed 8KB buffer holding a much shorter frame — it
            // would feed Steam kilobytes of stale bytes from the previous packet.
            _receiveCompressed.Position = 0;
            _receiveCompressed.SetLength(0);
            _receiveCompressed.Write(_receiveScratch, 0, length);
            _receiveCompressed.Position = 0;

            _receivePcm.Position = 0;
            _receivePcm.SetLength(0);

            int decompressed = SteamUser.DecompressVoice(_receiveCompressed, length, _receivePcm);
            if (decompressed <= 0)
            {
                return;
            }

            VoiceFrameReceived?.Invoke(
                speakerSlot, gain, new ArraySegment<byte>(_receivePcm.GetBuffer(), 0, decompressed));
        }
    }
}
