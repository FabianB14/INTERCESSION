namespace Session.Core.Attendant
{
    /// <summary>
    /// Every threshold and timing the Attendant uses. Implemented by AttendantProfileSO in
    /// Session.Runtime so all of it is tunable without a recompile.
    ///
    /// Nothing here controls whether the Attendant may enter a room being worked. That is not a
    /// tuning value — it is the promise the game makes to the player, and it is hardcoded in
    /// <see cref="AttendantMachine"/> on purpose. Stay and solve, and you are safe.
    /// </summary>
    public interface IAttendantProfile
    {
        /// <summary>Suspicion at which it stops being Dormant and starts watching.</summary>
        float ObserveThreshold { get; }

        /// <summary>Suspicion at which it starts walking toward the offender.</summary>
        float ApproachThreshold { get; }

        /// <summary>Suspicion at which, having reached them, it escorts.</summary>
        float EnforceThreshold { get; }

        /// <summary>Suspicion bled off per second while not enforcing. Good behaviour is forgiven, slowly.</summary>
        float SuspicionDecayPerSecond { get; }

        /// <summary>Ceiling, so a burst of violations cannot bank arbitrary escalation.</summary>
        float SuspicionCap { get; }

        /// <summary>How long it lingers in Observing after suspicion falls back below the observe threshold.</summary>
        float ObserveDwellSeconds { get; }

        /// <summary>How long an escort takes once it has hold of someone.</summary>
        float EnforceDurationSeconds { get; }

        /// <summary>How long it takes to walk back out of play.</summary>
        float WithdrawSeconds { get; }

        /// <summary>Suspicion added by one violation of this kind.</summary>
        float WeightFor(ViolationKind kind);
    }

    /// <summary>
    /// Defaults for tests and for running before an AttendantProfileSO asset exists. These are
    /// deliberately plain numbers with no feel tuned into them — pacing is a design call, not a
    /// code one, and belongs in the SO.
    /// </summary>
    public sealed class DefaultAttendantProfile : IAttendantProfile
    {
        public static readonly DefaultAttendantProfile Instance = new DefaultAttendantProfile();

        public float ObserveThreshold => 1f;

        public float ApproachThreshold => 2f;

        public float EnforceThreshold => 3f;

        public float SuspicionDecayPerSecond => 0.1f;

        public float SuspicionCap => 6f;

        public float ObserveDwellSeconds => 8f;

        public float EnforceDurationSeconds => 6f;

        public float WithdrawSeconds => 4f;

        public float WeightFor(ViolationKind kind)
        {
            switch (kind)
            {
                case ViolationKind.LeftRoomUnfinished:
                    return 2f;
                case ViolationKind.ForcedDoor:
                    return 2f;
                case ViolationKind.BacktrackedIntoCompletedRoom:
                    return 1f;
                case ViolationKind.TimeAllowanceExceeded:
                    return 1f;
                default:
                    return 0f;
            }
        }
    }
}
