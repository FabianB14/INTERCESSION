using System;
using System.Collections.Generic;
using NUnit.Framework;
using Session.Core.Content;
using Session.Core.Identity;
using Session.Core.Tapes;

namespace Session.Tests.Core.Tapes
{
    internal static class TestTapes
    {
        internal static readonly int LineAKey = ContentKey.Of("tape.line.a");
        internal static readonly int LineBKey = ContentKey.Of("tape.line.b");
        internal static readonly int LeakKey = ContentKey.Of("tape.line.leak");
        internal static readonly int TitleKey = ContentKey.Of("tape.title");
        internal static readonly int SpeakerKey = ContentKey.Of("tape.speaker.verity");

        internal static ContentTable Content()
        {
            return new ContentTable(
                new List<int> { LineAKey, LineBKey, LeakKey, TitleKey, SpeakerKey },
                new List<string>
                {
                    "I'm sorry about the forms. They help me build the room properly.",
                    "You can leave whenever you've worked it out. That's the whole of it.",
                    "The ward code is 4-1-7-2, if anyone asks you at the desk.",
                    "Intake 03",
                    "Dr. Alan Verity"
                });
        }

        /// <summary>Ten seconds, two cues covering 0-4 and 5-9. Coverage 80%.</summary>
        internal static TapeDefinition Standard(bool includeLeak = false)
        {
            var cues = new[]
            {
                new TranscriptCue(0f, 4f, LineAKey),
                new TranscriptCue(5f, 9f, includeLeak ? LeakKey : LineBKey)
            };

            return new TapeDefinition(new TapeId(3), TitleKey, SpeakerKey, 1974, 10f, cues);
        }
    }

    public sealed class TapeDefinitionTests
    {
        [Test]
        public void CueLookupFindsTheSpokenLine()
        {
            TapeDefinition tape = TestTapes.Standard();

            Assert.AreEqual(0, tape.CueIndexAt(0f), "Start of the first cue.");
            Assert.AreEqual(0, tape.CueIndexAt(3.99f));
            Assert.AreEqual(1, tape.CueIndexAt(5f));
            Assert.AreEqual(1, tape.CueIndexAt(8.99f));
        }

        [Test]
        public void CueLookupReturnsMinusOneInGapsAndOutsideTheTape()
        {
            TapeDefinition tape = TestTapes.Standard();

            Assert.AreEqual(-1, tape.CueIndexAt(4.5f), "Gap between lines.");
            Assert.AreEqual(-1, tape.CueIndexAt(9.5f), "After the last line.");
            Assert.AreEqual(-1, tape.CueIndexAt(-1f));
        }

        [Test]
        public void CueEndIsExclusiveSoLinesNeverOverlap()
        {
            TapeDefinition tape = TestTapes.Standard();

            Assert.AreEqual(0, tape.CueIndexAt(3.999f));
            Assert.AreEqual(-1, tape.CueIndexAt(4f), "The end boundary belongs to the gap, not the cue.");
        }

        [Test]
        public void OverlappingCuesAreRejected()
        {
            var cues = new[]
            {
                new TranscriptCue(0f, 5f, TestTapes.LineAKey),
                new TranscriptCue(4f, 8f, TestTapes.LineBKey)
            };

            Assert.Throws<ArgumentException>(
                () => new TapeDefinition(new TapeId(1), 0, 0, 1974, 10f, cues));
        }

        [Test]
        public void CuesOutsideTheRuntimeAreRejected()
        {
            var cues = new[] { new TranscriptCue(0f, 20f, TestTapes.LineAKey) };

            Assert.Throws<ArgumentException>(
                () => new TapeDefinition(new TapeId(1), 0, 0, 1974, 10f, cues));
        }

        [Test]
        public void BackwardsCueIsRejected()
        {
            var cues = new[] { new TranscriptCue(6f, 3f, TestTapes.LineAKey) };

            Assert.Throws<ArgumentException>(
                () => new TapeDefinition(new TapeId(1), 0, 0, 1974, 10f, cues));
        }

