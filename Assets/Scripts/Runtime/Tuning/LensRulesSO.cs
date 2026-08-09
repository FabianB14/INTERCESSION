using Session.Core.Perception;
using UnityEngine;

namespace Session.Runtime.Tuning
{
    /// <summary>
    /// Tuning for how perception is split between players.
    /// </summary>
    [CreateAssetMenu(menuName = "Session/Lens Rules", fileName = "SO_LensRules")]
    public sealed class LensRulesSO : ScriptableObject, ILensRules
    {
        [Header("Group size")]
        [SerializeField, Min(2)] private int _minPlayers = 2;

        [SerializeField, Min(2)] private int _maxPlayers = 4;

        [Header("Redundancy")]
        [Tooltip(
            "Chance a clue is legible to a second player as well as its owner. " +
            "Zero is a strict partition: exactly one player can read each clue. " +
            "Raise it to soften the case where someone disconnects mid-room. " +
            "Every grant is checked against the solo-solve invariant before it is committed, so " +
            "turning this up can only make rooms easier — it can never break them.")]
        [SerializeField, Range(0, 100)] private int _redundantRevealPercent;

        public int MinPlayers => _minPlayers;

        public int MaxPlayers => _maxPlayers;

        public int RedundantRevealPercent => _redundantRevealPercent;

        private void OnValidate()
        {
            if (_maxPlayers < _minPlayers)
            {
                _maxPlayers = _minPlayers;
            }
        }
    }
}
