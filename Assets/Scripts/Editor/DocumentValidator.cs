using System.Collections.Generic;
using System.Text;
using Session.Core.Content;
using Session.Core.Documents;
using Session.Runtime.Tuning;
using Session.Runtime.View;
using UnityEditor;
using UnityEngine;

namespace Session.Editor
{
    /// <summary>
    /// Audits every paper prop in the project against the lens split.
    ///
    /// The bug this exists to catch: a document on a <i>concealing</i> variant that nonetheless
    /// spells out the answer. You write the withheld version of a patient file, mark the obvious
    /// line as ClueBearing, and forget the same four digits appear two paragraphs down as a ward
    /// number. The room still gets solved, so nothing looks broken — the player who can read it has
    /// no idea their partner was supposed to be needed. This is invisible in play tests and fatal
    /// to the premise, which makes it exactly the kind of thing to check mechanically.
    ///
    /// Reads the prefab, works out which room the prop belongs to, pulls that node's canonical
    /// solution out of the RoomLayoutSO, and looks for it in copy the concealing lens can read.
    /// </summary>
    public static class DocumentValidator
    {
        [MenuItem("Session/Validate Paper Props", priority = 102)]
        public static void ValidateAll()
        {
            ContentTable content = LoadContentTable();
            if (content == null)
            {
                Debug.LogWarning("[Session] No ContentTableSO found. Paper props cannot be audited without copy.");
                return;
            }

            List<RoomDefinitionEntry> rooms = LoadRooms();
            var problems = new StringBuilder();
            int auditedDocuments = 0;
            int auditedProps = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                foreach (PaperPropView paper in prefab.GetComponentsInChildren<PaperPropView>(true))
                {
                    PropView prop = paper.GetComponent<PropView>();
                    if (prop == null)
                    {
                        continue;
                    }

                    auditedProps++;
                    auditedDocuments += AuditProp(path, paper, prop, rooms, content, problems);
                }
            }

            if (problems.Length > 0)
            {
                Debug.LogError(
                    "[Session] Paper prop audit FAILED. These documents break the perception split:\n" + problems);
                return;
            }

            Debug.Log(
                "[Session] Paper prop audit passed: " + auditedDocuments + " document(s) across " +
                auditedProps + " prop(s). No concealing variant leaks its answer.");
        }

        private static int AuditProp(
            string prefabPath,
            PaperPropView paper,
            PropView prop,
            List<RoomDefinitionEntry> rooms,
            ContentTable content,
            StringBuilder problems)
        {
            SerializedObject serialized = new SerializedObject(paper);
            SerializedProperty documents = serialized.FindProperty("_documentsByVariant");

            if (documents == null || documents.arraySize == 0)
            {
                problems.AppendLine("  " + prefabPath + " → " + paper.name + ": no documents assigned.");
                return 0;
            }

            RoomDefinitionEntry room = FindRoomContaining(rooms, prop.PropId);
            int[] solution = room?.SolutionForProp(prop.PropId);

            int audited = 0;

            for (int variant = 0; variant < documents.arraySize; variant++)
            {
                var source = documents.GetArrayElementAtIndex(variant).objectReferenceValue as DocumentSO;
                if (source == null)
                {
                    problems.AppendLine(
                        "  " + prefabPath + " → " + paper.name + ": variant " + variant + " has no document.");
                    continue;
                }

                DocumentDefinition document;
                try
                {
                    document = source.Build();
                }
                catch (System.Exception exception)
                {
                    problems.AppendLine("  " + prefabPath + " → " + source.name + ": " + exception.Message);
                    continue;
                }

                audited++;

                bool revealsClue = room != null && room.VariantReveals(prop.PropId, variant);

                DocumentAuditResult result = DocumentAudit.Audit(
                    document, content, solution ?? System.Array.Empty<int>(), revealsClue);

                if (result.IsClean)
                {
                    continue;
                }

                problems.AppendLine(
                    "  " + prefabPath + " → " + source.name + " (variant " + variant +
                    (revealsClue ? ", revealing" : ", concealing") + "): " + Explain(result));
            }

            return audited;
        }

