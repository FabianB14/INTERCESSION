using Session.Core.Attendant;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// Every Attendant threshold and timing, tunable without a recompile.
    ///
    /// Note what is absent: there is no "can enter an active room" toggle. That rule is hardcoded
    /// in AttendantMachine because it is the promise the game makes to the player, not a difficulty
    /// setting.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Attendant Profile", fileName = "SO_AttendantProfile")]
    public sealed class AttendantProfileSO : ScriptableObject, IAttendantProfile
    {
        [Header("Escalation thresholds (suspicion)")]
        [Tooltip("Stops being Dormant and starts walking the corridor.")]
        [SerializeField, Min(0f)] private float _observeThreshold = 1f;

        [Tooltip("Starts walking toward the offender.")]
        [SerializeField, Min(0f)] private float _approachThreshold = 2f;

        [Tooltip("Having reached them, escorts.")]
        [SerializeField, Min(0f)] private float _enforceThreshold = 3f;

        [Tooltip("Ceiling, so a burst of violations cannot bank arbitrary escalation.")]
        [SerializeField, Min(0f)] private float _suspicionCap = 6f;

        [Header("Decay")]
        [Tooltip("Suspicion bled off per second while not enforcing. Good behaviour is forgiven, slowly.")]
        [SerializeField, Min(0f)] private float _suspicionDecayPerSecond = 0.1f;

        [Header("Timings (seconds)")]
        [Tooltip("How long it lingers in the corridor after suspicion falls back below the observe threshold.")]
        [SerializeField, Min(0f)] private float _observeDwellSeconds = 8f;

        [Tooltip("How long an escort takes once it has hold of someone.")]
        [SerializeField, Min(0f)] private float _enforceDurationSeconds = 6f;

        [Tooltip("How long it takes to walk back out of play.")]
        [SerializeField, Min(0f)] private float _withdrawSeconds = 4f;

        [Header("Violation weights (suspicion added)")]
        [Tooltip("Left a room whose puzzles are not finished. The third principle.")]
        [SerializeField, Min(0f)] private float _leftRoomUnfinishedWeight = 2f;

        [Tooltip("Re-entered a room already completed.")]
        [SerializeField, Min(0f)] private float _backtrackedWeight = 1f;

        [Tooltip("Forced a door rather than answering the honest question.")]
        [SerializeField, Min(0f)] private float _forcedDoorWeight = 2f;

        [Tooltip("Exceeded the room's time allowance.")]
        [SerializeField, Min(0f)] private float _timeAllowanceExceededWeight = 1f;

        public float ObserveThreshold => _observeThreshold;

        public float ApproachThreshold => _approachThreshold;

        public float EnforceThreshold => _enforceThreshold;

        public float SuspicionDecayPerSecond => _suspicionDecayPerSecond;

        public float SuspicionCap => _suspicionCap;

        public float ObserveDwellSeconds => _observeDwellSeconds;

        public float EnforceDurationSeconds => _enforceDurationSeconds;

        public float WithdrawSeconds => _withdrawSeconds;

        public float WeightFor(ViolationKind kind)
        {
            switch (kind)
            {
                case ViolationKind.LeftRoomUnfinished:
                    return _leftRoomUnfinishedWeight;
                case ViolationKind.BacktrackedIntoCompletedRoom:
                    return _backtrackedWeight;
                case ViolationKind.ForcedDoor:
                    return _forcedDoorWeight;
                case ViolationKind.TimeAllowanceExceeded:
                    return _timeAllowanceExceededWeight;
                default:
                    return 0f;
            }
        }

        private void OnValidate()
        {
            // The ladder has to stay a ladder. Out-of-order thresholds would let it skip a rung,
            // and the whole point is that players can hear it coming.
            if (_approachThreshold < _observeThreshold)
            {
                _approachThreshold = _observeThreshold;
            }

            if (_enforceThreshold < _approachThreshold)
            {
                _enforceThreshold = _approachThreshold;
            }

            if (_suspicionCap < _enforceThreshold)
            {
                _suspicionCap = _enforceThreshold;
            }
        }
    }
}
