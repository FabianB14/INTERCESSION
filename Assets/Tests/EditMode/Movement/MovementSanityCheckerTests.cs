using NUnit.Framework;
using Session.Core.Identity;
using Session.Core.Movement;
using Session.Core.Spatial;

namespace Session.Tests.Core.Movement
{
    public sealed class MovementSanityCheckerTests
    {
        private static readonly PlayerId Alice = new PlayerId(0);

        private static MovementSanityChecker Fresh(IMovementRules rules = null)
            => new MovementSanityChecker(rules ?? DefaultMovementRules.Instance);

        [Test]
        public void FirstReportEstablishesPositionWithoutValidation()
        {
            MovementSanityChecker checker = Fresh();

            MovementVerdict verdict = checker.Validate(Alice, new Vec3(100f, 0f, 100f), 0.05f, false);

            Assert.AreEqual(MovementOutcome.Accepted, verdict.Outcome);
        }

        [Test]
        public void WalkingAtNormalSpeedIsAccepted()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            // 4.5 m/s for 50ms is 0.225m. Well inside budget.
            float x = 0f;
            for (int i = 0; i < 200; i++)
            {
                x += 0.225f;
                MovementVerdict verdict = checker.Validate(Alice, new Vec3(x, 0f, 0f), 0.05f, false);
                Assert.AreEqual(MovementOutcome.Accepted, verdict.Outcome, "Rejected at step {0}", i);
            }
        }

        [Test]
        public void SprintingWithinTheMultiplierIsAccepted()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            // 4.5 * 1.6 = 7.2 m/s. 50ms of that is 0.36m.
            MovementVerdict verdict = checker.Validate(Alice, new Vec3(0.36f, 0f, 0f), 0.05f, true);

            Assert.AreEqual(MovementOutcome.Accepted, verdict.Outcome);
        }

        [Test]
        public void SprintSpeedIsRejectedWhenNotSprinting()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            // Same distance as the sprint test, but claiming to walk.
            MovementVerdict verdict = checker.Validate(Alice, new Vec3(0.36f, 0f, 0f), 0.05f, false);

            Assert.AreNotEqual(MovementOutcome.Accepted, verdict.Outcome);
        }

        [Test]
        public void ModestOverspeedIsClampedNotRejected()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            MovementVerdict verdict = checker.Validate(Alice, new Vec3(2f, 0f, 0f), 0.05f, false);

            Assert.AreEqual(MovementOutcome.Clamped, verdict.Outcome);
            Assert.Less(verdict.AcceptedPosition.X, 2f);
            Assert.Greater(verdict.AcceptedPosition.X, 0f);
            Assert.Greater(verdict.ExcessMeters, 0f);
        }

        [Test]
        public void TeleportIsRejectedAndSnapsBack()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            MovementVerdict verdict = checker.Validate(Alice, new Vec3(500f, 0f, 0f), 0.05f, false);

            Assert.AreEqual(MovementOutcome.Rejected, verdict.Outcome);
            Assert.AreEqual(0f, verdict.AcceptedPosition.X);
            Assert.AreEqual(Vec3.Zero, checker.PositionOf(Alice));
        }

        [Test]
        public void BankedTimeAbsorbsAStalledPacket()
        {
            // The scenario this exists for: a packet stalls for 300ms, then one arrives carrying
            // 300ms of entirely legitimate movement. Clamping that is a false positive on an
            // honest player, which is far worse than letting a cheat gain a metre.
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            MovementVerdict verdict = checker.Validate(Alice, new Vec3(4.5f * 0.3f, 0f, 0f), 0.3f, false);

            Assert.AreEqual(MovementOutcome.Accepted, verdict.Outcome);
        }

        [Test]
        public void StandingStillDoesNotBankUnlimitedCredit()
        {
            // Otherwise a cheat stands still for a minute, banks 60 seconds of movement, and
            // crosses the building in one packet.
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            for (int i = 0; i < 200; i++)
            {
                checker.Validate(Alice, Vec3.Zero, 0.05f, false);
            }

            // Grace is 0.35s, so at most ~0.35 * 4.5 * 1.15 metres is available.
            MovementVerdict verdict = checker.Validate(Alice, new Vec3(20f, 0f, 0f), 0.05f, false);

            Assert.AreNotEqual(MovementOutcome.Accepted, verdict.Outcome);
        }

        [Test]
        public void FallingIsAllowedByTheVerticalBudget()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, new Vec3(0f, 10f, 0f));

            // Falling fast, moving nowhere horizontally.
            MovementVerdict verdict = checker.Validate(Alice, new Vec3(0f, 9f, 0f), 0.1f, false);

            Assert.AreEqual(MovementOutcome.Accepted, verdict.Outcome);
        }

        [Test]
        public void NegativeDeltaIsRejected()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, Vec3.Zero);

            MovementVerdict verdict = checker.Validate(Alice, new Vec3(1f, 0f, 0f), -5f, false);

            Assert.AreEqual(MovementOutcome.Rejected, verdict.Outcome);
        }

        [Test]
        public void ForgetResetsTheTrackedPlayer()
        {
            MovementSanityChecker checker = Fresh();
            checker.Teleport(Alice, new Vec3(50f, 0f, 50f));
            checker.Forget(Alice);

            // Treated as a first report again.
            MovementVerdict verdict = checker.Validate(Alice, new Vec3(999f, 0f, 999f), 0.05f, false);

            Assert.AreEqual(MovementOutcome.Accepted, verdict.Outcome);
        }
    }
}
