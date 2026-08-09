using Session.Core.Attendant;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Session.Netcode
{
    /// <summary>
    /// Walks the Attendant. The decision of <i>whether</i> to walk, and where, is made by the
    /// deterministic state machine in Session.Core; this only translates an intent into NavMesh
    /// movement and replicates enough for clients to animate and hear it.
    ///
    /// It walks. Always. There is no code path in this file that sets a position directly, and
    /// there must never be one — players locate it by sound, and a teleport breaks the contract
    /// that makes the whole game learnable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AttendantNetBehaviour : NetworkBehaviour
    {
        [Tooltip("Waypoints the Attendant patrols when Observing. In corridor order.")]
        [SerializeField] private Transform[] _patrolRoute = new Transform[0];

        [Tooltip("Door anchor per room, indexed alongside the catalog's rooms.")]
        [SerializeField] private RoomAnchor[] _roomAnchors = new RoomAnchor[0];

        [Tooltip("Distance at which the agent counts as having arrived.")]
        [SerializeField, Min(0.1f)] private float _arrivalToleranceMeters = 1.2f;

        [System.Serializable]
        public struct RoomAnchor
        {
            [Min(1)] public int RoomNumber;
            public Transform DoorAnchor;
            public Transform InteriorAnchor;
        }

        /// <summary>Replicated so clients can drive animation and footstep audio.</summary>
        private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>();

        private readonly NetworkVariable<byte> _intent = new NetworkVariable<byte>();

        private NavMeshAgent _agent;
        private Transform _transform;
        private int _patrolIndex;

        public AttendantState ObservedState => (AttendantState)_state.Value;

        public AttendantIntent ObservedIntent => (AttendantIntent)_intent.Value;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _transform = transform;
        }

        public override void OnNetworkSpawn()
        {
            // Clients render a replicated puppet; only the server pathfinds.
            if (!IsServer)
            {
                _agent.enabled = false;
            }
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            SessionDirectorNetBehaviour net = SessionDirectorNetBehaviour.Instance;
            if (net == null || net.Director == null)
            {
                return;
            }

            _state.Value = (byte)net.Director.AttendantState;
            _intent.Value = (byte)net.Director.AttendantIntent;

            Drive(net.Director.AttendantIntent, net.Director.AttendantTargetRoom.Value);
        }

        /// <summary>Whether the agent has reached its current destination. Fed back into the state machine.</summary>
        public bool HasReachedTarget =>
            _agent.enabled && !_agent.pathPending && _agent.remainingDistance <= _arrivalToleranceMeters;

        private void Drive(AttendantIntent intent, int targetRoomNumber)
        {
            switch (intent)
            {
                case AttendantIntent.Idle:
                    _agent.isStopped = true;
                    break;

                case AttendantIntent.Patrol:
                    Patrol();
                    break;

                case AttendantIntent.MoveToTarget:
                    MoveTo(InteriorAnchorFor(targetRoomNumber));
                    break;

                case AttendantIntent.HoldAtDoor:
                    // The room is still being worked. It comes as far as the threshold and stops.
                    // Standing here, audible, is the whole horror beat.
                    MoveTo(DoorAnchorFor(targetRoomNumber));
                    break;

                case AttendantIntent.Escort:
                    MoveTo(InteriorAnchorFor(targetRoomNumber));
                    break;

                case AttendantIntent.Withdraw:
                    MoveTo(_patrolRoute.Length > 0 ? _patrolRoute[0] : null);
                    break;
            }
        }

        private void Patrol()
        {
            if (_patrolRoute.Length == 0)
            {
                _agent.isStopped = true;
                return;
            }

            Transform waypoint = _patrolRoute[_patrolIndex];
            MoveTo(waypoint);

            if (waypoint != null &&
                Vector3.Distance(_transform.position, waypoint.position) <= _arrivalToleranceMeters)
            {
                _patrolIndex = (_patrolIndex + 1) % _patrolRoute.Length;
            }
        }

        private void MoveTo(Transform target)
        {
            if (target == null)
            {
                _agent.isStopped = true;
                return;
            }

            _agent.isStopped = false;
            _agent.SetDestination(target.position);
        }

        private Transform DoorAnchorFor(int roomNumber)
        {
            for (int i = 0; i < _roomAnchors.Length; i++)
            {
                if (_roomAnchors[i].RoomNumber == roomNumber)
                {
                    return _roomAnchors[i].DoorAnchor;
                }
            }

            return null;
        }

        private Transform InteriorAnchorFor(int roomNumber)
        {
            for (int i = 0; i < _roomAnchors.Length; i++)
            {
                if (_roomAnchors[i].RoomNumber != roomNumber)
                {
                    continue;
                }

                // Fall back to the door anchor rather than failing to move — better it stands at
                // the threshold than freezes mid-corridor.
                return _roomAnchors[i].InteriorAnchor != null
                    ? _roomAnchors[i].InteriorAnchor
                    : _roomAnchors[i].DoorAnchor;
            }

            return null;
        }
    }
}
