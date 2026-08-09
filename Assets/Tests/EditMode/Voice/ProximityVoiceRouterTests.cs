using NUnit.Framework;
using Session.Core.Identity;
using Session.Core.Spatial;
using Session.Core.Voice;

namespace Session.Tests.Core.Voice
{
    public sealed class ProximityVoiceRouterTests
    {
        private static readonly RoomId Nine = new RoomId(9);
        private static readonly RoomId Seventeen = new RoomId(17);

        private static VoiceListener At(int slot, float x, RoomId room, bool connected = true)
            => new VoiceListener(new PlayerId(slot), new Vec3(x, 0f, 0f), room, connected);

        private static IVoiceRules Rules => DefaultVoiceRules.Instance;

        [Test]
        public void SpeakerNeverHearsThemselves()
        {
            VoiceListener speaker = At(0, 0f, Nine);

            Assert.AreEqual(0f, ProximityVoiceRouter.GainFor(in speaker, in speaker, Rules));
        }

        [Test]
        public void ConversationalRangeIsFullVolume()
        {
            VoiceListener speaker = At(0, 0f, Nine);
            VoiceListener listener = At(1, 2f, Nine);

            Assert.AreEqual(1f, ProximityVoiceRouter.GainFor(in speaker, in listener, Rules));
        }

        [Test]
        public void GainFallsOffWithDistanceInTheSameRoom()
        {
            VoiceListener speaker = At(0, 0f, Nine);

            float near = ProximityVoiceRouter.GainFor(in speaker, At(1, 5f, Nine), Rules);
            float far = ProximityVoiceRouter.GainFor(in speaker, At(1, 12f, Nine), Rules);

            Assert.Greater(near, far);
            Assert.Greater(near, 0f);
            Assert.Greater(far, 0f);
        }

        [Test]
        public void BeyondFalloffEndIsSilent()
        {
            VoiceListener speaker = At(0, 0f, Nine);

            Assert.AreEqual(0f, ProximityVoiceRouter.GainFor(in speaker, At(1, 25f, Nine), Rules));
        }

        [Test]
        public void ThroughAWallIsQuieterAtTheSameDistance()
        {
            // The design depends on this: players in different rooms must be able to hear each
            // other enough to co-ordinate, but not so clearly that being apart costs nothing.
            VoiceListener speaker = At(0, 0f, Nine);

            float sameRoom = ProximityVoiceRouter.GainFor(in speaker, At(1, 5f, Nine), Rules);
            float otherRoom = ProximityVoiceRouter.GainFor(in speaker, At(1, 5f, Seventeen), Rules);

            Assert.Greater(sameRoom, otherRoom);
            Assert.Greater(otherRoom, 0f, "Adjacent rooms must not be fully soundproof.");
        }

        [Test]
        public void ThroughWallCutsOffSoonerThanSameRoom()
        {
            VoiceListener speaker = At(0, 0f, Nine);

            Assert.AreEqual(0f, ProximityVoiceRouter.GainFor(in speaker, At(1, 10f, Seventeen), Rules));
            Assert.Greater(ProximityVoiceRouter.GainFor(in speaker, At(1, 10f, Nine), Rules), 0f);
        }

        [Test]
        public void DisconnectedPeersAreSilentBothWays()
        {
            VoiceListener speaker = At(0, 0f, Nine);
            VoiceListener gone = At(1, 1f, Nine, connected: false);

            Assert.AreEqual(0f, ProximityVoiceRouter.GainFor(in speaker, in gone, Rules));
            Assert.AreEqual(0f, ProximityVoiceRouter.GainFor(in gone, in speaker, Rules));
        }

        [Test]
        public void GainIsAlwaysInRange()
        {
            VoiceListener speaker = At(0, 0f, Nine);

            for (int i = 0; i < 400; i++)
            {
                float distance = i * 0.1f;
                float same = ProximityVoiceRouter.GainFor(in speaker, At(1, distance, Nine), Rules);
                float other = ProximityVoiceRouter.GainFor(in speaker, At(1, distance, Seventeen), Rules);

                Assert.That(same, Is.InRange(0f, 1f), "same-room gain at {0}m", distance);
                Assert.That(other, Is.InRange(0f, 1f), "cross-room gain at {0}m", distance);
            }
        }

        [Test]
        public void GainsForSpeakerCountsAudibleListeners()
        {
            VoiceListener speaker = At(0, 0f, Nine);

            var listeners = new[]
            {
                speaker,                    // self, never audible
                At(1, 2f, Nine),            // close, same room
                At(2, 100f, Nine),          // far away
                At(3, 3f, Seventeen)        // next door
            };

            var gains = new float[4];
            int audible = ProximityVoiceRouter.GainsForSpeaker(in speaker, listeners, Rules, gains);

            Assert.AreEqual(2, audible);
            Assert.AreEqual(0f, gains[0]);
            Assert.AreEqual(1f, gains[1]);
            Assert.AreEqual(0f, gains[2]);
            Assert.Greater(gains[3], 0f);
        }

        [Test]
        public void PlayersWithNoRoomDoNotCountAsSharingOne()
        {
            // Two players in transit between rooms both have RoomId.None. That must not be treated
            // as "the same room", or corridors would carry voice at full volume.
            VoiceListener a = At(0, 0f, RoomId.None);
            VoiceListener b = At(1, 5f, RoomId.None);

            float gain = ProximityVoiceRouter.GainFor(in a, in b, Rules);
            float sameRoomGain = ProximityVoiceRouter.GainFor(At(0, 0f, Nine), At(1, 5f, Nine), Rules);

            Assert.Less(gain, sameRoomGain);
        }
    }
}
