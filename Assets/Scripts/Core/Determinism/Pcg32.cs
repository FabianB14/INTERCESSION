namespace Session.Core.Determinism
{
    /// <summary>
    /// PCG-XSH-RR 32-bit generator. Deterministic across platforms and runtime versions,
    /// which <c>System.Random</c> is not — its algorithm is an implementation detail and has
    /// changed between .NET versions. Lens assignment must reproduce exactly from a seed on
    /// every client, so nothing in Session.Core may use System.Random.
    ///
    /// Value type with no allocation. Copy it and you fork the stream; pass it by ref to advance it.
    /// </summary>
    public struct Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _increment;

        public Pcg32(ulong seed, ulong sequence = 0xDA3E39CB94B95BDBUL)
        {
            _state = 0UL;
            _increment = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>Uniform in [0, uint.MaxValue].</summary>
        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);

            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>
        /// Uniform in [0, boundExclusive). Rejection-sampled, so the distribution is flat —
        /// a plain modulo would bias low values and skew which props reveal clues.
        /// </summary>
        public uint NextUInt(uint boundExclusive)
        {
            if (boundExclusive == 0u)
            {
                throw new System.ArgumentOutOfRangeException(nameof(boundExclusive), "Bound must be positive.");
            }

            uint threshold = (uint)((0x100000000UL - boundExclusive) % boundExclusive);
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                {
                    return r % boundExclusive;
                }
            }
        }

        /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxExclusive), "Range must be non-empty.");
            }

            uint span = (uint)((long)maxExclusive - minInclusive);
            return minInclusive + (int)NextUInt(span);
        }

        /// <summary>Uniform in [0, 1). 24 bits of mantissa — enough for tuning rolls, not for physics.</summary>
        public float NextFloat01()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>True with the given percentage chance, 0..100.</summary>
        public bool NextChancePercent(int percent)
        {
            if (percent <= 0)
            {
                return false;
            }

            if (percent >= 100)
            {
                return true;
            }

            return NextUInt(100u) < (uint)percent;
        }

        /// <summary>In-place Fisher-Yates. Deterministic for a given stream position.</summary>
        public void Shuffle(int[] items)
        {
            if (items == null)
            {
                throw new System.ArgumentNullException(nameof(items));
            }

            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = (int)NextUInt((uint)(i + 1));
                int swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }
    }
}
