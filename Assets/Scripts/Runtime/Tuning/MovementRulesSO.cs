using Session.Core.Movement;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// Server-side movement validation limits. These are anti-cheat bounds, not feel settings —
    /// how movement <i>feels</i> belongs to the character controller. Set these loose enough that
    /// no honest player on a bad connection is ever clamped.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Movement Rules", fileName = "SO_MovementRules")]
    public sealed class MovementRulesSO : ScriptableObject, IMovementRules
    {
        [Header("Speed ceilings")]
        [SerializeField, Min(0.1f)] private float _maxSpeedMetersPerSecond = 4.5f;

        [SerializeField, Min(1f)] private float _sprintMultiplier = 1.6f;

        [Tooltip("Falls and stairs. Generous — gravity is not something the player controls.")]
        [SerializeField, Min(0.1f)] private float _maxVerticalSpeedMetersPerSecond = 25f;

        [Header("Lag tolerance")]
        [Tooltip("Extra headroom on every check. Absorbs client/server frame time mismatch.")]
        [SerializeField, Range(0f, 1f)] private float _toleranceFraction = 0.15f;

        [Tooltip("Seconds of unused movement banked, so a stalled packet followed by a burst is not punished.")]
        [SerializeField, Min(0f)] private float _budgetGraceSeconds = 0.35f;

        [Tooltip("Past this much unexplained displacement in one update it is a teleport, not lag. Snap back.")]
        [SerializeField, Min(0.5f)] private float _teleportThresholdMeters = 8f;

        public float MaxSpeedMetersPerSecond => _maxSpeedMetersPerSecond;

        public float SprintMultiplier => _sprintMultiplier;

        public float ToleranceFraction => _toleranceFraction;

        public float BudgetGraceSeconds => _budgetGraceSeconds;

        public float TeleportThresholdMeters => _teleportThresholdMeters;

        public float MaxVerticalSpeedMetersPerSecond => _maxVerticalSpeedMetersPerSecond;
    }
}
