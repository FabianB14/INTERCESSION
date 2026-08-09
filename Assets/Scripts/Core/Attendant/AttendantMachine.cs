using System;
using Session.Core.Identity;

namespace Session.Core.Attendant
{
    public enum AttendantState
    {
        Dormant = 0,
        Observing = 1,
        Approaching = 2,
        Enforcing = 3,
        Withdrawing = 4
    }

    /// <summary>
    /// What the machine wants the Runtime adapter to do. Core has no navigation and no transforms —
    /// it produces an intent, and a NavMeshAgent in Session.Runtime walks it there. It always walks.
    /// There is no intent that teleports, because there is no situation in which it should.
    /// </summary>
    public enum AttendantIntent
    {
        Idle = 0,

        /// <summary>Walk the corridor loop. Audible, unhurried.</summary>
        Patrol = 1,

        /// <summary>Walk toward <see cref="AttendantMachine.TargetRoom"/>.</summary>
        MoveToTarget = 2,

        /// <summary>Standing at the door of a room it may not enter. This is the shape of the whole game.</summary>
        HoldAtDoor = 3,

        /// <summary>Has the offender. Walking them back.</summary>
        Escort = 4,

        /// <summary>Leaving.</summary>
        Withdraw = 5
    }

    /// <summary>
    /// Per-tick facts the machine needs from the world. Passed by <c>in</c> — this runs every frame
    /// on the server and allocates nothing.
    /// </summary>
    public readonly struct AttendantContext
    {
        /// <summary>Where the offender is, or <see cref="RoomId.None"/> if unknown.</summary>
        public readonly RoomId OffenderRoom;

        /// <summary>
        /// Players are inside the target room and its puzzles are unfinished. While true the
        /// Attendant cannot enter, full stop.
        /// </summary>
        public readonly bool TargetRoomIsBeingWorked;

        /// <summary>Navigation reports the Attendant is at the target.</summary>
        public readonly bool HasReachedTarget;

        /// <summary>The offender is back in an unfinished room, working. Compliance.</summary>
        public readonly bool OffenderReturnedToSession;

        public AttendantContext(
            RoomId offenderRoom,
            bool targetRoomIsBeingWorked,
            bool hasReachedTarget,
            bool offenderReturnedToSession)
        {
            OffenderRoom = offenderRoom;
            TargetRoomIsBeingWorked = targetRoomIsBeingWorked;
            HasReachedTarget = hasReachedTarget;
            OffenderReturnedToSession = offenderReturnedToSession;
        }
    }

    /// <summary>
    /// The Attendant's brain. A deterministic finite state machine — same inputs, same transitions,
    /// every time. No randomness anywhere in this file, by design: a player on their fourth run has
    /// to be able to explain this to a new player in thirty seconds.
    ///
    /// The ladder is: violations raise suspicion, suspicion crosses thresholds, thresholds move
    /// state. Good behaviour bleeds suspicion off. Nothing else escalates it — not proximity, not
    /// noise, not the flashlight.
    /// </summary>
    public sealed class AttendantMachine
    {
        private readonly IAttendantProfile _profile;

        public AttendantMachine(IAttendantProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Reset();
        }

        public AttendantState State { get; private set; }

        public AttendantIntent Intent { get; private set; }

        public float Suspicion { get; private set; }

        /// <summary>Seconds spent in the current state.</summary>
        public float StateTime { get; private set; }

        public PlayerId Offender { get; private set; }

        public RoomId TargetRoom { get; private set; }

        /// <summary>True while it is standing outside a room it is not permitted to enter.</summary>
        public bool IsBlockedByActiveSession { get; private set; }

        public void Reset()
        {
            State = AttendantState.Dormant;
            Intent = AttendantIntent.Idle;
            Suspicion = 0f;
            StateTime = 0f;
            Offender = PlayerId.None;
            TargetRoom = RoomId.None;
            IsBlockedByActiveSession = false;
        }

        /// <summary>
        /// Record a protocol violation. This is the only thing that raises suspicion.
        /// Safe to call during Enforcing; it just tops up the pool.
        /// </summary>
        public void Report(in ProtocolViolation violation)
        {
            float weight = _profile.WeightFor(violation.Kind);
            if (weight <= 0f)
            {
                return;
            }

            Suspicion += weight;
            if (Suspicion > _profile.SuspicionCap)
            {
                Suspicion = _profile.SuspicionCap;
            }

            // The most recent violator is the one it walks toward. It handles one person at a time,
            // like staff would.
            Offender = violation.Player;
            TargetRoom = violation.Room;
        }

