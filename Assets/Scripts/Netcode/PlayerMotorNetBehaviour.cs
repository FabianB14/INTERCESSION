using Session.Core.Movement;
using Unity.Netcode;
using UnityEngine;

namespace Session.Netcode
{
    /// <summary>
    /// Client-authoritative movement with server sanity checks, per golden rule 5.
    ///
    /// The owning client moves itself and tells the server where it ended up. Over a Steam relay
    /// this is the difference between a game that feels responsive and one that does not, and the
    /// worst outcome of a movement cheat here is walking through a room faster — puzzle and door
    /// state are server-owned and cannot be touched this way.
    ///
    /// The server clamps or rejects anything past what was physically possible and sends a
    /// correction. Corrections are deliberately rare and quiet: a false positive that yanks an
    /// honest player backwards is far worse than a cheater covering ten extra metres.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotorNetBehaviour : NetworkBehaviour
    {
        [Tooltip("Position reports per second to the server. Look direction rides along.")]
        [SerializeField, Min(1)] private int _reportsPerSecond = 20;

        [Tooltip("Distance a correction must exceed before the client is snapped. Below this it is eased.")]
        [SerializeField, Min(0f)] private float _hardSnapMeters = 2f;

        [Tooltip("How quickly an eased correction is absorbed. Metres per second.")]
        [SerializeField, Min(0.1f)] private float _softCorrectionSpeed = 4f;

        /// <summary>Replicated for remote peers to render. Owner writes, everyone reads.</summary>
        private readonly NetworkVariable<Vector3> _networkPosition =
            new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<float> _networkYaw =
            new NetworkVariable<float>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private CharacterController _controller;
        private Transform _transform;
        private float _reportAccumulator;
        private float _timeSinceLastReport;
        private Vector3 _pendingCorrection;
        private bool _hasPendingCorrection;
        private int _slot = -1;

        private void Awake()
        {
            // Cached in Awake — golden rule 6 forbids GetComponent on a per-frame path.
            _controller = GetComponent<CharacterController>();
            _transform = transform;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && SessionDirectorNetBehaviour.Instance != null)
            {
                if (SessionDirectorNetBehaviour.Instance.TryGetSlot(OwnerClientId, out int slot))
                {
                    _slot = slot;
                    SessionDirectorNetBehaviour.Instance.PlaceMovement(slot, _transform.position);
                }
            }
        }

        private void Update()
        {
            if (IsOwner)
            {
                TickOwner();
            }
            else
            {
                TickRemote();
            }
        }

        private void TickOwner()
        {
            float delta = Time.deltaTime;
            _timeSinceLastReport += delta;

            if (_hasPendingCorrection)
            {
                ApplyCorrection(delta);
            }

            _networkYaw.Value = _transform.eulerAngles.y;

            float interval = 1f / _reportsPerSecond;
            _reportAccumulator += delta;
            if (_reportAccumulator < interval)
            {
                return;
            }

            _reportAccumulator = 0f;

            Vector3 position = _transform.position;
            _networkPosition.Value = position;

            // Sprint state is the client's claim; the server only uses it to widen the speed
            // ceiling, so lying about it buys at most the sprint multiplier.
            bool sprinting = _controller.velocity.sqrMagnitude >
                             GetSprintProbeThreshold();

            ReportPositionRpc(position, _timeSinceLastReport, sprinting);
            _timeSinceLastReport = 0f;
        }

        private float GetSprintProbeThreshold()
        {
            // Squared walk speed, so the probe costs no square root on a per-frame path.
            const float NominalWalk = 4.5f;
            return NominalWalk * NominalWalk;
        }

        private void TickRemote()
        {
            // Remote peers are rendered from replicated state. NetworkVariable interpolation is
            // intentionally not used for position here so the character stays on the ground plane;
            // smoothing is the animation layer's job.
            _transform.position = _networkPosition.Value;

            Vector3 euler = _transform.eulerAngles;
            euler.y = _networkYaw.Value;
            _transform.eulerAngles = euler;
        }

        private void ApplyCorrection(float delta)
        {
            Vector3 current = _transform.position;
            float distance = Vector3.Distance(current, _pendingCorrection);

            if (distance > _hardSnapMeters)
            {
                // Too far to hide. Snap, and disable the controller for the frame so it does not
                // fight the teleport.
                _controller.enabled = false;
                _transform.position = _pendingCorrection;
                _controller.enabled = true;
                _hasPendingCorrection = false;
                return;
            }

            Vector3 next = Vector3.MoveTowards(current, _pendingCorrection, _softCorrectionSpeed * delta);
            _controller.Move(next - current);

            if (Vector3.Distance(_transform.position, _pendingCorrection) < 0.05f)
            {
                _hasPendingCorrection = false;
            }
        }

        [Rpc(SendTo.Server)]
        private void ReportPositionRpc(Vector3 position, float deltaSeconds, bool sprinting, RpcParams rpcParams = default)
        {
            SessionDirectorNetBehaviour director = SessionDirectorNetBehaviour.Instance;
            if (director == null || director.Movement == null || _slot < 0)
            {
                return;
            }

            MovementVerdict verdict = director.ValidateMovement(_slot, position, deltaSeconds, sprinting);

            if (!verdict.RequiresCorrection)
            {
                return;
            }

            var corrected = new Vector3(
                verdict.AcceptedPosition.X, verdict.AcceptedPosition.Y, verdict.AcceptedPosition.Z);

            CorrectPositionRpc(corrected, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void CorrectPositionRpc(Vector3 position, RpcParams rpcParams = default)
        {
            _pendingCorrection = position;
            _hasPendingCorrection = true;
        }
    }
}
