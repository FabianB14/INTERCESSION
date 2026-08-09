using System;
using Session.Core.Identity;
using Session.Core.Rooms;

namespace Session.Core.Perception
{
    [Flags]
    public enum LensIssue
    {
        None = 0,

        /// <summary>
        /// A single lens exposes every required clue. This is the design-breaking failure: that
        /// player can finish the room without speaking to anyone, and the game stops being the game.
        /// </summary>
        SoloSolvable = 1 << 0,

        /// <summary>No player can read some required clue. The room is unfinishable.</summary>
        RequiredCluesUncovered = 1 << 1,

        /// <summary>
        /// A player holds none of the required clues. Not unwinnable, but that player is a
        /// passenger for the whole room, which is its own kind of failure.
        /// </summary>
        PlayerContributesNothing = 1 << 2,

        /// <summary>A lens points at a variant index the prop does not have.</summary>
        VariantOutOfRange = 1 << 3,

        /// <summary>A lens claims to reveal a clue while sitting on a concealing variant, or vice versa.</summary>
        RevealFlagInconsistent = 1 << 4,

        /// <summary>The assignment's player count or room does not match what was validated against.</summary>
        StructuralMismatch = 1 << 5
    }

    public readonly struct LensValidation
    {
        public readonly LensIssue Issues;

        /// <summary>Index of the first player the issue was found on, or -1 when it is not player-specific.</summary>
        public readonly int OffendingPlayerIndex;

        public LensValidation(LensIssue issues, int offendingPlayerIndex)
        {
            Issues = issues;
            OffendingPlayerIndex = offendingPlayerIndex;
        }

        public bool IsValid => Issues == LensIssue.None;

        public override string ToString()
        {
            return IsValid
                ? "Lens assignment valid."
                : "Lens assignment invalid: " + Issues + " (player " + OffendingPlayerIndex + ")";
        }
    }

    /// <summary>
    /// Independent check of a <see cref="LensAssignment"/>. Deliberately does not share code with
    /// <see cref="LensAssigner"/> — if it did, a bug in the dealing logic would validate itself.
    ///
    /// Run this in tests over thousands of seeds. It is also cheap enough to assert on the server
    /// in development builds every time a room is staged.
    /// </summary>
    public static class LensValidator
    {
        public static LensValidation Validate(RoomDefinition room, LensAssignment assignment)
        {
            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            if (assignment == null)
            {
                throw new ArgumentNullException(nameof(assignment));
            }

            if (assignment.Room != room.Id)
            {
                return new LensValidation(LensIssue.StructuralMismatch, -1);
            }

            LensIssue issues = LensIssue.None;
            int offender = -1;

            ReadOnlySpan<ClueId> required = room.Puzzles.RequiredClues;
            int playerCount = assignment.PlayerCount;

            Span<bool> covered = required.Length <= 64
                ? stackalloc bool[required.Length]
                : new bool[required.Length];

            for (int p = 0; p < playerCount; p++)
            {
                Lens lens = assignment.For(p);

                if (lens.PropCount != room.PropCount || lens.Room != room.Id)
                {
                    return new LensValidation(LensIssue.StructuralMismatch, p);
                }

                // Structural consistency: the stored reveal flag must agree with the chosen variant.
                for (int ordinal = 0; ordinal < room.PropCount; ordinal++)
                {
                    PropDefinition prop = room.PropAt(ordinal);
                    int variantIndex = lens.VariantIndex(ordinal);

                    if (variantIndex < 0 || variantIndex >= prop.VariantCount)
                    {
                        return new LensValidation(LensIssue.VariantOutOfRange, p);
                    }

                    bool variantReveals = prop.CarriesClue && prop.VariantAt(variantIndex).RevealsClue;
                    if (variantReveals != lens.RevealsClue(ordinal))
                    {
                        issues |= LensIssue.RevealFlagInconsistent;
                        if (offender < 0)
                        {
                            offender = p;
                        }
                    }
                }

                int readable = 0;
                for (int i = 0; i < required.Length; i++)
                {
                    if (!lens.CanRead(room, required[i]))
                    {
                        continue;
                    }

                    readable++;
                    covered[i] = true;
                }

                if (required.Length > 0 && readable == required.Length)
                {
                    issues |= LensIssue.SoloSolvable;
                    if (offender < 0)
                    {
                        offender = p;
                    }
                }

                if (required.Length > 0 && readable == 0)
                {
                    issues |= LensIssue.PlayerContributesNothing;
                    if (offender < 0)
                    {
                        offender = p;
                    }
                }
            }

            for (int i = 0; i < required.Length; i++)
            {
                if (covered[i])
                {
                    continue;
                }

                issues |= LensIssue.RequiredCluesUncovered;
                break;
            }

            return new LensValidation(issues, offender);
        }
    }
}
