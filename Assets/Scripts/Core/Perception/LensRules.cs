namespace Session.Core.Perception
{
    /// <summary>
    /// Tuning for lens assignment. Implemented by LensRulesSO in Session.Runtime so these can be
    /// changed without a recompile; Core only ever sees the interface, which is what keeps this
    /// assembly free of UnityEngine.
    /// </summary>
    public interface ILensRules
    {
        int MinPlayers { get; }

        int MaxPlayers { get; }

        /// <summary>
        /// Chance, 0..100, that a clue is legible to a second player as well as its owner.
        ///
        /// Zero means a strict partition: exactly one player can read each clue. Raise it to soften
        /// the failure case where one player disconnects mid-room. Every redundant grant is checked
        /// against the solo-solve invariant before it is committed, so this can never be turned up
        /// high enough to break the game — at worst it makes rooms easier.
        /// </summary>
        int RedundantRevealPercent { get; }
    }

    /// <summary>Defaults used by tests and as the fallback when no SO is wired yet.</summary>
    public sealed class DefaultLensRules : ILensRules
    {
        public static readonly DefaultLensRules Instance = new DefaultLensRules();

        public int MinPlayers => 2;

        public int MaxPlayers => 4;

        public int RedundantRevealPercent => 0;
    }
}