        public void Tick(in AttendantContext context, float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            StateTime += deltaSeconds;

            if (!context.OffenderRoom.IsNone)
            {
                TargetRoom = context.OffenderRoom;
            }

            IsBlockedByActiveSession = false;

            switch (State)
            {
                case AttendantState.Dormant:
                    TickDormant();
                    break;
                case AttendantState.Observing:
                    TickObserving();
                    break;
                case AttendantState.Approaching:
                    TickApproaching(in context);
                    break;
                case AttendantState.Enforcing:
                    TickEnforcing();
                    break;
                case AttendantState.Withdrawing:
                    TickWithdrawing();
                    break;
            }

            // Decay last, so a violation reported this frame is compared against its full weight
            // before any of it bleeds off. Decaying first makes a threshold set exactly equal to a
            // violation's weight unreachable, which is a maddening way to lose an afternoon.
            // Suspended mid-escort: that outcome is already decided.
            if (State != AttendantState.Enforcing)
            {
                Suspicion -= _profile.SuspicionDecayPerSecond * deltaSeconds;
                if (Suspicion < 0f)
                {
                    Suspicion = 0f;
                }
            }
        }

        private void TickDormant()
        {
            Intent = AttendantIntent.Idle;

            if (Suspicion >= _profile.ObserveThreshold)
            {
                Enter(AttendantState.Observing);
            }
        }

        private void TickObserving()
        {
            // Present in the corridor, audible, not yet coming for anyone.
            Intent = AttendantIntent.Patrol;

            if (Suspicion >= _profile.ApproachThreshold)
            {
                Enter(AttendantState.Approaching);
                return;
            }

            if (Suspicion < _profile.ObserveThreshold && StateTime >= _profile.ObserveDwellSeconds)
            {
                Enter(AttendantState.Withdrawing);
            }
        }

        private void TickApproaching(in AttendantContext context)
        {
            // They went back to work. Nothing further is owed.
            if (context.OffenderReturnedToSession)
            {
                Enter(AttendantState.Withdrawing);
                return;
            }

            if (context.TargetRoomIsBeingWorked)
            {
                // The hard promise: a room still being worked cannot be entered. It waits at the
                // threshold for as long as that stays true.
                Intent = AttendantIntent.HoldAtDoor;
                IsBlockedByActiveSession = true;
                return;
            }

            if (Suspicion < _profile.ObserveThreshold)
            {
                Enter(AttendantState.Withdrawing);
                return;
            }

            Intent = AttendantIntent.MoveToTarget;

            if (context.HasReachedTarget && Suspicion >= _profile.EnforceThreshold)
            {
                Enter(AttendantState.Enforcing);
            }
        }

        private void TickEnforcing()
        {
            Intent = AttendantIntent.Escort;

            if (StateTime < _profile.EnforceDurationSeconds)
            {
                return;
            }

            // Escort complete. The ledger is settled — this is why suspicion resets rather than decays.
            Suspicion = 0f;
            Enter(AttendantState.Withdrawing);
        }

        private void TickWithdrawing()
        {
            Intent = AttendantIntent.Withdraw;

            // A fresh violation during withdrawal pulls it straight back around.
            if (Suspicion >= _profile.ApproachThreshold)
            {
                Enter(AttendantState.Approaching);
                return;
            }

            if (StateTime >= _profile.WithdrawSeconds)
            {
                Offender = PlayerId.None;
                Enter(AttendantState.Dormant);
            }
        }

        private void Enter(AttendantState next)
        {
            State = next;
            StateTime = 0f;

            // Set the intent on entry rather than waiting for the next tick's handler. Otherwise
            // the frame a transition happens reports the new state with the old state's intent,
            // and the animation layer plays a beat of the wrong thing.
            switch (next)
            {
                case AttendantState.Dormant:
                    Intent = AttendantIntent.Idle;
                    break;
                case AttendantState.Observing:
                    Intent = AttendantIntent.Patrol;
                    break;
                case AttendantState.Approaching:
                    Intent = AttendantIntent.MoveToTarget;
                    break;
                case AttendantState.Enforcing:
                    Intent = AttendantIntent.Escort;
                    break;
                case AttendantState.Withdrawing:
                    Intent = AttendantIntent.Withdraw;
                    break;
            }
        }
    }
}
