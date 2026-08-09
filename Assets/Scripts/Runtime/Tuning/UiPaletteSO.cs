using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// The locked palette, in one asset.
    ///
    /// The accent is the reason this exists. #FF8A3D means "you can interact with this" and is
    /// never decorative — if it is scattered as a literal across a dozen prefabs, that rule survives
    /// exactly as long as everyone remembers it. Referenced from here, `Session > Validate Accent
    /// Colour Use` can find every violation in the project.
    ///
    /// The six body colours are placeholders matching the names in CLAUDE.md. Actual values are an
    /// art-direction call.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/UI Palette", fileName = "SO_UiPalette")]
    public sealed class UiPaletteSO : ScriptableObject
    {
        /// <summary>#FF8A3D. The interactable accent. Nothing else may use it.</summary>
        public static readonly Color32 LockedAccent = new Color32(0xFF, 0x8A, 0x3D, 0xFF);

        [Header("Interactable accent — do not change")]
        [Tooltip("#FF8A3D. Means 'interactable' and nothing else. Never use it decoratively.")]
        [SerializeField] private Color _accent = new Color(1f, 0.541f, 0.239f, 1f);

        [Header("Body palette (placeholders — art direction owns these)")]
        [SerializeField] private Color _mustard = new Color(0.78f, 0.62f, 0.16f, 1f);

        [SerializeField] private Color _oxideRed = new Color(0.50f, 0.20f, 0.16f, 1f);

        [SerializeField] private Color _olive = new Color(0.42f, 0.44f, 0.26f, 1f);

        [SerializeField] private Color _cream = new Color(0.93f, 0.90f, 0.82f, 1f);

        [SerializeField] private Color _oakVeneer = new Color(0.56f, 0.42f, 0.26f, 1f);

        [SerializeField] private Color _institutionalGreen = new Color(0.44f, 0.55f, 0.49f, 1f);

        [Header("States")]
        [Tooltip("Interactable but not currently usable. Never the accent.")]
        [SerializeField] private Color _dimmed = new Color(0.72f, 0.70f, 0.65f, 0.6f);

        public Color Accent => _accent;

        public Color Mustard => _mustard;

        public Color OxideRed => _oxideRed;

        public Color Olive => _olive;

        public Color Cream => _cream;

        public Color OakVeneer => _oakVeneer;

        public Color InstitutionalGreen => _institutionalGreen;

        public Color Dimmed => _dimmed;

        private void OnValidate()
        {
            // The accent is a constant, not a preference. If someone nudges it in the inspector,
            // put it back and say so rather than letting the project drift off the locked value.
            var current = (Color32)_accent;
            if (current.r == LockedAccent.r && current.g == LockedAccent.g && current.b == LockedAccent.b)
            {
                return;
            }

            Debug.LogWarning(
                "[Session] The interactable accent is locked to #FF8A3D and has been reset. " +
                "If it genuinely needs to change, change it in UiPaletteSO.LockedAccent and tell the art lead.");

            _accent = new Color(LockedAccent.r / 255f, LockedAccent.g / 255f, LockedAccent.b / 255f, 1f);
        }
    }
}
