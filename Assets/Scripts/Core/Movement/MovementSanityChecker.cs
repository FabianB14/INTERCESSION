using Session.Core.Identity;
using Session.Core.Spatial;

namespace Session.Core.Movement
{
    /// <summary>
    /// Tuning for server-side movement validation. Implemented by MovementRulesSO in Session.Runtime.
    /// </summary>
    public interface IMovementRules
    {
        /// <summary>Fastest a player can move on the horizontal plane under their own power.</summary>
        float MaxSpeedMetersPerSecond { get; }

        /// <summary>Multiplier applied to <see cref="MaxSpeedMetersPerSecond"/> while sprinting.</summary>
        float SprintMultiplier { get; }

        /// <summary>
        /// Extra headroom on every check, as a fraction. Absorbs the difference between the client's
        /// frame time and the server's without accusing an honest player of cheating.
        /// </summary>
        float ToleranceFraction { get; }

        /// <summary>
        /// Seconds of unused movement the checker will bank. A client whose packet stalls for
        /// 200ms then arrives with 200ms of legitimate motion must not be clamped for it.
        /// </summary>
        float BudgetGraceSeconds { get; }

        /// <summary>
        /// Beyond this much unexplained displacement in one update, the move is not lag — it is a
        /// teleport. Snap back rather than clamp.
        /// </summary>
        float TeleportThresholdMeters { get; }

        /// <summary>Vertical displacement allowed per second, covering falls and stairs.</summary>
        float MaxVerticalSpeedMetersPerSecond { get; }
    }

    public sealed class DefaultMovementRules : IMovementRules
    {
        public static readonly DefaultMovementRules Instance = new DefaultMovementRules();

        public float MaxSpeedMetersPerSecond => 4.5f;

        public float SprintMultiplier => 1.6f;

        public float ToleranceFraction => 0.15f;

        public float BudgetGraceSeconds => 0.35f;

        public float TeleportThresholdMeters => 8f;

        public float MaxVerticalSpeedMetersPerSecond => 25f;
    }

    public enum MovementOutcome
    {
        /// <summary>Within budget. The client's position stands.</summary>
        Accepted = 0,

        /// <summary>Over budget but plausibly lag. Clamped to the furthest reachable point.</summary>
        Clamped = 1,

        /// <summary>Beyond any explanation. The player is snapped back to their last accepted position.</summary>
        Rejected = 2
    }

    public readonly struct MovementVerdict
    {
        public readonly MovementOutcome Outcome;

        /// <summary>The position the server considers true. Always send this back on a non-Accepted verdict.</summary>
        public readonly Vec3 AcceptedPosition;

        /// <summary>How far past the allowance the client claimed to travel. Zero when accepted.</summary>
        public readonly float ExcessMeters;

        public MovementVerdict(MovementOutcome outcome, Vec3 acceptedPosition, float excessMeters)
        {
            Outcome = outcome;
            AcceptedPosition = acceptedPosition;
            ExcessMeters = excessMeters;
        }

        public bool RequiresCorrection => Outcome != MovementOutcome.Accepted;
    }

    /// <summary>
    /// Server-side sanity check on client-authoritative movement.
    ///
    /// Golden rule 5 gives clients authority over their own position and look direction, which
    /// keeps movement responsive over a Steam relay. This is the counterweight: the server tracks
    /// what each player could plausibly have done and clamps anything past it.
    ///
    /// Deliberately NOT wired to the Attendant. Moving too fast is a networking event, not a
    /// protocol violation — the Attendant escalates on leaving rooms unfinished, not on speed.
    /// Feeding cheat corrections into its suspicion pool would make it unlearnable, which is the
    /// one thing it must never be.
    ///
    /// Fixed-size arrays indexed by player slot. No allocation after construction.
    /// </summary>
    public sealed class MovementSanityChecker
    {
        private struct Tracked
        {
            public Vec3 Position;
            public float BankedSeconds;
            public bool Initialised;
        }

        private readonly IMovementRules _rules;
        private readonly Tracked[] _players;

