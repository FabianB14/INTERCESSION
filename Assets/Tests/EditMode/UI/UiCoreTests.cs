using System;
using NUnit.Framework;
using Session.Core.Content;
using Session.Core.Identity;
using Session.Core.Interaction;
using Session.Core.Lobby;
using Session.Core.Text;

namespace Session.Tests.Core.UI
{
    public sealed class TextWriteBufferTests
    {
        private static TextWriteBuffer Fresh(int capacity = 64) => new TextWriteBuffer(new char[capacity]);

        [Test]
        public void AppendsStringsAndChars()
        {
            TextWriteBuffer buffer = Fresh();
            buffer.Append("Room");
            buffer.Append(' ');
            buffer.Append("9");

            Assert.AreEqual("Room 9", buffer.ToString());
        }

        [Test]
        public void AppendsIntegersWithoutToString()
        {
            TextWriteBuffer buffer = Fresh();
            buffer.Append(0);
            buffer.Append(' ');
            buffer.Append(41);
            buffer.Append(' ');
            buffer.Append(-1971);

            Assert.AreEqual("0 41 -1971", buffer.ToString());
        }

        [Test]
        public void HandlesIntMinValue()
        {
            // The classic off-by-one: negating int.MinValue overflows. Accumulating on the negative
            // side avoids it, and this is the test that proves it.
            TextWriteBuffer buffer = Fresh();
            buffer.Append(int.MinValue);

            Assert.AreEqual("-2147483648", buffer.ToString());
        }

        [Test]
        public void PadsToWidth()
        {
            TextWriteBuffer buffer = Fresh();
            buffer.AppendPadded(7, 3);
            buffer.Append('|');
            buffer.AppendPadded(1234, 2);

            Assert.AreEqual("007|1234", buffer.ToString());
        }

        [Test]
        public void FormatsDurations()
        {
            TextWriteBuffer buffer = Fresh();
            buffer.AppendDuration(65f);
            Assert.AreEqual("1:05", buffer.ToString());

            buffer.Clear();
            buffer.AppendDuration(3725f);
            Assert.AreEqual("1:02:05", buffer.ToString());

            buffer.Clear();
            buffer.AppendDuration(0f);
            Assert.AreEqual("0:00", buffer.ToString());
        }

        [Test]
        public void NegativeDurationClampsToZero()
        {
            // A room's remaining allowance goes negative the instant it is overrun. "-1:-5" on the
            // HUD would be worse than useless.
            TextWriteBuffer buffer = Fresh();
            buffer.AppendDuration(-42f);

            Assert.AreEqual("0:00", buffer.ToString());
        }

        [Test]
        public void OverflowTruncatesInsteadOfThrowing()
        {
            TextWriteBuffer buffer = Fresh(4);

            Assert.IsTrue(buffer.Append("abcd"));
            Assert.IsFalse(buffer.Append("e"), "Should report failure, not throw.");
            Assert.AreEqual("abcd", buffer.ToString());
        }

        [Test]
        public void IntegerAppendDoesNotPartiallyWriteOnOverflow()
        {
            TextWriteBuffer buffer = Fresh(4);
            buffer.Append("ab");

            Assert.IsFalse(buffer.Append(12345));
            Assert.AreEqual("ab", buffer.ToString(), "A failed append must leave the buffer untouched.");
        }

        [Test]
        public void DiffersFromDetectsChange()
        {
            TextWriteBuffer buffer = Fresh();
            buffer.Append("Room 9");

            var same = "Room 9".ToCharArray();
            var different = "Room 17".ToCharArray();

            Assert.IsFalse(buffer.DiffersFrom(same, same.Length));
            Assert.IsTrue(buffer.DiffersFrom(different, different.Length));
            Assert.IsTrue(buffer.DiffersFrom(same, 3));
        }

        [Test]
        public void ClearResets()
        {
            TextWriteBuffer buffer = Fresh();
            buffer.Append("something");
            buffer.Clear();

            Assert.AreEqual(0, buffer.Length);
            Assert.IsTrue(buffer.IsEmpty);
        }
    }

    public sealed class ContentTableTests
    {
        [Test]
        public void ResolvesAuthoredKeys()
        {
            int key = ContentKey.Of("prop.curtain.hospital");
            var table = new ContentTable(new[] { key }, new[] { "privacy curtain" });

            Assert.AreEqual("privacy curtain", table.Get(key));
        }

        [Test]
        public void MissingKeyIsVisibleNotSilent()
        {
            var table = new ContentTable(Array.Empty<int>(), Array.Empty<string>());

            Assert.AreEqual(ContentTable.MissingPlaceholder, table.Get(ContentKey.Of("nope")));
            Assert.AreEqual(1, table.MissCount);
        }

