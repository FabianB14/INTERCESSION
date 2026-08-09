using System;
using System.Collections.Generic;
using Session.Core.Attendant;
using Session.Core.Identity;
using Session.Core.Movement;
using Session.Core.Perception;
using Session.Core.Puzzles;
using Session.Core.Rooms;
using Session.Core.Session;
using Session.Core.Spatial;
using Session.Netcode.Wire;
using Session.Runtime.Tuning;
using Unity.Netcode;
using UnityEngine;

namespace Session.Netcode
{
    /// <summary>
    /// The server's authority, on the network. Owns the one <see cref="SessionDirector"/> for the
    /// run and translates between client RPCs and its plain-C# API.
    ///
    /// This class is deliberately thin. It contains no puzzle rules, no lens logic and no
    /// Attendant behaviour — read it and you should see nothing but plumbing. If an `if` about
    /// game rules ever appears in this file, it belongs in Session.Core.
    ///
    /// Authority model (golden rule 5):
    ///   - Puzzle and door state: server only. Clients submit tokens, never outcomes.
    ///   - Movement and look: client-authoritative, server sanity-checked. See PlayerMotorNetBehaviour.
    ///   - Perception: derived locally on every peer from the replicated session seed. No variant
    ///     ids are ever sent, which keeps a modified client from reading another player's room.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionDirectorNetBehaviour : NetworkBehaviour
    {
        public static SessionDirectorNetBehaviour Instance { get; private set; }

        [SerializeField] private SessionCatalogSO _catalog;

        [Tooltip("Server tick rate for the director. Independent of frame rate.")]
        [SerializeField, Min(1)] private int _ticksPerSecond = 20;

        /// <summary>
        /// The seed every peer derives its lenses from. Replicated once at start; clients need it
        /// to compute their own perception without the server sending per-prop data.
        /// </summary>
        private readonly NetworkVariable<ulong> _sessionSeed = new NetworkVariable<ulong>();

        private SessionDirector _director;
        private MovementSanityChecker _movement;
        private ILensRules _lensRules;

        // Steam/NGO client ids are opaque ulongs; Session.Core wants dense slots 0..3.
        private readonly Dictionary<ulong, int> _slotByClientId = new Dictionary<ulong, int>();
        private readonly ulong[] _clientIdBySlot = new ulong[MaxPlayers];
        private readonly bool[] _slotOccupied = new bool[MaxPlayers];

        private SessionEvent[] _eventScratch = Array.Empty<SessionEvent>();
        private float _tickAccumulator;

        public const int MaxPlayers = 4;

        /// <summary>Server-only. Null on clients — they must never hold canonical puzzle state.</summary>
        public SessionDirector Director => _director;

        public MovementSanityChecker Movement => _movement;

        public ulong SessionSeed => _sessionSeed.Value;

        /// <summary>Raised on every peer when the server reports something happened.</summary>
        public event Action<SessionEvent> SessionEventReceived;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (!IsServer)
            {
                return;
            }

            if (_catalog == null)
            {
                Debug.LogError("[Session] SessionDirectorNetBehaviour has no catalog assigned. Cannot start a run.");
                enabled = false;
                return;
            }

            // Careful: `??` is wrong on Unity Object references. A destroyed object, or a
            // reference to an asset that has gone missing, is "fake null" — its overloaded ==
            // returns true while the C# reference is still non-null, so ?? hands back a dead
            // object instead of the fallback. Compare with == null explicitly.
            _lensRules = _catalog.LensRules == null ? DefaultLensRules.Instance : _catalog.LensRules;

            List<RoomDefinition> rooms;
            try
            {
                rooms = _catalog.BuildRooms();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Session] Could not build rooms: " + exception.Message);
                enabled = false;
                return;
            }

            if (rooms.Count == 0)
            {
                Debug.LogError("[Session] Catalog contains no usable rooms.");
                enabled = false;
                return;
            }

            // Seeded from the network time at run start so each session differs, then replicated so
            // every peer derives identical lenses. Never regenerate this mid-run.
            _sessionSeed.Value = (ulong)DateTime.UtcNow.Ticks;

            IAttendantProfile attendantProfile = _catalog.AttendantProfile == null
                ? DefaultAttendantProfile.Instance
                : _catalog.AttendantProfile;

            IMovementRules movementRules = _catalog.MovementRules == null
                ? DefaultMovementRules.Instance
                : _catalog.MovementRules;

            _director = new SessionDirector(_sessionSeed.Value, rooms, _lensRules, attendantProfile, MaxPlayers);
            _movement = new MovementSanityChecker(movementRules, MaxPlayers);

            _eventScratch = new SessionEvent[32];

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // The host is already connected by the time we spawn.
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                OnClientConnected(clientId);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---- slot mapping ------------------------------------------------------------------

        private void OnClientConnected(ulong clientId)
        {
            if (_slotByClientId.ContainsKey(clientId))
            {
                return;
            }

            int slot = -1;
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (!_slotOccupied[i])
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                Debug.LogWarning("[Session] Client " + clientId + " connected but the session is full.");
                NetworkManager.DisconnectClient(clientId);
                return;
            }

