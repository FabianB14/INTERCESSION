using System;
using System.Text;
using Session.Core.Perception;
using Session.Core.Rooms;
using Session.Runtime.Tuning;
using UnityEditor;
using UnityEngine;

namespace Session.Editor
{
    /// <summary>
    /// Sweeps every RoomLayoutSO in the project and proves, over many seeds and every supported
    /// group size, that no player can ever solve the room alone.
    ///
    /// The EditMode tests prove the algorithm. This proves the <i>content</i> — a room can be
    /// authored badly (too few clues, a clue on a prop with no concealing variant) in ways no unit
    /// test will catch, because the failure lives in an asset. Run it before every content commit.
    /// </summary>
    public static class RoomLayoutValidator
    {
        private const int SeedsPerRoom = 2000;

        [MenuItem("Session/Validate Room Layouts", priority = 100)]
        public static void ValidateAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(RoomLayoutSO));

            if (guids.Length == 0)
            {
                Debug.Log("[Session] No RoomLayoutSO assets found. Nothing to validate.");
                return;
            }

            var rules = LoadRulesOrDefault();
            var failures = new StringBuilder();
            int roomsChecked = 0;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var layout = AssetDatabase.LoadAssetAtPath<RoomLayoutSO>(path);

                    if (layout == null)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Validating room layouts", path, (float)i / guids.Length);

                    ValidateOne(layout, path, rules, failures);
                    roomsChecked++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (failures.Length > 0)
            {
                Debug.LogError("[Session] Room layout validation FAILED:\n" + failures);
                return;
            }

            Debug.Log(
                "[Session] " + roomsChecked + " room layout(s) validated over " + SeedsPerRoom +
                " seeds each, for 2-4 players. No room is solvable alone.");
        }

        private static void ValidateOne(RoomLayoutSO layout, string path, ILensRules rules, StringBuilder failures)
        {
            RoomDefinition room;
            try
            {
                room = layout.Build();
            }
            catch (Exception exception)
            {
                failures.AppendLine("  " + path + ": " + exception.Message);
                return;
            }

            for (int playerCount = rules.MinPlayers; playerCount <= rules.MaxPlayers; playerCount++)
            {
                for (int seed = 0; seed < SeedsPerRoom; seed++)
                {
                    bool assigned = LensAssigner.TryAssign(
                        room, (ulong)seed, playerCount, rules,
                        out LensAssignment assignment, out LensAssignmentFailure failure);

                    if (!assigned)
                    {
                        failures.AppendLine(
                            "  " + path + ": cannot assign lenses for " + playerCount +
                            " players — " + Explain(failure));
                        break; // One report per group size is enough; the cause is structural.
                    }

                    LensValidation validation = LensValidator.Validate(room, assignment);
                    if (validation.IsValid)
                    {
                        continue;
                    }

                    failures.AppendLine(
                        "  " + path + ": seed " + seed + ", " + playerCount + " players — " + validation);
                    break;
                }
            }
        }

        private static string Explain(LensAssignmentFailure failure)
        {
            switch (failure)
            {
                case LensAssignmentFailure.NotEnoughRequiredClues:
                    return "the room has fewer required clues than players, so someone has nothing to contribute. " +
                           "Add a clue, or lower max players.";
                case LensAssignmentFailure.PropMissingConcealingVariant:
                    return "a clue-carrying prop has no concealing variant, so its clue can never be withheld. " +
                           "Every clue prop needs at least one variant with RevealsClue off.";
                case LensAssignmentFailure.PropMissingRevealingVariant:
                    return "a clue-carrying prop has no revealing variant, so nobody could ever read it.";
                case LensAssignmentFailure.RequiredClueHasNoProp:
                    return "a puzzle requires a clue that no prop in the room carries.";
                default:
                    return failure.ToString();
            }
        }

        private static ILensRules LoadRulesOrDefault()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LensRulesSO));
            if (guids.Length == 0)
            {
                Debug.LogWarning("[Session] No LensRulesSO asset found. Validating against built-in defaults.");
                return DefaultLensRules.Instance;
            }

            if (guids.Length > 1)
            {
                Debug.LogWarning(
                    "[Session] Found " + guids.Length + " LensRulesSO assets. Validating against the first.");
            }

            return AssetDatabase.LoadAssetAtPath<LensRulesSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
