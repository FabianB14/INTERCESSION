using System.Collections.Generic;

namespace Session.Core.Content
{
    /// <summary>
    /// Resolves the int keys Core passes around back into the strings players read.
    ///
    /// Core deals in <see cref="ContentKey"/> hashes so nothing on a per-frame path touches a
    /// string. This is the one place that turns them back, and it is a plain dictionary lookup —
    /// no allocation, no formatting.
    ///
    /// A missing key returns a visible placeholder rather than null or empty. Silent empty strings
    /// are how a prop ships with no name and nobody notices until a player asks what the thing in
    /// the corner is called.
    /// </summary>
    public sealed class ContentTable
    {
        /// <summary>What a missing key renders as. Deliberately ugly and deliberately not empty.</summary>
        public const string MissingPlaceholder = "[no copy]";

        private readonly Dictionary<int, string> _byKey;
        private int _missCount;

        public ContentTable(IReadOnlyList<int> keys, IReadOnlyList<string> values)
        {
            if (keys == null || values == null)
            {
                throw new System.ArgumentNullException(nameof(keys));
            }

            if (keys.Count != values.Count)
            {
                throw new System.ArgumentException("Content keys and values must be the same length.", nameof(values));
            }

            _byKey = new Dictionary<int, string>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                // Last writer wins rather than throwing: a duplicate key in an authoring table is
                // a content problem to report, not a reason to refuse to boot the game.
                _byKey[keys[i]] = values[i] ?? string.Empty;
            }
        }

        public int Count => _byKey.Count;

        /// <summary>How many lookups have failed since load. Surface this in a dev overlay.</summary>
        public int MissCount => _missCount;

        public string Get(int key)
        {
            if (key == ContentKey.None)
            {
                return string.Empty;
            }

            if (_byKey.TryGetValue(key, out string value))
            {
                return value;
            }

            _missCount++;
            return MissingPlaceholder;
        }

        public bool TryGet(int key, out string? value) => _byKey.TryGetValue(key, out value);

        public bool Has(int key) => _byKey.ContainsKey(key);

        public void ResetMissCount() => _missCount = 0;
    }
}