        [Test]
        public void ZeroLengthTapeIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TapeDefinition(new TapeId(1), 0, 0, 1974, 0f, Array.Empty<TranscriptCue>()));
        }
    }

    public sealed class TapePlaybackStateTests
    {
        private static TapePlaybackState Loaded()
        {
            var state = new TapePlaybackState();
            state.Load(TestTapes.Standard());
            return state;
        }

        [Test]
        public void LoadsStoppedAtZero()
        {
            TapePlaybackState state = Loaded();

            Assert.AreEqual(TapeTransport.Stopped, state.Transport);
            Assert.AreEqual(0f, state.PositionSeconds);
            Assert.AreEqual(-1, state.CueIndex);
        }

        [Test]
        public void PlayingAdvancesThePositionAndCue()
        {
            TapePlaybackState state = Loaded();
            state.Play();

            Assert.AreEqual(0, state.CueIndex);

            state.Tick(6f);

            Assert.AreEqual(6f, state.PositionSeconds, 0.001f);
            Assert.AreEqual(1, state.CueIndex);
        }

        [Test]
        public void PausingResumesFromTheSamePlace()
        {
            // A tape is ninety seconds and the Attendant does not stop walking. If every
            // interruption cost the whole tape, players would learn never to start one.
            TapePlaybackState state = Loaded();
            state.Play();
            state.Tick(6f);
            state.Pause();

            Assert.AreEqual(6f, state.PositionSeconds, 0.001f);

            state.Tick(3f);
            Assert.AreEqual(6f, state.PositionSeconds, 0.001f, "A paused tape must not advance.");

            state.Play();
            state.Tick(1f);
            Assert.AreEqual(7f, state.PositionSeconds, 0.001f);
        }

        [Test]
        public void StoppingRewinds()
        {
            TapePlaybackState state = Loaded();
            state.Play();
            state.Tick(6f);
            state.Stop();

            Assert.AreEqual(0f, state.PositionSeconds);
            Assert.AreEqual(TapeTransport.Stopped, state.Transport);
            Assert.AreEqual(-1, state.CueIndex);
        }

        [Test]
        public void ReachingTheEndFinishesAndClamps()
        {
            TapePlaybackState state = Loaded();
            state.Play();
            state.Tick(30f);

            Assert.IsTrue(state.HasFinished);
            Assert.AreEqual(10f, state.PositionSeconds, 0.001f);
            Assert.IsFalse(state.IsPlaying);
            Assert.AreEqual(-1, state.CueIndex);
        }

        [Test]
        public void FinishedFiresExactlyOnce()
        {
            TapePlaybackState state = Loaded();
            int finishes = 0;
            state.Finished += () => finishes++;

            state.Play();
            state.Tick(30f);
            state.Tick(30f);

            Assert.AreEqual(1, finishes);
        }

        [Test]
        public void PlayingAFinishedTapeStartsItOver()
        {
            TapePlaybackState state = Loaded();
            state.Play();
            state.Tick(30f);

            state.Play();

            Assert.AreEqual(0f, state.PositionSeconds);
            Assert.IsTrue(state.IsPlaying);
            Assert.IsFalse(state.HasFinished);
        }

        [Test]
        public void SeekClampsToTheTape()
        {
            TapePlaybackState state = Loaded();

            state.Seek(-5f);
            Assert.AreEqual(0f, state.PositionSeconds);

            state.Seek(500f);
            Assert.AreEqual(10f, state.PositionSeconds, 0.001f);
        }

        [Test]
        public void CueChangedFiresOnlyWhenTheLineActuallyChanges()
        {
            TapePlaybackState state = Loaded();
            int changes = 0;
            state.CueChanged += _ => changes++;

            state.Play();      // -1 -> 0
            state.Tick(1f);    // still cue 0
            state.Tick(1f);    // still cue 0
            state.Tick(2.5f);  // 4.5s, in the gap: 0 -> -1
            state.Tick(1f);    // 5.5s, cue 1: -1 -> 1

            Assert.AreEqual(3, changes);
        }

        [Test]
        public void SmallDriftDoesNotResync()
        {
            // Snapping on every update would make a tape stutter continuously over a relay.
            TapePlaybackState state = Loaded();
            state.Play();
            state.Tick(5f);

            bool changed = state.SyncTo(5.1f, TapeTransport.Playing);

            Assert.IsFalse(changed);
            Assert.AreEqual(5f, state.PositionSeconds, 0.001f);
        }

        [Test]
        public void LargeDriftResyncs()
        {
            TapePlaybackState state = Loaded();
            state.Play();
            state.Tick(5f);

            bool changed = state.SyncTo(8f, TapeTransport.Playing);

            Assert.IsTrue(changed);
            Assert.AreEqual(8f, state.PositionSeconds, 0.001f);
            Assert.AreEqual(1, state.CueIndex);
        }

        [Test]
        public void TransportChangeAlwaysSyncsEvenWithoutDrift()
        {
            TapePlaybackState state = Loaded();
            state.Play();
            state.Tick(5f);

            Assert.IsTrue(state.SyncTo(5f, TapeTransport.Paused));
            Assert.AreEqual(TapeTransport.Paused, state.Transport);
        }

        [Test]
        public void ShouldResyncUsesTheTolerance()
        {
            Assert.IsFalse(TapePlaybackState.ShouldResync(5f, 5.1f, 0.2f));
            Assert.IsTrue(TapePlaybackState.ShouldResync(5f, 5.3f, 0.2f));
            Assert.IsTrue(TapePlaybackState.ShouldResync(5.3f, 5f, 0.2f), "Drift in either direction counts.");
        }

        [Test]
        public void UnloadedDeckIsInert()
        {
            var state = new TapePlaybackState();

            Assert.IsFalse(state.Play());
            Assert.IsFalse(state.Pause());
            Assert.IsFalse(state.Stop());
            Assert.IsFalse(state.Seek(5f));

            state.Tick(10f);
            Assert.AreEqual(0f, state.PositionSeconds);
        }
    }

    public sealed class TapeLibraryTests
    {
        [Test]
        public void FindingATapeCountsOnce()
        {
            var library = new TapeLibrary();

            Assert.IsTrue(library.MarkFound(new TapeId(1)));
            Assert.IsFalse(library.MarkFound(new TapeId(1)));
            Assert.AreEqual(1, library.FoundCount);
            Assert.AreEqual(0, library.HeardCount);
        }

        [Test]
        public void HearingATapeImpliesFindingIt()
        {
            // A deck someone else pressed play on across the room still counts as found. A run that
            // recorded "heard" without "found" would report nonsense.
            var library = new TapeLibrary();

            Assert.IsTrue(library.MarkHeard(new TapeId(2)));
            Assert.IsTrue(library.IsFound(new TapeId(2)));
            Assert.AreEqual(1, library.FoundCount);
            Assert.AreEqual(1, library.HeardCount);
        }

        [Test]
        public void FindingThenHearingDoesNotDoubleCountFound()
        {
            var library = new TapeLibrary();

            library.MarkFound(new TapeId(3));
            library.MarkHeard(new TapeId(3));

            Assert.AreEqual(1, library.FoundCount);
            Assert.AreEqual(1, library.HeardCount);
        }

        [Test]
        public void FoundAndHeardAreTrackedSeparately()
        {
            var library = new TapeLibrary();

            library.MarkFound(new TapeId(1));
            library.MarkFound(new TapeId(2));
            library.MarkHeard(new TapeId(1));

            Assert.AreEqual(2, library.FoundCount);
            Assert.AreEqual(1, library.HeardCount);
            Assert.IsTrue(library.IsHeard(new TapeId(1)));
            Assert.IsFalse(library.IsHeard(new TapeId(2)));
        }

        [Test]
        public void NoneIsIgnored()
        {
            var library = new TapeLibrary();

            Assert.IsFalse(library.MarkFound(TapeId.None));
            Assert.IsFalse(library.MarkHeard(TapeId.None));
            Assert.AreEqual(0, library.FoundCount);
        }
    }

    public sealed class TapeAuditTests
    {
        private static readonly int[] Solution = { 4, 1, 7, 2 };

        [Test]
        public void CleanTapePasses()
        {
            TapeAuditResult result = TapeAudit.Audit(TestTapes.Standard(), TestTapes.Content(), Solution);

            Assert.IsTrue(result.IsClean, result.ToString());
        }

        [Test]
        public void AnAnswerSpokenOnTapeIsCaught()
        {
            // The whole group hears a tape identically, so this does not leak a clue to one
            // player -- it removes the room's need for co-operation entirely.
            TapeAuditResult result = TapeAudit.Audit(TestTapes.Standard(true), TestTapes.Content(), Solution);

            Assert.AreNotEqual(0, result.Issues & TapeIssue.AnswerSpokenOnTape);
            Assert.AreEqual(1, result.CueIndex);
        }

        [Test]
        public void UnrelatedSolutionsDoNotFlag()
        {
            TapeAuditResult result = TapeAudit.Audit(TestTapes.Standard(true), TestTapes.Content(), new[] { 9, 9, 9, 9 });

            Assert.AreEqual(0, result.Issues & TapeIssue.AnswerSpokenOnTape);
        }

        [Test]
        public void MissingTranscriptCopyIsCaught()
        {
            var cues = new[] { new TranscriptCue(0f, 9f, ContentKey.Of("tape.never.authored")) };
            var tape = new TapeDefinition(new TapeId(5), 0, 0, 1974, 10f, cues);

            TapeAuditResult result = TapeAudit.Audit(tape, TestTapes.Content(), Solution);

            Assert.AreNotEqual(0, result.Issues & TapeIssue.MissingTranscriptCopy);
        }

        [Test]
        public void TapeWithNoTranscriptIsCaught()
        {
            var tape = new TapeDefinition(new TapeId(6), 0, 0, 1974, 10f, Array.Empty<TranscriptCue>());

            TapeAuditResult result = TapeAudit.Audit(tape, TestTapes.Content(), Solution);

            Assert.AreNotEqual(0, result.Issues & TapeIssue.NoTranscript);
        }

        [Test]
        public void LowTranscriptCoverageIsCaught()
        {
            // Timings that stop partway through: two seconds subtitled out of thirty.
            var cues = new[] { new TranscriptCue(0f, 2f, TestTapes.LineAKey) };
            var tape = new TapeDefinition(new TapeId(7), 0, 0, 1974, 30f, cues);

            TapeAuditResult result = TapeAudit.Audit(tape, TestTapes.Content(), Solution);

            Assert.AreNotEqual(0, result.Issues & TapeIssue.TranscriptCoverageLow);
            Assert.Less(result.TranscriptCoverage, 0.1f);
        }

        [Test]
        public void GoodCoverageIsNotFlagged()
        {
            TapeAuditResult result = TapeAudit.Audit(TestTapes.Standard(), TestTapes.Content(), Solution);

            Assert.AreEqual(0, result.Issues & TapeIssue.TranscriptCoverageLow);
            Assert.AreEqual(0.8f, result.TranscriptCoverage, 0.001f);
        }
    }
}
