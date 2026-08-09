using NUnit.Framework;
using Session.Core.Attendant;
using Session.Core.Identity;

namespace Session.Tests.Core.Attendant
{
    public sealed class AttendantMachineTests
    {
        private static readonly RoomId Room = new RoomId(9);
        private static readonly PlayerId Player = new PlayerId(1);

        private static AttendantMachine Fresh() => new AttendantMachine(DefaultAttendantProfile.Instance);

        private static AttendantContext Idle => new AttendantContext(RoomId.None, false, false, false);

        private static AttendantContext AtTarget(bool roomBeingWorked = false)
            => new AttendantContext(Room, roomBeingWorked, hasReachedTarget: true, offenderReturnedToSession: false);

        private static void Report(AttendantMachine machine, ViolationKind kind, float time = 0f)
        {
            machine.Report(new ProtocolViolation(kind, Player, Room, time));
        }

        /// <summary>
        /// Walk the machine up to Approaching the way the game would: one violation per rung,
        /// one transition per tick. Leaves suspicion just under 3, below the enforce threshold.
        /// </summary>
        private static void DriveToApproaching(AttendantMachine machine)
        {
            Report(machine, ViolationKind.LeftRoomUnfinished);
            machine.Tick(Idle, 0.016f);
            Report(machine, ViolationKind.BacktrackedIntoCompletedRoom);
            machine.Tick(Idle, 0.016f);

            Assert.AreEqual(AttendantState.Approaching, machine.State, "Fixture failed to reach Approaching.");
        }

        /// <summary>Approaching, then over the enforce threshold with the target reached.</summary>
        private static void DriveToEnforcing(AttendantMachine machine)
        {
            DriveToApproaching(machine);
            Report(machine, ViolationKind.ForcedDoor);
            machine.Tick(AtTarget(), 0.016f);

            Assert.AreEqual(AttendantState.Enforcing, machine.State, "Fixture failed to reach Enforcing.");
        }

        [Test]
        public void StartsDormantAndIdle()
        {
            AttendantMachine machine = Fresh();

            Assert.AreEqual(AttendantState.Dormant, machine.State);
            Assert.AreEqual(AttendantIntent.Idle, machine.Intent);
            Assert.AreEqual(0f, machine.Suspicion);
        }

        [Test]
        public void NoiseAloneNeverWakesIt()
        {
            // There is no input other than a protocol violation that raises suspicion. Ticking
            // forever with players screaming in the corridor changes nothing.
            AttendantMachine machine = Fresh();

            for (int i = 0; i < 600; i++)
            {
                machine.Tick(Idle, 0.1f);
            }

            Assert.AreEqual(AttendantState.Dormant, machine.State);
        }

        [Test]
        public void LeavingARoomUnfinishedEscalatesToObserving()
        {
            AttendantMachine machine = Fresh();

            Report(machine, ViolationKind.LeftRoomUnfinished);
            machine.Tick(Idle, 0.016f);

            Assert.AreEqual(AttendantState.Observing, machine.State);
            Assert.AreEqual(AttendantIntent.Patrol, machine.Intent);
        }

        [Test]
        public void EscalationLadderIsDormantObservingApproachingEnforcing()
        {
            AttendantMachine machine = Fresh();

            // One rung per tick, and each rung needs its own threshold crossed. The machine never
            // skips a state, so the player always gets to hear it coming.
            Report(machine, ViolationKind.LeftRoomUnfinished); // 2 -> past observe(1)
            machine.Tick(Idle, 0.016f);
            Assert.AreEqual(AttendantState.Observing, machine.State);
            Assert.AreEqual(AttendantIntent.Patrol, machine.Intent);

            Report(machine, ViolationKind.BacktrackedIntoCompletedRoom); // ~3 -> past approach(2)
            machine.Tick(Idle, 0.016f);
            Assert.AreEqual(AttendantState.Approaching, machine.State);
            Assert.AreEqual(AttendantIntent.MoveToTarget, machine.Intent);

            Report(machine, ViolationKind.ForcedDoor); // ~5 -> past enforce(3)
            machine.Tick(AtTarget(), 0.016f);
            Assert.AreEqual(AttendantState.Enforcing, machine.State);
            Assert.AreEqual(AttendantIntent.Escort, machine.Intent);
        }

