using System;
using System.Collections.Generic;
using NUnit.Framework;
using Session.Core.Attendant;
using Session.Core.Identity;
using Session.Core.Protocol;

namespace Session.Tests.Core.Protocol
{
    public sealed class ProtocolMonitorTests
    {
        private static readonly RoomId Nine = new RoomId(9);
        private static readonly RoomId Seventeen = new RoomId(17);
        private static readonly PlayerId Alice = new PlayerId(0);
        private static readonly PlayerId Bren = new PlayerId(1);

        private sealed class FakeProgress : IRoomProgressSource
        {
            private readonly HashSet<int> _complete = new HashSet<int>();
            private readonly Dictionary<int, float> _allowances = new Dictionary<int, float>();

            public void MarkComplete(RoomId room) => _complete.Add(room.Value);

            public void SetAllowance(RoomId room, float seconds) => _allowances[room.Value] = seconds;

            public bool IsComplete(RoomId room) => _complete.Contains(room.Value);

            public float TimeAllowanceSeconds(RoomId room)
                => _allowances.TryGetValue(room.Value, out float seconds) ? seconds : 0f;
        }

        private static ProtocolViolation[] Drain(ProtocolMonitor monitor)
        {
            Span<ProtocolViolation> buffer = stackalloc ProtocolViolation[16];
            int count = monitor.Drain(buffer);

            var result = new ProtocolViolation[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = buffer[i];
            }

            return result;
        }

        [Test]
        public void LeavingAnUnfinishedRoomIsAViolation()
        {
            var progress = new FakeProgress();
            var monitor = new ProtocolMonitor(progress);

            monitor.PlayerEnteredRoom(Alice, Nine, 0f);
            monitor.PlayerLeftRoom(Alice, Nine, 10f);

            ProtocolViolation[] violations = Drain(monitor);

            Assert.AreEqual(1, violations.Length);
            Assert.AreEqual(ViolationKind.LeftRoomUnfinished, violations[0].Kind);
            Assert.AreEqual(Alice, violations[0].Player);
            Assert.AreEqual(Nine, violations[0].Room);
        }

        [Test]
        public void LeavingAFinishedRoomIsFine()
        {
            var progress = new FakeProgress();
            var monitor = new ProtocolMonitor(progress);

            monitor.PlayerEnteredRoom(Alice, Nine, 0f);
            progress.MarkComplete(Nine);
            monitor.PlayerLeftRoom(Alice, Nine, 10f);

            Assert.AreEqual(0, Drain(monitor).Length);
        }

        [Test]
        public void WalkingBackIntoAFinishedRoomIsAViolation()
        {
            var progress = new FakeProgress();
            progress.MarkComplete(Nine);
            var monitor = new ProtocolMonitor(progress);

            monitor.PlayerEnteredRoom(Alice, Nine, 5f);

            ProtocolViolation[] violations = Drain(monitor);

            Assert.AreEqual(1, violations.Length);
            Assert.AreEqual(ViolationKind.BacktrackedIntoCompletedRoom, violations[0].Kind);
        }

        [Test]
        public void ForcingADoorIsAViolation()
        {
            var monitor = new ProtocolMonitor(new FakeProgress());

            monitor.DoorForced(Bren, Seventeen, 3f);

            ProtocolViolation[] violations = Drain(monitor);

            Assert.AreEqual(1, violations.Length);
            Assert.AreEqual(ViolationKind.ForcedDoor, violations[0].Kind);
            Assert.AreEqual(Bren, violations[0].Player);
        }

        [Test]
        public void OverrunningTheAllowanceRaisesExactlyOneViolation()
        {
            // Once per stay, not once per frame — otherwise a slow group banks enough suspicion to
            // pin the Attendant on them permanently.
            var progress = new FakeProgress();
            progress.SetAllowance(Nine, 60f);
            var monitor = new ProtocolMonitor(progress);

            monitor.PlayerEnteredRoom(Alice, Nine, 0f);

            for (int i = 0; i < 200; i++)
            {
                monitor.Tick(i * 1f);
            }

            ProtocolViolation[] violations = Drain(monitor);

            Assert.AreEqual(1, violations.Length);
            Assert.AreEqual(ViolationKind.TimeAllowanceExceeded, violations[0].Kind);
            Assert.AreEqual(Nine, violations[0].Room);
        }

        [Test]
        public void AllowanceDoesNotRunOutInAFinishedRoom()
        {
            var progress = new FakeProgress();
            progress.SetAllowance(Nine, 10f);
            progress.MarkComplete(Nine);
            var monitor = new ProtocolMonitor(progress);

            // Entering a completed room is its own violation; drain it so the assert is unambiguous.
            monitor.PlayerEnteredRoom(Alice, Nine, 0f);
            Drain(monitor);

            for (int i = 0; i < 100; i++)
            {
                monitor.Tick(i * 1f);
            }

            Assert.AreEqual(0, Drain(monitor).Length);
        }