        [Test]
        public void NoneKeyIsEmptyAndNotAMiss()
        {
            var table = new ContentTable(Array.Empty<int>(), Array.Empty<string>());

            Assert.AreEqual(string.Empty, table.Get(ContentKey.None));
            Assert.AreEqual(0, table.MissCount, "A deliberately absent key is not a missing-copy bug.");
        }

        [Test]
        public void ContentKeyIsStableAndNonZero()
        {
            Assert.AreEqual(ContentKey.Of("ui.verb.examine"), ContentKey.Of("ui.verb.examine"));
            Assert.AreNotEqual(ContentKey.Of("ui.verb.examine"), ContentKey.Of("ui.verb.use"));
            Assert.AreEqual(ContentKey.None, ContentKey.Of(null));
            Assert.AreEqual(ContentKey.None, ContentKey.Of(""));
            Assert.Greater(ContentKey.Of("a"), 0);
        }

        [Test]
        public void MismatchedTableLengthsAreRejected()
        {
            Assert.Throws<ArgumentException>(
                () => new ContentTable(new[] { 1, 2 }, new[] { "only one" }));
        }
    }

    public sealed class PromptResolverTests
    {
        private static InteractionCandidate Candidate(
            bool interactable = true, bool withinReach = true, bool enabled = true,
            InteractionVerb verb = InteractionVerb.Use)
        {
            return new InteractionCandidate(
                new PropId(1), ContentKey.Of("prop.keypad"), verb, interactable, withinReach, enabled);
        }

        [Test]
        public void NothingFocusedShowsNothing()
        {
            Assert.IsFalse(PromptResolver.Resolve(InteractionCandidate.None).Visible);
        }

        [Test]
        public void NonInteractablePropsNeverPrompt()
        {
            Assert.IsFalse(PromptResolver.Resolve(Candidate(interactable: false)).Visible);
        }

        [Test]
        public void OutOfReachNeverPrompts()
        {
            Assert.IsFalse(PromptResolver.Resolve(Candidate(withinReach: false)).Visible);
        }

        [Test]
        public void AccentIsUsedExactlyWhenTheThingIsActionable()
        {
            // The art rule, as a test: #FF8A3D means "you can interact with this" and nothing else.
            Assert.IsTrue(PromptResolver.Resolve(Candidate()).UseAccentColour);

            Assert.IsFalse(PromptResolver.Resolve(Candidate(enabled: false)).UseAccentColour);
            Assert.IsFalse(PromptResolver.Resolve(Candidate(interactable: false)).UseAccentColour);
            Assert.IsFalse(PromptResolver.Resolve(Candidate(withinReach: false)).UseAccentColour);
        }

        [Test]
        public void DisabledInteractableIsShownDimmed()
        {
            InteractionPrompt prompt = PromptResolver.Resolve(Candidate(enabled: false));

            Assert.IsTrue(prompt.Visible, "A locked keypad should still be identified.");
            Assert.IsTrue(prompt.IsDimmed);
            Assert.AreEqual(InteractionVerb.None, prompt.Verb, "Do not offer a verb for something that will not respond.");
        }

        [Test]
        public void MissingVerbFallsBackToExamine()
        {
            InteractionPrompt prompt = PromptResolver.Resolve(Candidate(verb: InteractionVerb.None));

            Assert.AreEqual(InteractionVerb.Examine, prompt.Verb);
        }

        [Test]
        public void AuthoredVerbIsPreserved()
        {
            Assert.AreEqual(
                InteractionVerb.Read,
                PromptResolver.Resolve(Candidate(verb: InteractionVerb.Read)).Verb);
        }
    }

    public sealed class PuzzleInputBufferTests
    {
        [Test]
        public void AccumulatesUpToCapacity()
        {
            var input = new PuzzleInputBuffer(4);

            Assert.IsTrue(input.Push(4));
            Assert.IsTrue(input.Push(1));
            Assert.IsTrue(input.Push(7));
            Assert.IsTrue(input.Push(2));
            Assert.IsTrue(input.IsFull);
            Assert.IsFalse(input.Push(9), "A full keypad must reject, not overwrite.");
            Assert.AreEqual(4, input.Count);
        }

        [Test]
        public void BackspaceRemovesTheLastToken()
        {
            var input = new PuzzleInputBuffer(4);
            input.Push(4);
            input.Push(1);

            Assert.IsTrue(input.Backspace());
            Assert.AreEqual(1, input.Count);
            Assert.AreEqual(4, input.TokenAt(0));

            input.Backspace();
            Assert.IsFalse(input.Backspace(), "Backspace on an empty buffer is a no-op, not an error.");
        }