        [Test]
        public void ItCannotEnterARoomStillBeingWorked()
        {
            // The promise the whole game rests on. Stay and solve, and you are safe.
            AttendantMachine machine = Fresh();

            DriveToApproaching(machine);
            Report(machine, ViolationKind.ForcedDoor); // well past the enforce threshold

            for (int i = 0; i < 300; i++)
            {
                machine.Tick(AtTarget(roomBeingWorked: true), 0.016f);
                Assert.AreNotEqual(AttendantState.Enforcing, machine.State,
                    "Entered a room that was still being worked at tick {0}.", i);
            }

            Assert.AreEqual(AttendantIntent.HoldAtDoor, machine.Intent);
            Assert.IsTrue(machine.IsBlockedByActiveSession);
        }

        [Test]
        public void ReturningToSessionCallsItOff()
        {
            AttendantMachine machine = Fresh();

            DriveToApproaching(machine);

            machine.Tick(new AttendantContext(Room, false, false, offenderReturnedToSession: true), 0.016f);

            Assert.AreEqual(AttendantState.Withdrawing, machine.State);
            Assert.AreEqual(AttendantIntent.Withdraw, machine.Intent);
        }

        [Test]
        public void EnforcingResetsSuspicionAndWithdraws()
        {
            AttendantMachine machine = Fresh();

            DriveToEnforcing(machine);

            machine.Tick(AtTarget(), DefaultAttendantProfile.Instance.EnforceDurationSeconds);

            Assert.AreEqual(AttendantState.Withdrawing, machine.State);
            Assert.AreEqual(0f, machine.Suspicion);
        }

        [Test]
        public void SuspicionDoesNotDecayMidEscort()
        {
            AttendantMachine machine = Fresh();

            DriveToEnforcing(machine);

            float before = machine.Suspicion;
            machine.Tick(AtTarget(), 1f);

            Assert.AreEqual(before, machine.Suspicion);
        }

        [Test]
        public void GoodBehaviourBleedsSuspicionAndReturnsItToDormant()
        {
            AttendantMachine machine = Fresh();

            Report(machine, ViolationKind.BacktrackedIntoCompletedRoom); // weight 1, exactly observe threshold
            machine.Tick(Idle, 0.016f);
            Assert.AreEqual(AttendantState.Observing, machine.State);

            // Decay 0.1/s, dwell 8s. Long enough to fall below threshold and time out the dwell.
            for (int i = 0; i < 1200; i++)
            {
                machine.Tick(Idle, 0.1f);
            }

            Assert.AreEqual(AttendantState.Dormant, machine.State);
            Assert.AreEqual(0f, machine.Suspicion);
            Assert.IsTrue(machine.Offender.IsNone);
        }

        [Test]
        public void SuspicionIsCapped()
        {
            AttendantMachine machine = Fresh();

            for (int i = 0; i < 50; i++)
            {
                Report(machine, ViolationKind.ForcedDoor);
            }

            Assert.AreEqual(DefaultAttendantProfile.Instance.SuspicionCap, machine.Suspicion);
        }

        [Test]
        public void AFreshViolationDuringWithdrawalPullsItBack()
        {
            AttendantMachine machine = Fresh();

            DriveToApproaching(machine);
            machine.Tick(new AttendantContext(Room, false, false, offenderReturnedToSession: true), 0.016f);
            Assert.AreEqual(AttendantState.Withdrawing, machine.State);

            Report(machine, ViolationKind.ForcedDoor);
            machine.Tick(Idle, 0.016f);

            Assert.AreEqual(AttendantState.Approaching, machine.State);
        }

        [Test]
        public void MachineIsDeterministic()
        {
            // Same violations, same ticks, same outcome — every time. Players have to be able to
            // learn this.
            AttendantMachine a = Fresh();
            AttendantMachine b = Fresh();

            for (int i = 0; i < 500; i++)
            {
                if (i % 97 == 0)
                {
                    Report(a, ViolationKind.LeftRoomUnfinished, i * 0.05f);
                    Report(b, ViolationKind.LeftRoomUnfinished, i * 0.05f);
                }

                bool worked = i % 13 == 0;
                a.Tick(AtTarget(worked), 0.05f);
                b.Tick(AtTarget(worked), 0.05f);

                Assert.AreEqual(a.State, b.State, "Diverged at tick {0}", i);
                Assert.AreEqual(a.Suspicion, b.Suspicion, "Suspicion diverged at tick {0}", i);
            }
        }

        [Test]
        public void UnknownViolationKindIsIgnored()
        {
            AttendantMachine machine = Fresh();

            Report(machine, ViolationKind.None);
            machine.Tick(Idle, 0.016f);

            Assert.AreEqual(AttendantState.Dormant, machine.State);
            Assert.IsTrue(machine.Offender.IsNone);
        }
    }
}
