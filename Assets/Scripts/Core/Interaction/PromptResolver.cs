using Session.Core.Identity;

namespace Session.Core.Interaction
{
    public enum InteractionVerb
    {
        None = 0,

        /// <summary>Look closer. The default for anything with detail worth reading.</summary>
        Examine = 1,

        /// <summary>Operate it — a dial, a switch, a keypad.</summary>
        Use = 2,

        /// <summary>Read a document, label, or stencil.</summary>
        Read = 3,

        /// <summary>A door, drawer, or cabinet.</summary>
        Open = 4
    }

    /// <summary>What the player is currently looking at, as far as the view layer can tell.</summary>
    public readonly struct InteractionCandidate
    {
        public static readonly InteractionCandidate None = default;

        public readonly PropId Prop;

        /// <summary>Content key for the name this player's lens gives the prop.</summary>
        public readonly int DisplayNameKey;

        public readonly InteractionVerb Verb;

        /// <summary>Authored on the prop: can a player do anything with this at all?</summary>
        public readonly bool IsInteractable;

        /// <summary>Within arm's reach. Looking at something across the room is not a prompt.</summary>
        public readonly bool WithinReach;

        /// <summary>Server says this is currently usable — an unlocked keypad, an openable door.</summary>
        public readonly bool IsEnabled;

        public InteractionCandidate(
            PropId prop,
            int displayNameKey,
            InteractionVerb verb,
            bool isInteractable,
            bool withinReach,
            bool isEnabled)
        {
            Prop = prop;
            DisplayNameKey = displayNameKey;
            Verb = verb;
            IsInteractable = isInteractable;
            WithinReach = withinReach;
            IsEnabled = isEnabled;
        }

        public bool IsNone => Prop.IsNone;
    }

    /// <summary>The prompt to draw. Produced by <see cref="PromptResolver"/>, consumed by Session.UI.</summary>
    public readonly struct InteractionPrompt
    {
        public static readonly InteractionPrompt Hidden = default;

        public readonly bool Visible;
        public readonly PropId Prop;
        public readonly int SubjectNameKey;
        public readonly InteractionVerb Verb;

        /// <summary>
        /// Whether to draw in the accent colour. See the invariant on <see cref="PromptResolver"/> —
        /// this is true if and only if the player can actually act on the thing.
        /// </summary>
        public readonly bool UseAccentColour;

        /// <summary>Visible but not actionable: shown dimmed, no accent. "The door is locked."</summary>
        public readonly bool IsDimmed;

        public InteractionPrompt(
            bool visible, PropId prop, int subjectNameKey, InteractionVerb verb, bool useAccentColour, bool isDimmed)
        {
            Visible = visible;
            Prop = prop;
            SubjectNameKey = subjectNameKey;
            Verb = verb;
            UseAccentColour = useAccentColour;
            IsDimmed = isDimmed;
        }
    }

    /// <summary>
    /// Decides whether to show an interaction prompt, and in what colour.
    ///
    /// This is small enough to look like it belongs in a MonoBehaviour, and it does not, because it
    /// encodes an art constraint the project treats as load-bearing:
    ///
    ///   <b>#FF8A3D means "you can interact with this" and is never decorative.</b>
    ///
    /// Putting that rule in one pure function means it is tested rather than remembered, and the
    /// UI layer physically cannot draw an accent-coloured prompt on something inert — it gets a
    /// bool from here and has no other source for it. Session.Editor's accent validator covers the
    /// other half: materials and prefabs that use the colour decoratively.
    /// </summary>
    public static class PromptResolver
    {
        public static InteractionPrompt Resolve(in InteractionCandidate candidate)
        {
            if (candidate.IsNone || !candidate.IsInteractable || !candidate.WithinReach)
            {
                return InteractionPrompt.Hidden;
            }

            // Interactable but currently unusable — a keypad whose prerequisites are unsolved, a
            // door that has not opened. Say so, but do not promise anything with the accent colour.
            if (!candidate.IsEnabled)
            {
                return new InteractionPrompt(
                    visible: true,
                    prop: candidate.Prop,
                    subjectNameKey: candidate.DisplayNameKey,
                    verb: InteractionVerb.None,
                    useAccentColour: false,
                    isDimmed: true);
            }

            return new InteractionPrompt(
                visible: true,
                prop: candidate.Prop,
                subjectNameKey: candidate.DisplayNameKey,
                verb: candidate.Verb == InteractionVerb.None ? InteractionVerb.Examine : candidate.Verb,
                useAccentColour: true,
                isDimmed: false);
        }
    }
}
