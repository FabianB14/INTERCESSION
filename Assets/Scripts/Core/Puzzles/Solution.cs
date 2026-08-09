using System;

namespace Session.Core.Puzzles
{
    public enum SolutionKind
    {
        /// <summary>Tokens must arrive in this exact order. A four-digit keypad code.</summary>
        Ordered = 0,

        /// <summary>Tokens must all be present, order irrelevant. Four dials set to four values.</summary>
        Unordered = 1
    }

    /// <summary>
    /// The canonical answer to a puzzle node. Canonical is the whole point: a four-digit code is
    /// the same four digits for every player, no matter which surface each of them read it from.
    ///
    /// Held only by the server. <see cref="Matches"/> is the only way to test an attempt, and it
    /// never allocates.
    /// </summary>
    public readonly struct Solution
    {
        private readonly int[] _tokens;

        public readonly SolutionKind Kind;

        public Solution(SolutionKind kind, params int[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                throw new ArgumentException("A solution needs at least one token.", nameof(tokens));
            }

            Kind = kind;
            _tokens = tokens;
        }

        public int TokenCount => _tokens?.Length ?? 0;

        public bool IsValid => _tokens != null && _tokens.Length > 0;

        public bool Matches(ReadOnlySpan<int> attempt)
        {
            if (_tokens == null || attempt.Length != _tokens.Length)
            {
                return false;
            }

            if (Kind == SolutionKind.Ordered)
            {
                for (int i = 0; i < _tokens.Length; i++)
                {
                    if (attempt[i] != _tokens[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            // Unordered: multiset equality. Bounded by TokenCount, which room authoring keeps small,
            // so the quadratic scan is cheaper than allocating a set.
            Span<bool> consumed = stackalloc bool[_tokens.Length];
            for (int i = 0; i < attempt.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < _tokens.Length; j++)
                {
                    if (consumed[j] || _tokens[j] != attempt[i])
                    {
                        continue;
                    }

                    consumed[j] = true;
                    found = true;
                    break;
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