        private static string Explain(DocumentAuditResult result)
        {
            var builder = new StringBuilder();

            if ((result.Issues & DocumentIssue.AnswerLeakedThroughConcealingLens) != 0)
            {
                builder.Append("the answer appears in legible copy on page ").Append(result.PageIndex)
                    .Append(", block ").Append(result.BlockIndex)
                    .Append(" — this lens is supposed to be withholding it. ");
            }

            if ((result.Issues & DocumentIssue.RevealingVariantHasNoClueBlock) != 0)
            {
                builder.Append("this variant is supposed to carry the clue but no block is marked ClueBearing, " +
                               "so nobody can read it and the room cannot be finished. ");
            }

            if ((result.Issues & DocumentIssue.MissingCopy) != 0)
            {
                builder.Append("a block has no copy in the content table (page ").Append(result.PageIndex)
                    .Append(", block ").Append(result.BlockIndex).Append("). ");
            }

            if ((result.Issues & DocumentIssue.EmptyPage) != 0)
            {
                builder.Append("page ").Append(result.PageIndex).Append(" has no blocks. ");
            }

            if ((result.Issues & DocumentIssue.ConcealingVariantContainsClueBlock) != 0)
            {
                builder.Append("[minor] a ClueBearing block on a concealing variant — the reader hides it, " +
                               "but this is the same document with a hole rather than two honest different ones. ");
            }

            return builder.ToString().TrimEnd();
        }

        private static ContentTable LoadContentTable()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(ContentTableSO));
            if (guids.Length == 0)
            {
                return null;
            }

            var table = AssetDatabase.LoadAssetAtPath<ContentTableSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return table != null ? table.Build() : null;
        }

        private static List<RoomDefinitionEntry> LoadRooms()
        {
            var rooms = new List<RoomDefinitionEntry>();

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(RoomLayoutSO)))
            {
                var layout = AssetDatabase.LoadAssetAtPath<RoomLayoutSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (layout == null)
                {
                    continue;
                }

                rooms.Add(new RoomDefinitionEntry(layout));
            }

            return rooms;
        }

        private static RoomDefinitionEntry FindRoomContaining(List<RoomDefinitionEntry> rooms, int propId)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].HasProp(propId))
                {
                    return rooms[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Reads a RoomLayoutSO through SerializedObject rather than Build(), so a room that is
        /// mid-authoring and would throw can still have its paper props checked.
        /// </summary>
        private sealed class RoomDefinitionEntry
        {
            private readonly Dictionary<int, int> _clueByProp = new Dictionary<int, int>();
            private readonly Dictionary<int, List<bool>> _variantRevealsByProp = new Dictionary<int, List<bool>>();
            private readonly Dictionary<int, int[]> _solutionByClue = new Dictionary<int, int[]>();

            public RoomDefinitionEntry(RoomLayoutSO layout)
            {
                var serialized = new SerializedObject(layout);

                SerializedProperty props = serialized.FindProperty("_props");
                for (int i = 0; props != null && i < props.arraySize; i++)
                {
                    SerializedProperty entry = props.GetArrayElementAtIndex(i);
                    int propId = entry.FindPropertyRelative("PropId").intValue;
                    int clueId = entry.FindPropertyRelative("ClueId").intValue;

                    _clueByProp[propId] = clueId;

                    SerializedProperty variants = entry.FindPropertyRelative("Variants");
                    var reveals = new List<bool>(variants != null ? variants.arraySize : 0);

                    for (int v = 0; variants != null && v < variants.arraySize; v++)
                    {
                        reveals.Add(variants.GetArrayElementAtIndex(v).FindPropertyRelative("RevealsClue").boolValue);
                    }

                    _variantRevealsByProp[propId] = reveals;
                }

                SerializedProperty nodes = serialized.FindProperty("_puzzleNodes");
                for (int i = 0; nodes != null && i < nodes.arraySize; i++)
                {
                    SerializedProperty node = nodes.GetArrayElementAtIndex(i);

                    SerializedProperty tokens = node.FindPropertyRelative("SolutionTokens");
                    var solution = new int[tokens != null ? tokens.arraySize : 0];
                    for (int t = 0; t < solution.Length; t++)
                    {
                        solution[t] = tokens.GetArrayElementAtIndex(t).intValue;
                    }

                    SerializedProperty clues = node.FindPropertyRelative("RequiredClueIds");
                    for (int c = 0; clues != null && c < clues.arraySize; c++)
                    {
                        _solutionByClue[clues.GetArrayElementAtIndex(c).intValue] = solution;
                    }
                }
            }

            public bool HasProp(int propId) => _clueByProp.ContainsKey(propId);

            public bool VariantReveals(int propId, int variantIndex)
            {
                return _variantRevealsByProp.TryGetValue(propId, out List<bool> reveals)
                       && variantIndex >= 0 && variantIndex < reveals.Count
                       && reveals[variantIndex];
            }

            /// <summary>The canonical answer this prop's clue feeds into, or null for set dressing.</summary>
            public int[] SolutionForProp(int propId)
            {
                if (!_clueByProp.TryGetValue(propId, out int clueId) || clueId == 0)
                {
                    return null;
                }

                return _solutionByClue.TryGetValue(clueId, out int[] solution) ? solution : null;
            }
        }
    }
}