        public MovementSanityChecker(IMovementRules rules, int maxPlayers = 4)
        {
            _rules = rules ?? throw new System.ArgumentNullException(nameof(rules));
            _players = new Tracked[maxPlayers];
        }

        /// <summary>Place a player without validation — spawn, room transition, or an accepted correction.</summary>
        public void Teleport(PlayerId player, Vec3 position)
        {
            int slot = SlotOf(player);
            _players[slot].Position = position;
            _players[slot].BankedSeconds = 0f;
            _players[slot].Initialised = true;
        }

        public Vec3 PositionOf(PlayerId player) => _players[SlotOf(player)].Position;

        public void Forget(PlayerId player)
        {
            _players[SlotOf(player)] = default;
        }

        /// <summary>
        /// Validate a position update the client claims happened over <paramref name="deltaSeconds"/>.
        /// </summary>
        public MovementVerdict Validate(PlayerId player, Vec3 reported, float deltaSeconds, bool sprinting)
        {
            int slot = SlotOf(player);
            ref Tracked tracked = ref _players[slot];

            // First report from this player is the source of truth; there is nothing to compare to.
            if (!tracked.Initialised)
            {
                tracked.Position = reported;
                tracked.Initialised = true;
                return new MovementVerdict(MovementOutcome.Accepted, reported, 0f);
            }

            if (deltaSeconds < 0f)
            {
                // A negative interval is malformed. Hold position rather than trust it.
                return new MovementVerdict(MovementOutcome.Rejected, tracked.Position, 0f);
            }

            // Bank unused time so a stalled packet followed by a burst is not punished.
            tracked.BankedSeconds += deltaSeconds;
            if (tracked.BankedSeconds > _rules.BudgetGraceSeconds)
            {
                tracked.BankedSeconds = _rules.BudgetGraceSeconds;
            }

            Vec3 delta = reported - tracked.Position;

            float maxSpeed = _rules.MaxSpeedMetersPerSecond;
            if (sprinting)
            {
                maxSpeed *= _rules.SprintMultiplier;
            }

            float horizontalBudget = maxSpeed * tracked.BankedSeconds * (1f + _rules.ToleranceFraction);
            float verticalBudget =
                _rules.MaxVerticalSpeedMetersPerSecond * tracked.BankedSeconds * (1f + _rules.ToleranceFraction);

            float horizontalTravelled = delta.HorizontalMagnitude;
            float verticalTravelled = delta.Y < 0f ? -delta.Y : delta.Y;

            bool horizontalOk = horizontalTravelled <= horizontalBudget;
            bool verticalOk = verticalTravelled <= verticalBudget;

            if (horizontalOk && verticalOk)
            {
                // Spend only what was used, so standing still does not accrue an infinite reserve.
                float used = maxSpeed > 0f ? horizontalTravelled / maxSpeed : 0f;
                tracked.BankedSeconds -= used;
                if (tracked.BankedSeconds < 0f)
                {
                    tracked.BankedSeconds = 0f;
                }

                tracked.Position = reported;
                return new MovementVerdict(MovementOutcome.Accepted, reported, 0f);
            }

            float excess = horizontalTravelled - horizontalBudget;
            if (excess < 0f)
            {
                excess = 0f;
            }

            float totalTravelled = delta.Magnitude;
            if (totalTravelled > _rules.TeleportThresholdMeters)
            {
                // Not lag. Snap back and let the client reconcile.
                tracked.BankedSeconds = 0f;
                return new MovementVerdict(MovementOutcome.Rejected, tracked.Position, excess);
            }

            // Plausibly lag or a mild desync: allow the furthest point they could have reached.
            Vec3 clamped = Vec3.MoveTowards(tracked.Position, reported, horizontalBudget);
            tracked.Position = clamped;
            tracked.BankedSeconds = 0f;

            return new MovementVerdict(MovementOutcome.Clamped, clamped, excess);
        }

        private int SlotOf(PlayerId player)
        {
            if (player.Value < 0 || player.Value >= _players.Length)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(player), "Player slot " + player.Value + " is outside the configured max players.");
            }

            return player.Value;
        }
    }
}
