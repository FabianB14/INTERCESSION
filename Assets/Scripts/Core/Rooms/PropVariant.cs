using Session.Core.Identity;

namespace Session.Core.Rooms
{
    /// <summary>
    /// One player's rendering of a prop. The variant changes what the prop looks like, what it is
    /// called, and what text it carries. It never changes what the prop <i>does</i>.
    ///
    /// From LORE.md: the objects are the same objects. The Institute is not lying to anyone —
    /// it is showing each person the honest version. A privacy curtain and a shower curtain are
    /// the same curtain, hang in the same place, and hide the same thing.
    /// </summary>
    public readonly struct PropVariant
    {
        /// <summary>Stable id for this variant within its prop. Used by the Runtime layer to pick a mesh/material set.</summary>
        public readonly VariantId Id;

        /// <summary>Content key for the name this player would use out loud. Resolved outside Core.</summary>
        public readonly int DisplayNameKey;

        /// <summary>Content key for legible surface text — a label, a stencil, a scratch.</summary>
        public readonly int SurfaceTextKey;

        /// <summary>
        /// Whether this rendering exposes the prop's clue to its holder.
        ///
        /// A concealing variant is not a blank prop. It is the same object rendered as something
        /// whose surface carries nothing readable: the medication bottle is a water glass, the
        /// bedframe is unscratched. The player sees an object and has nothing to read from it.
        /// </summary>
        public readonly bool RevealsClue;

        public PropVariant(VariantId id, int displayNameKey, int surfaceTextKey, bool revealsClue)
        {
            Id = id;
            DisplayNameKey = displayNameKey;
            SurfaceTextKey = surfaceTextKey;
            RevealsClue = revealsClue;
        }
    }

    /// <summary>Stable id for a prop variant. Scoped to its owning prop, not globally unique.</summary>
    public readonly struct VariantId : System.IEquatable<VariantId>
    {
        public readonly int Value;

        public VariantId(int value)
        {
            Value = value;
        }

        public bool Equals(VariantId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is VariantId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => "Variant(" + Value + ")";

        public static bool operator ==(VariantId a, VariantId b) => a.Value == b.Value;
        public static bool operator !=(VariantId a, VariantId b) => a.Value != b.Value;
    }

    /// <summary>
    /// A prop as the server knows it: one identity, one optional clue, and the set of ways it can
    /// present itself. Immutable after load.
    /// </summary>
    public sealed class PropDefinition
    {
        private readonly PropVariant[] _variants;

        /// <summary>Canonical identity. Same for every player.</summary>
        public readonly PropId Id;

        /// <summary>The information this prop carries, or <see cref="ClueId.None"/> for set dressing.</summary>
        public readonly ClueId Clue;

        public PropDefinition(PropId id, ClueId clue, PropVariant[] variants)
        {
            if (id.IsNone)
            {
                throw new System.ArgumentException("Prop id must not be None.", nameof(id));
            }

            if (variants == null || variants.Length == 0)
            {
                throw new System.ArgumentException("A prop needs at least one variant.", nameof(variants));
            }

            if (variants.Length > byte.MaxValue)
            {
                throw new System.ArgumentException(
                    "A prop may have at most 255 variants — lenses store the choice as a byte.", nameof(variants));
            }

            Id = id;
            Clue = clue;
            _variants = variants;
        }

        public bool CarriesClue => !Clue.IsNone;

        public int VariantCount => _variants.Length;

        public PropVariant VariantAt(int index) => _variants[index];

        /// <summary>Number of variants that expose the clue. Zero on a clue-carrying prop is a room authoring bug.</summary>
        public int CountVariants(bool revealing)
        {
            int count = 0;
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i].RevealsClue == revealing)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Nth variant matching the requested reveal state. Used by the lens assigner after it has
        /// decided <i>whether</i> this player may read the clue; this picks <i>which</i> honest
        /// version they see.
        /// </summary>
        public int VariantIndexByRank(bool revealing, int rank)
        {
            int seen = 0;
            for (int i = 0; i < _variants.Length; i++)
            {
                if (_variants[i].RevealsClue != revealing)
                {
                    continue;
                }

                if (seen == rank)
                {
                    return i;
                }

                seen++;
            }

            throw new System.ArgumentOutOfRangeException(nameof(rank), "No variant of that kind at that rank.");
        }
    }
}
