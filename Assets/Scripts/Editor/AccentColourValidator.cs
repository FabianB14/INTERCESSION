using System.Collections.Generic;
using System.Text;
using Session.Runtime.Tuning;
using Session.Runtime.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Session.Editor
{
    /// <summary>
    /// Finds every use of the interactable accent (#FF8A3D) on something that is not interactable.
    ///
    /// CLAUDE.md asks for this to be enforced in code review. Code review does not scale to a
    /// project full of prefabs and materials, and the failure mode is silent: the colour drifts
    /// into decoration, players stop trusting it, and the game's only "you can touch this" signal
    /// quietly stops working. This makes the check mechanical.
    ///
    /// Scans materials, prefab Images and TMP labels. A hit is a violation unless the object is on
    /// an interactable PropView or is explicitly whitelisted below.
    /// </summary>
    public static class AccentColourValidator
    {
        /// <summary>How far a colour may sit from the accent before it stops counting. 8-bit channels.</summary>
        private const int ChannelTolerance = 6;

        private static readonly string[] WhitelistedPathFragments =
        {
            "/Editor/",
            "/Gizmos/",
            "SO_UiPalette"
        };

        [MenuItem("Session/Validate Accent Colour Use", priority = 101)]
        public static void ValidateAll()
        {
            var violations = new StringBuilder();
            int scanned = 0;

            scanned += ScanMaterials(violations);
            scanned += ScanPrefabs(violations);

            if (violations.Length > 0)
            {
                Debug.LogError(
                    "[Session] #FF8A3D is the interactable accent and must never be decorative. " +
                    "Found decorative uses:\n" + violations);
                return;
            }

            Debug.Log("[Session] Accent colour check passed across " + scanned + " asset(s).");
        }

        private static int ScanMaterials(StringBuilder violations)
        {
            string[] guids = AssetDatabase.FindAssets("t:Material");
            int scanned = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsWhitelisted(path))
                {
                    continue;
                }

                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    continue;
                }

                scanned++;

                if (!MaterialUsesAccent(material, out string propertyName))
                {
                    continue;
                }

                violations.AppendLine(
                    "  " + path + " — material property '" + propertyName + "'. " +
                    "If this material is on an interactable prop, drive the colour from UiPaletteSO " +
                    "at runtime instead of baking it in.");
            }

            return scanned;
        }

        private static bool MaterialUsesAccent(Material material, out string propertyName)
        {
            propertyName = null;

            Shader shader = material.shader;
            if (shader == null)
            {
                return false;
            }

            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color)
                {
                    continue;
                }

                string name = shader.GetPropertyName(i);
                if (!IsAccent(material.GetColor(name)))
                {
                    continue;
                }

                propertyName = name;
                return true;
            }

            return false;
        }

        private static int ScanPrefabs(StringBuilder violations)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            int scanned = 0;
            var buffer = new List<Component>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsWhitelisted(path))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                scanned++;

                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                {
                    if (IsAccent(image.color) && !IsOnInteractable(image.gameObject))
                    {
                        violations.AppendLine(
                            "  " + path + " → " + Path(image.transform) + " (Image). " +
                            "Accent on a non-interactable element.");
                    }
                }

                foreach (TMP_Text text in prefab.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (IsAccent(text.color) && !IsOnInteractable(text.gameObject))
                    {
                        violations.AppendLine(
                            "  " + path + " → " + Path(text.transform) + " (TMP_Text). " +
                            "Accent on a non-interactable label. If this is an interaction prompt, " +
                            "let InteractionPromptView set the colour — it gets the decision from " +
                            "PromptResolver, which is the only thing allowed to make it.");
                    }
                }

                buffer.Clear();
            }

            return scanned;
        }

        /// <summary>
        /// The accent is legitimate on anything sitting under an interactable prop — that is
        /// exactly what it is for.
        /// </summary>
        private static bool IsOnInteractable(GameObject target)
        {
            PropView prop = target.GetComponentInParent<PropView>();
            if (prop != null && prop.IsInteractable)
            {
                return true;
            }

            return target.GetComponentInParent<Selectable>() != null;
        }

        private static bool IsAccent(Color colour)
        {
            Color32 c = colour;
            Color32 accent = UiPaletteSO.LockedAccent;

            return Mathf.Abs(c.r - accent.r) <= ChannelTolerance
                   && Mathf.Abs(c.g - accent.g) <= ChannelTolerance
                   && Mathf.Abs(c.b - accent.b) <= ChannelTolerance
                   && c.a > 8;
        }

        private static bool IsWhitelisted(string path)
        {
            for (int i = 0; i < WhitelistedPathFragments.Length; i++)
            {
                if (path.Contains(WhitelistedPathFragments[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Path(Transform transform)
        {
            var builder = new StringBuilder(transform.name);
            Transform current = transform.parent;

            while (current != null)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return builder.ToString();
        }
    }
}