        [Test]
        public void CommitCopiesAndClears()
        {
            var input = new PuzzleInputBuffer(4);
            input.Push(4);
            input.Push(1);
            input.Push(7);
            input.Push(2);

            Span<int> destination = stackalloc int[4];
            int count = input.Commit(destination);

            Assert.AreEqual(4, count);
            Assert.AreEqual(4, destination[0]);
            Assert.AreEqual(2, destination[3]);
            Assert.IsTrue(input.IsEmpty, "Leaving a wrong code on the display is a real play-test bug.");
        }

        [Test]
        public void ChangedFiresOnEveryMutation()
        {
            var input = new PuzzleInputBuffer(4);
            int changes = 0;
            input.Changed += () => changes++;

            input.Push(1);      // 1
            input.Backspace();  // 2
            input.Push(2);      // 3
            input.Clear();      // 4
            input.Clear();      // already empty, no event

            Assert.AreEqual(4, changes);
        }

        [Test]
        public void AbsurdCapacityIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleInputBuffer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleInputBuffer(64));
        }
    }

    public sealed class LobbyRosterTests
    {
        [Test]
        public void PlayersTakeTheFirstFreeSlot()
        {
            var roster = new LobbyRoster();

            Assert.IsTrue(roster.TryAdd(100UL, out int first));
            Assert.IsTrue(roster.TryAdd(200UL, out int second));

            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second);
            Assert.AreEqual(2, roster.Count);
        }

        [Test]
        public void RejoiningKeepsTheSameSlot()
        {
            var roster = new LobbyRoster();
            roster.TryAdd(100UL, out int first);
            roster.TryAdd(100UL, out int again);

            Assert.AreEqual(first, again);
            Assert.AreEqual(1, roster.Count, "A rejoin must not consume a second slot.");
        }

        [Test]
        public void FullLobbyRejectsNewcomers()
        {
            var roster = new LobbyRoster(maxPlayers: 2);
            roster.TryAdd(1UL, out _);
            roster.TryAdd(2UL, out _);

            Assert.IsFalse(roster.TryAdd(3UL, out int slot));
            Assert.AreEqual(-1, slot);
        }

        [Test]
        public void CannotStartBelowTheMinimumEvenWhenEveryoneIsReady()
        {
            // A solo run is not hard mode. Rooms are authored so every player holds a clue nobody
            // else does, so one player is an unsolvable room.
            var roster = new LobbyRoster();
            roster.TryAdd(1UL, out int slot);
            roster.SetReady(slot, true);

            Assert.IsFalse(roster.CanStart);
        }

        [Test]
        public void CannotStartUntilEveryoneIsReady()
        {
            var roster = new LobbyRoster();
            roster.TryAdd(1UL, out int a);
            roster.TryAdd(2UL, out int b);

            roster.SetReady(a, true);
            Assert.IsFalse(roster.CanStart);

            roster.SetReady(b, true);
            Assert.IsTrue(roster.CanStart);
        }

        [Test]
        public void UnreadyingBlocksTheStartAgain()
        {
            var roster = new LobbyRoster();
            roster.TryAdd(1UL, out int a);
            roster.TryAdd(2UL, out int b);
            roster.SetReady(a, true);
            roster.SetReady(b, true);

            roster.SetReady(b, false);

            Assert.IsFalse(roster.CanStart);
        }

        [Test]
        public void LeavingFreesTheSlotAndCanBlockTheStart()
        {
            var roster = new LobbyRoster();
            roster.TryAdd(1UL, out int a);
            roster.TryAdd(2UL, out int b);
            roster.SetReady(a, true);
            roster.SetReady(b, true);
            Assert.IsTrue(roster.CanStart);

            roster.Remove(b);

            Assert.AreEqual(1, roster.Count);
            Assert.IsFalse(roster.CanStart);
        }

        [Test]
        public void ChangedFiresOnMembershipAndReadyChanges()
        {
            var roster = new LobbyRoster();
            int changes = 0;
            roster.Changed += () => changes++;

            roster.TryAdd(1UL, out int slot);   // 1
            roster.SetReady(slot, true);        // 2
            roster.SetReady(slot, true);        // no change
            roster.Remove(slot);                // 3
            roster.Remove(slot);                // already gone

            Assert.AreEqual(3, changes);
        }

        [Test]
        public void SoloLobbyConfigurationIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LobbyRoster(maxPlayers: 4, minPlayers: 1));
        }

        [Test]
        public void OutOfRangeSlotIsRejected()
        {
            var roster = new LobbyRoster();

            Assert.Throws<ArgumentOutOfRangeException>(() => roster.SetReady(9, true));
        }
    }
}
