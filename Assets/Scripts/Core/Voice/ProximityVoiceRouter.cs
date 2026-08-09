using Session.Core.Identity;
using Session.Core.Spatial;

namespace Session.Core.Voice
{
    /// <summary>
    /// Tuning for proximity voice. Implemented by VoiceRulesSO in Session.Runtime.
    /// </summary>
    public interface IVoiceRules
    {
        /// <summary>Within this distance, a speaker is at full volume. Conversational range.</summary>
        float FullVolumeRadiusMeters { get; }

        /// <summary>Beyond this distance a speaker in the same room is inaudible.</summary>
        float FalloffEndMeters { get; }

        /// <summary>
        /// Gain multiplier applied when speaker and listener are in different rooms. The Institute's
        /// walls are plaster over brick — muffled, not soundproof.
        /// </summary>
        float ThroughWallMultiplier { get; }

        /// <summary>Beyond this, a speaker in a different room is cut entirely.</summary>
        float ThroughWallCutoffMeters { get; }

        /// <summary>Gains below this are treated as silence, so the mixer can skip the channel.</summary>
        float SilenceThreshold { get; }
    }

    public sealed class DefaultVoiceRules : IVoiceRules
    {
        public static readonly DefaultVoiceRules Instance = new DefaultVoiceRules();

        public float FullVolumeRadiusMeters => 3f;

        public float FalloffEndMeters => 18f;

        public float ThroughWallMultiplier => 0.35f;

        public float ThroughWallCutoffMeters => 8f;

        public float SilenceThreshold => 0.01f;
    }

    /// <summary>Where a player is, for voice purposes.</summary>
    public readonly struct VoiceListener
    {
        public readonly PlayerId Player;
        public readonly Vec3 Position;
        public readonly RoomId Room;
        public readonly bool Connected;

        public VoiceListener(PlayerId player, Vec3 position, RoomId room, bool connected = true)
        {
            Player = player;
            Position = position;
            Room = room;
            Connected = connected;
        }
    }

    /// <summary>
    /// Decides how loudly each listener hears each speaker.
    ///
    /// This is gameplay, not audio plumbing, which is why it lives in Core and is tested here. The
    /// entire game is players describing what they see to each other — see LORE.md, "you cannot
    /// trust your own eyes, only your friends' voices". If voice attenuation is wrong the puzzle
    /// design silently stops working, and that failure is very hard to spot in a play test because
    /// players will just talk louder.
    ///
    /// Steam Voice is the transport for the audio bytes. This decides routing and gain. Keeping the
    /// two separate means the falloff curve is testable without a Steam client running.
    /// </summary>
    public static class ProximityVoiceRouter
    {
        /// <summary>
        /// Gain in [0, 1] for <paramref name="listener"/> hearing <paramref name="speaker"/>.
        /// Zero means do not transmit — the caller should skip the channel entirely rather than
        /// send silent packets.
        /// </summary>
        public static float GainFor(in VoiceListener speaker, in VoiceListener listener, IVoiceRules rules)
        {
            if (rules == null)
            {
                throw new System.ArgumentNullException(nameof(rules));
            }

            if (!speaker.Connected || !listener.Connected)
            {
                return 0f;
            }

            // Never route a speaker back to themselves; it is the fastest way to build a feedback loop.
            if (speaker.Player == listener.Player)
            {
                return 0f;
            }

            float distance = Vec3.Distance(speaker.Position, listener.Position);
            bool sameRoom = speaker.Room == listener.Room && !speaker.Room.IsNone;

            float cutoff = sameRoom ? rules.FalloffEndMeters : rules.ThroughWallCutoffMeters;
            if (distance >= cutoff)
            {
                return 0f;
            }

            float gain;
            if (distance <= rules.FullVolumeRadiusMeters)
            {
                gain = 1f;
            }
            else
            {
                float span = cutoff - rules.FullVolumeRadiusMeters;
                gain = span <= 0f ? 0f : 1f - ((distance - rules.FullVolumeRadiusMeters) / span);
            }

            if (!sameRoom)
            {
                gain *= rules.ThroughWallMultiplier;
            }

            if (gain <= rules.SilenceThreshold)
            {
                return 0f;
            }

            return gain > 1f ? 1f : gain;
        }

        /// <summary>
        /// Fill <paramref name="gains"/> with this speaker's gain for every listener, indexed by
        /// player slot. Returns how many listeners can hear anything at all — zero means the
        /// speaker's audio does not need to be captured or sent this frame.
        /// </summary>
        public static int GainsForSpeaker(
            in VoiceListener speaker,
            System.ReadOnlySpan<VoiceListener> listeners,
            IVoiceRules rules,
            System.Span<float> gains)
        {
            if (gains.Length < listeners.Length)
            {
                throw new System.ArgumentException("Gain buffer is smaller than the listener set.", nameof(gains));
            }

            int audible = 0;
            for (int i = 0; i < listeners.Length; i++)
            {
                float gain = GainFor(in speaker, in listeners[i], rules);
                gains[i] = gain;

                if (gain > 0f)
                {
                    audible++;
                }
            }

            return audible;
        }
    }
}