            _slotOccupied[slot] = true;
            _clientIdBySlot[slot] = clientId;
            _slotByClientId[clientId] = slot;

            _director.PlayerConnected(new PlayerId(slot));
            AssignSlotRpc(slot, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!_slotByClientId.TryGetValue(clientId, out int slot))
            {
                return;
            }

            _slotByClientId.Remove(clientId);
            _slotOccupied[slot] = false;

            var player = new PlayerId(slot);
            _director.PlayerDisconnected(player);
            _movement.Forget(player);
        }

        /// <summary>Which player slot this peer occupies. -1 until the server has said.</summary>
        public int LocalSlot { get; private set; } = -1;

        [Rpc(SendTo.SpecifiedInParams)]
        private void AssignSlotRpc(int slot, RpcParams rpcParams = default)
        {
            LocalSlot = slot;
        }

        public bool TryGetSlot(ulong clientId, out int slot) => _slotByClientId.TryGetValue(clientId, out slot);

        public ulong ClientIdForSlot(int slot) => _clientIdBySlot[slot];

        // ---- tick --------------------------------------------------------------------------

        private void Update()
        {
            if (!IsServer || _director == null)
            {
                return;
            }

            float step = 1f / _ticksPerSecond;
            _tickAccumulator += Time.deltaTime;

            // Fixed-step so the Attendant's timings are frame-rate independent. Bounded iterations
            // so a hitch cannot spiral into a long catch-up loop.
            int guard = 0;
            while (_tickAccumulator >= step && guard < 8)
            {
                _tickAccumulator -= step;
                guard++;
                _director.Tick(step);
            }

            if (guard == 8)
            {
                _tickAccumulator = 0f;
            }

            PublishEvents();
        }

        private void PublishEvents()
        {
            int count = _director.DrainEvents(_eventScratch);
            for (int i = 0; i < count; i++)
            {
                BroadcastEventRpc(SessionEventWire.From(in _eventScratch[i]));
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastEventRpc(SessionEventWire wire)
        {
            SessionEventReceived?.Invoke(wire.ToEvent());
        }

        // ---- client commands ---------------------------------------------------------------

        /// <summary>
        /// Submit an attempt at a puzzle. The client sends the tokens it entered — never a claim
        /// that the puzzle is solved. The server adjudicates against the canonical solution, which
        /// only it holds.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void SubmitPuzzleRpc(int room, int node, int[] tokens, RpcParams rpcParams = default)
        {
            if (!_slotByClientId.TryGetValue(rpcParams.Receive.SenderClientId, out int slot))
            {
                return;
            }

            if (tokens == null || tokens.Length == 0 || tokens.Length > 16)
            {
                // Malformed or absurd. Drop it silently rather than feeding it to the director.
                return;
            }

            AttemptOutcome outcome = _director.SubmitPuzzle(
                new PlayerId(slot), new RoomId(room), new PuzzleNodeId(node), tokens);

            PuzzleResultRpc(room, node, (byte)outcome,
                RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        /// <summary>Result of this peer's own attempt. Never broadcast — other players' guesses are their business.</summary>
        public event Action<RoomId, PuzzleNodeId, AttemptOutcome> PuzzleAttemptResolved;

        [Rpc(SendTo.SpecifiedInParams)]
        private void PuzzleResultRpc(int room, int node, byte outcome, RpcParams rpcParams = default)
        {
            PuzzleAttemptResolved?.Invoke(new RoomId(room), new PuzzleNodeId(node), (AttemptOutcome)outcome);
        }

        [Rpc(SendTo.Server)]
        public void ReportRoomEnteredRpc(int room, RpcParams rpcParams = default)
        {
            if (_slotByClientId.TryGetValue(rpcParams.Receive.SenderClientId, out int slot))
            {
                _director.PlayerEnteredRoom(new PlayerId(slot), new RoomId(room));
            }
        }

        [Rpc(SendTo.Server)]
        public void ReportDoorForcedRpc(int room, RpcParams rpcParams = default)
        {
            if (_slotByClientId.TryGetValue(rpcParams.Receive.SenderClientId, out int slot))
            {
                _director.DoorForced(new PlayerId(slot), new RoomId(room));
            }
        }

        // ---- movement ----------------------------------------------------------------------

        /// <summary>
        /// Server-side validation hook used by PlayerMotorNetBehaviour. Returns the verdict so the
        /// motor can decide whether to send a correction.
        /// </summary>
        public MovementVerdict ValidateMovement(int slot, Vector3 position, float deltaSeconds, bool sprinting)
        {
            return _movement.Validate(
                new PlayerId(slot), new Vec3(position.x, position.y, position.z), deltaSeconds, sprinting);
        }

        public void PlaceMovement(int slot, Vector3 position)
        {
            _movement.Teleport(new PlayerId(slot), new Vec3(position.x, position.y, position.z));
        }
    }
}
