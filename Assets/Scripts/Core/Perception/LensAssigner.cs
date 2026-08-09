using System;
using Session.Core.Determinism;
using Session.Core.Identity;
using Session.Core.Rooms;

namespace Session.Core.Perception
{
    /// <summary>
    /// Builds the per-player lenses for a room. Pure and deterministic: the same
    /// (room, seed, playerCount, rules) always produces byte-identical lenses, on any machine.
    /// That is what lets the server and every client derive the same perception locally instead of
    /// replicating a variant id per prop per player.
    ///
    /// The interdependence invariant — no player may be able to finish a room alone — is enforced
    /// by construction here, not by rejecting bad draws afterwards:
    ///
    ///   1. Take the room's required clues (the transitive inputs to the exit).
    ///   2. Shuffle them with the room's seeded stream.
    ///   3. Deal them round-robin, so every player owns at least one and every clue has an owner.
    ///
    /// With at least two players and at least as many required clues as players, every player is
    /// therefore missing at least one clue that another player holds, and the union covers the
    /// room. Redundant reveals are optional and individually guarded, so they can soften the
    /// partition but never dissolve it. <see cref="LensValidator"/> re-checks all of this
    /// independently; the tests run it over thousands of seeds.
    /// </summary>
    public static class LensAssigner
    {
        public static bool TryAssign(
            RoomDefinition room,
            ulong sessionSeed,
            int playerCount,
            ILensRules rules,
            out LensAssignment? assignment,
            out LensAssignmentFailure failure)
        {
            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            assignment = null;

            if (playerCount < rules.MinPlayers)
            {
                failure = LensAssignmentFailure.TooFewPlayers;
                return false;
            }

            if (playerCount > rules.MaxPlayers)
            {
                failure = LensAssignmentFailure.TooManyPlayers;
                return false;
            }

            ReadOnlySpan<ClueId> requiredClues = room.Puzzles.RequiredClues;
            if (requiredClues.Length < playerCount)
            {
                // Not a seed problem — the room is under-authored. Every player must have something
                // to say, or one of them is a spectator.
                failure = LensAssignmentFailure.NotEnoughRequiredClues;
                return false;
            }

            int propCount = room.PropCount;

            // Map each required clue to its source prop, and check the prop can actually both show
            // and withhold it. A clue on a prop with no concealing variant cannot be split.
            int[] requiredPropOrdinals = new int[requiredClues.Length];
            bool[] isRequiredProp = new bool[propCount];

            for (int i = 0; i < requiredClues.Length; i++)
            {
                if (!room.TryGetPropOrdinalForClue(requiredClues[i], out int ordinal))
                {
                    failure = LensAssignmentFailure.RequiredClueHasNoProp;
                    return false;
                }

                requiredPropOrdinals[i] = ordinal;
                isRequiredProp[ordinal] = true;
            }

            for (int ordinal = 0; ordinal < propCount; ordinal++)
            {
                PropDefinition prop = room.PropAt(ordinal);
                if (!prop.CarriesClue)
                {
                    continue;
                }

                if (prop.CountVariants(true) == 0)
                {
                    failure = LensAssignmentFailure.PropMissingRevealingVariant;
                    return false;
                }

                if (prop.CountVariants(false) == 0)
                {
                    failure = LensAssignmentFailure.PropMissingConcealingVariant;
                    return false;
                }
            }

            // --- Deal the required clues ------------------------------------------------------

            var roomRng = new Pcg32(SeedMixer.ForRoom(sessionSeed, room.Id));

            int[] dealOrder = new int[requiredClues.Length];
            for (int i = 0; i < dealOrder.Length; i++)
            {
                dealOrder[i] = i;
            }

            roomRng.Shuffle(dealOrder);

            bool[][] revealsProp = new bool[playerCount][];
            for (int p = 0; p < playerCount; p++)
            {
                revealsProp[p] = new bool[propCount];
            }

            int[] readableCount = new int[playerCount];

            for (int position = 0; position < dealOrder.Length; position++)
            {
                int clueIndex = dealOrder[position];
                int owner = position % playerCount;

                revealsProp[owner][requiredPropOrdinals[clueIndex]] = true;
                readableCount[owner]++;
            }

            // --- Optional redundancy ----------------------------------------------------------

            int redundancyPercent = rules.RedundantRevealPercent;
            if (redundancyPercent > 0)
            {
                int maxReadable = requiredClues.Length - 1; // never all of them

                for (int position = 0; position < dealOrder.Length; position++)
                {
                    int propOrdinal = requiredPropOrdinals[dealOrder[position]];

                    for (int p = 0; p < playerCount; p++)
                    {
                        // Roll for every player regardless of outcome so the stream advances
                        // identically no matter how the deal fell.
                        bool roll = roomRng.NextChancePercent(redundancyPercent);

                        if (!roll || revealsProp[p][propOrdinal] || readableCount[p] >= maxReadable)
                        {
                            continue;
                        }

                        revealsProp[p][propOrdinal] = true;
                        readableCount[p]++;
                    }
                }
            }

            // --- Choose variants --------------------------------------------------------------

            var lenses = new Lens[playerCount];

            for (int p = 0; p < playerCount; p++)
            {
                var player = new PlayerId(p);
                var playerRng = new Pcg32(SeedMixer.ForPlayer(sessionSeed, room.Id, player));

                byte[] variantIndices = new byte[propCount];
                bool[] reveals = new bool[propCount];

                for (int ordinal = 0; ordinal < propCount; ordinal++)
                {
                    PropDefinition prop = room.PropAt(ordinal);

                    if (!prop.CarriesClue)
                    {
                        // Set dressing. Every variant is fair game; the RevealsClue flag is
                        // meaningless without a clue to reveal.
                        int anyCount = prop.VariantCount;
                        variantIndices[ordinal] =
                            (byte)(anyCount == 1 ? 0 : (int)playerRng.NextUInt((uint)anyCount));
                        reveals[ordinal] = false;
                        continue;
                    }

                    bool reveal = isRequiredProp[ordinal]
                        ? revealsProp[p][ordinal]
                        // Flavour clue — carries story, not a puzzle input. Free to scatter.
                        : playerRng.NextChancePercent(50);

                    int candidateCount = prop.CountVariants(reveal);
                    int rank = candidateCount == 1 ? 0 : (int)playerRng.NextUInt((uint)candidateCount);

                    variantIndices[ordinal] = (byte)prop.VariantIndexByRank(reveal, rank);
                    reveals[ordinal] = reveal;
                }

                lenses[p] = new Lens(room.Id, player, variantIndices, reveals);
            }

            assignment = new LensAssignment(room.Id, sessionSeed, lenses);
            failure = LensAssignmentFailure.None;
            return true;
        }
    }
}
