using System.Collections.Generic;
using System.Text;
using Session.Core.Content;
using Session.Core.Tapes;
using Session.Runtime.Tuning;
using UnityEditor;
using UnityEngine;

namespace Session.Editor
{
    /// <summary>
    /// Audits every intake tape in the project.
    ///
    /// The check that matters is specific to tapes, and it is the inverse of the paper-prop one.
    /// A document can carry a clue because each player reads a different document. A tape cannot,
    /// because a tape is a recording — everyone hears the same words at the same moment. Speak an
    /// answer on a tape and you have not leaked it to one player; you have handed it to the entire
    /// group, and that room now requires no co-operation from anybody.
    ///
    /// That failure is quieter than any other in the project. Nobody experiences anything strange.
    /// The room is simply easy, and a play test reads "easy" as tuning rather than as the central
    /// mechanic having switched itself off for that room.
    ///
    /// Every tape is checked against every room's solution, because a deck can be carried, a tape
    /// can be found in one room and played in another, and rooms get reordered during production.
    /// </summary>
    public static class TapeValidator
    {
        [MenuItem("Session/Validate Intake Tapes", priority = 103)]
        public static void ValidateAll()
        {
            ContentTable content = LoadContentTable();
            if (content == null)
            {
                Debug.LogWarning("[Session] No ContentTableSO found. Tapes cannot be audited without transcripts.");
                return;
            }

            List<int[]> solutions = LoadAllSolutions();
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(TapeSO));

            if (guids.Length == 0)
            {
                Debug.Log("[Session] No intake tapes found.");
                return;
            }

            var problems = new StringBuilder();
            var seenIds = new Dictionary<int, string>();
            int audited = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<TapeSO>(path);
                if (asset == null)
                {
                    continue;
                }

                TapeDefinition tape;
                try
                {
                    tape = asset.Build();
                }
                catch (System.Exception exception)
                {
                    problems.AppendLine("  " + path + ": " + exception.Message);
                    continue;
                }

                audited++;

                // Duplicate ids silently merge two tapes in the library's found/heard counts.
                if (seenIds.TryGetValue(tape.Id.Value, out string other))
                {
                    problems.AppendLine(
                        "  " + path + ": tape id " + tape.Id.Value + " is already used by " + other + ".");
                }
                else
                {
                    seenIds.Add(tape.Id.Value, path);
                }

                AuditAgainstEverySolution(path, tape, content, solutions, problems);
            }

            if (problems.Length > 0)
            {
                Debug.LogError("[Session] Intake tape audit FAILED:\n" + problems);
                return;
            }

            Debug.Log(
                "[Session] Intake tape audit passed: " + audited + " tape(s) checked against " +
                solutions.Count + " room solution(s). No tape speaks an answer.");
        }

        private static void AuditAgainstEverySolution(
            string path,
            TapeDefinition tape,
            ContentTable content,
            List<int[]> solutions,
            StringBuilder problems)
        {
            // Structural issues do not depend on any solution, so check them once with none.
            TapeAuditResult structural = TapeAudit.Audit(tape, content, System.Array.Empty<int>());

            if ((structural.Issues & TapeIssue.NoTranscript) != 0)
            {
                problems.AppendLine(
                    "  " + path + ": no transcript. Subtitles are not optional — this is a quiet voice " +
                    "competing with proximity chat, and most players will miss the writing without them.");
                return;
            }

            if ((structural.Issues & TapeIssue.MissingTranscriptCopy) != 0)
            {
                problems.AppendLine(
                    "  " + path + ": cue " + structural.CueIndex + " has no copy in the content table.");
            }

            if ((structural.Issues & TapeIssue.TranscriptCoverageLow) != 0)
            {
                problems.AppendLine(
                    "  " + path + ": transcript covers only " +
                    (structural.TranscriptCoverage * 100f).ToString("0") +
                    "% of the runtime. Usually means the timings stop partway through.");
            }

            for (int i = 0; i < solutions.Count; i++)
            {
                TapeAuditResult result = TapeAudit.Audit(tape, content, solutions[i]);

                if ((result.Issues & TapeIssue.AnswerSpokenOnTape) == 0)
                {
                    continue;
                }

                problems.AppendLine(
                    "  " + path + ": cue " + result.CueIndex + " speaks a room's canonical answer. " +
                    "Every player hears a tape identically, so this gives the whole group the same " +
                    "clue and that room stops needing anyone to talk to anyone. Reword the line.");
                break;
            }
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

        /// <summary>
        /// Every solution in every room. Read via SerializedObject so a room that is mid-authoring
        /// and would throw from Build() still contributes its answers to the check.
        /// </summary>
        private static List<int[]> LoadAllSolutions()
        {
            var solutions = new List<int[]>();

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(RoomLayoutSO)))
            {
                var layout = AssetDatabase.LoadAssetAtPath<RoomLayoutSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (layout == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(layout);
                SerializedProperty nodes = serialized.FindProperty("_puzzleNodes");

                for (int i = 0; nodes != null && i < nodes.arraySize; i++)
                {
                    SerializedProperty tokens = nodes.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("SolutionTokens");

                    if (tokens == null || tokens.arraySize < 2)
                    {
                        continue;
                    }

                    var solution = new int[tokens.arraySize];
                    for (int t = 0; t < solution.Length; t++)
                    {
                        solution[t] = tokens.GetArrayElementAtIndex(t).intValue;
                    }

                    solutions.Add(solution);
                }
            }

            return solutions;
        }
    }
}
