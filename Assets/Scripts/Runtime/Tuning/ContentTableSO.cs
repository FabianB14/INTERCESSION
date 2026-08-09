using System.Collections.Generic;
using Session.Core.Content;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// Authored copy: the strings behind every content key.
    ///
    /// Keys are written as readable paths — <c>prop.curtain.hospital</c> — and hashed by
    /// <see cref="ContentKey"/> at load. Designers never see the integers.
    ///
    /// Tone, from LORE.md: the Institute is never evil in its own voice. Signage is helpful, forms
    /// are polite, and the dread comes from the gap between that tone and what is happening. Copy
    /// that sounds threatening is wrong even when it is describing something threatening.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Content Table", fileName = "SO_ContentTable")]
    public sealed class ContentTableSO : ScriptableObject
    {
        [System.Serializable]
        public sealed class Entry
        {
            [Tooltip("Readable path, e.g. prop.bottle.label or ui.prompt.examine")]
            public string Key = string.Empty;

            [TextArea]
            public string Text = string.Empty;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        public int EntryCount => _entries.Count;

        /// <summary>Build the runtime lookup. Call once at load; it allocates.</summary>
        public ContentTable Build()
        {
            var keys = new List<int>(_entries.Count);
            var values = new List<string>(_entries.Count);
            var seen = new HashSet<int>();

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Key))
                {
                    continue;
                }

                int key = ContentKey.Of(entry.Key);

                if (!seen.Add(key))
                {
                    // Either a duplicated key or — vanishingly unlikely but worth naming — a hash
                    // collision between two different paths. Both need a human to look.
                    Debug.LogError(
                        "[Session] Content table '" + name + "' has a key collision on '" + entry.Key +
                        "'. Two entries hash to the same value; rename one.");
                    continue;
                }

                keys.Add(key);
                values.Add(entry.Text);
            }

            return new ContentTable(keys, values);
        }
    }
}
