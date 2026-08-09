using NUnit.Framework;
using Session.Core.Determinism;
using Session.Core.Identity;

namespace Session.Tests.Core.Determinism
{
    public sealed class Pcg32Tests
    {
        [Test]
        public void SameSeedProducesSameSequence()
        {
            var a = new Pcg32(12345UL);
            var b = new Pcg32(12345UL);

            for (int i = 0; i < 256; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), "Diverged at draw {0}", i);
            }
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            var a = new Pcg32(1UL);
            var b = new Pcg32(2UL);

            bool diverged = false;
            for (int i = 0; i < 16 && !diverged; i++)
            {
                if (a.NextUInt() != b.NextUInt())
                {
                    diverged = true;
                }
            }

            Assert.IsTrue(diverged);
        }

        [Test]
        public void BoundedDrawsStayInRange()
        {
            var rng = new Pcg32(99UL);

            for (int i = 0; i < 100000; i++)
            {
                uint value = rng.NextUInt(7u);
                Assert.Less(value, 7u);
            }
        }

        [Test]
        public void BoundedDrawsAreNotVisiblyBiased()
        {
            // Rejection sampling should keep buckets within a few percent over this many draws.
            // A naive modulo would skew the low buckets.
            var rng = new Pcg32(2024UL);
            const int draws = 200000;
            const uint buckets = 6u;
            var counts = new int[buckets];

            for (int i = 0; i < draws; i++)
            {
                counts[rng.NextUInt(buckets)]++;
            }

            int expected = draws / (int)buckets;
            int tolerance = expected / 20; // 5%

            for (int i = 0; i < buckets; i++)
            {
                Assert.That(counts[i], Is.EqualTo(expected).Within(tolerance), "Bucket {0} skewed", i);
            }
        }

        [Test]
        public void ShuffleIsAPermutation()
        {
            var rng = new Pcg32(7UL);
            var items = new int[64];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = i;
            }

            rng.Shuffle(items);

            var seen = new bool[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                Assert.IsFalse(seen[items[i]], "Value {0} appeared twice", items[i]);
                seen[items[i]] = true;
            }
        }

        [Test]
        public void ShuffleIsDeterministic()
        {
            var first = new int[32];
            var second = new int[32];
            for (int i = 0; i < 32; i++)
            {
                first[i] = i;
                second[i] = i;
            }

            new Pcg32(555UL).Shuffle(first);
            new Pcg32(555UL).Shuffle(second);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void PlayerSeedsAreDistinctWithinARoom()
        {
            const ulong session = 1971UL;
            var room = new RoomId(9);

            ulong p0 = SeedMixer.ForPlayer(session, room, new PlayerId(0));
            ulong p1 = SeedMixer.ForPlayer(session, room, new PlayerId(1));
            ulong p2 = SeedMixer.ForPlayer(session, room, new PlayerId(2));

            Assert.AreNotEqual(p0, p1);
            Assert.AreNotEqual(p1, p2);
            Assert.AreNotEqual(p0, p2);
        }

        [Test]
        public void RoomSeedsAreDistinctWithinASession()
        {
            const ulong session = 1971UL;

            Assert.AreNotEqual(SeedMixer.ForRoom(session, new RoomId(9)), SeedMixer.ForRoom(session, new RoomId(17)));
        }

        [Test]
        public void PlayerSeedIsStableAcrossCalls()
        {
            const ulong session = 1971UL;
            var room = new RoomId(9);
            var player = new PlayerId(2);

            Assert.AreEqual(
                SeedMixer.ForPlayer(session, room, player),
                SeedMixer.ForPlayer(session, room, player));
        }
    }
}