        [Test]
        public void AllowanceClockRestartsInTheNextRoom()
        {
            var progress = new FakeProgress();
            progress.SetAllowance(Nine, 30f);
            progress.SetAllowance(Seventeen, 30f);
            var monitor = new ProtocolMonitor(progress);

            monitor.PlayerEnteredRoom(Alice, Nine, 0f);
            monitor.Tick(40f); // overrun in Room 9
            monitor.PlayerLeftRoom(Alice, Nine, 41f);
            monitor.PlayerEnteredRoom(Alice, Seventeen, 42f);
            monitor.Tick(50f); // only 8s into Room 17

            ProtocolViolation[] violations = Drain(monitor);

            foreach (ProtocolViolation violation in violations)
            {
                Assert.IsFalse(
                    violation.Kind == ViolationKind.TimeAllowanceExceeded && violation.Room == Seventeen,
                    "Room 17's allowance was judged against Room 9's entry time.");
            }
        }

        [Test]
        public void ZeroAllowanceMeansUnlimited()
        {
            // Rooms authored without a time pressure beat should never trip the clock.
            var monitor = new ProtocolMonitor(new FakeProgress());

            monitor.PlayerEnteredRoom(Alice, Nine, 0f);
            monitor.Tick(100000f);

            Assert.AreEqual(0, Drain(monitor).Length);
        }

        [Test]
        public void ViolationsDrainInOrderAndOnlyOnce()
        {
            var monitor = new ProtocolMonitor(new FakeProgress());

            monitor.PlayerEnteredRoom(Alice, Nine, 0f);
            monitor.PlayerLeftRoom(Alice, Nine, 1f);
            monitor.DoorForced(Alice, Seventeen, 2f);

            ProtocolViolation[] first = Drain(monitor);
            Assert.AreEqual(2, first.Length);
            Assert.AreEqual(ViolationKind.LeftRoomUnfinished, first[0].Kind);
            Assert.AreEqual(ViolationKind.ForcedDoor, first[1].Kind);

            Assert.AreEqual(0, Drain(monitor).Length, "Violations were delivered twice.");
        }

        [Test]
        public void PartialDrainLeavesTheRemainder()
        {
            var monitor = new ProtocolMonitor(new FakeProgress());

            monitor.DoorForced(Alice, Nine, 0f);
            monitor.DoorForced(Alice, Nine, 1f);
            monitor.DoorForced(Alice, Nine, 2f);

            Span<ProtocolViolation> small = stackalloc ProtocolViolation[2];
            Assert.AreEqual(2, monitor.Drain(small));
            Assert.AreEqual(1, monitor.PendingCount);
            Assert.AreEqual(1, Drain(monitor).Length);
        }

        [Test]
        public void BufferOverflowIsCountedNotThrown()
        {
            var monitor = new ProtocolMonitor(new FakeProgress(), maxPlayers: 4, violationCapacity: 4);

            for (int i = 0; i < 10; i++)
            {
                monitor.DoorForced(Alice, Nine, i);
            }

            Assert.AreEqual(4, monitor.PendingCount);
            Assert.AreEqual(6, monitor.DroppedViolations);
        }

        [Test]
        public void RingBufferSurvivesWrapAround()
        {
            var monitor = new ProtocolMonitor(new FakeProgress(), maxPlayers: 4, violationCapacity: 4);

            for (int cycle = 0; cycle < 10; cycle++)
            {
                monitor.DoorForced(Alice, Nine, cycle);
                monitor.DoorForced(Bren, Seventeen, cycle);

                ProtocolViolation[] drained = Drain(monitor);

                Assert.AreEqual(2, drained.Length, "Cycle {0}", cycle);
                Assert.AreEqual(Alice, drained[0].Player);
                Assert.AreEqual(Bren, drained[1].Player);
            }

            Assert.AreEqual(0, monitor.DroppedViolations);
        }

        [Test]
        public void PlayerOutsideTheConfiguredSlotRangeIsRejected()
        {
            var monitor = new ProtocolMonitor(new FakeProgress(), maxPlayers: 4);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => monitor.PlayerEnteredRoom(new PlayerId(9), Nine, 0f));
        }

        [Test]
        public void DisconnectStopsTheAllowanceClock()
        {
            var progress = new FakeProgress();
            progress.SetAllowance(Nine, 10f);
            var monitor = new ProtocolMonitor(progress);

            monitor.PlayerEnteredRoom(Alice, Nine, 0f);
            monitor.PlayerDisconnected(Alice);
            monitor.Tick(500f);

            Assert.AreEqual(0, Drain(monitor).Length);
        }
    }
}
