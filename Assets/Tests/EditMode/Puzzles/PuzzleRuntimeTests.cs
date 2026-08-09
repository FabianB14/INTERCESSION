using System;
using NUnit.Framework;
using Session.Core.Identity;
using Session.Core.Puzzles;

namespace Session.Tests.Core.Puzzles
{
    public sealed class PuzzleGraphTests
    {
        [Test]
        public void RequiredCluesAreGatheredTransitively()
        {
            var first = new PuzzleNode(
                new PuzzleNodeId(1),
                new Solution(SolutionKind.Ordered, 1),
                requiredClues: new[] { new ClueId(10), new ClueId(11) });

            var exit = new PuzzleNode(
                new PuzzleNodeId(2),
                new Solution(SolutionKind.Ordered, 2),
                requiredClues: new[] { new ClueId(12) },
                requiredNodes: new[] { new PuzzleNodeId(1) },
                isRoomExit: true);

            var graph = new PuzzleGraph(new[] { first, exit });

            Assert.AreEqual(3, graph.RequiredClueCount);
            CollectionAssert.AreEqual(
                new[] { new ClueId(10), new ClueId(11), new ClueId(12) },
                graph.RequiredClues.ToArray());
        }

        [Test]
        public void CluesOnlyReachableFromDeadEndsAreNotRequired()
        {
            // A node nothing depends on is optional content. Its clues must not be forced into the
            // lens split, or rooms get harder than they were authored to be.
            var optional = new PuzzleNode(
                new PuzzleNodeId(1),
                new Solution(SolutionKind.Ordered, 1),
                requiredClues: new[] { new ClueId(99) });

            var exit = new PuzzleNode(
                new PuzzleNodeId(2),
                new Solution(SolutionKind.Ordered, 2),
                requiredClues: new[] { new ClueId(10) },
                requiredNodes: null,
                isRoomExit: true);

            var graph = new PuzzleGraph(new[] { optional, exit });

            Assert.AreEqual(1, graph.RequiredClueCount);
            Assert.AreEqual(new ClueId(10), graph.RequiredClues[0]);
        }

        [Test]
        public void CyclesAreRejected()
        {
            var a = new PuzzleNode(
                new PuzzleNodeId(1),
                new Solution(SolutionKind.Ordered, 1),
                requiredNodes: new[] { new PuzzleNodeId(2) },
                isRoomExit: true);

            var b = new PuzzleNode(
                new PuzzleNodeId(2),
                new Solution(SolutionKind.Ordered, 2),
                requiredNodes: new[] { new PuzzleNodeId(1) });

            Assert.Throws<ArgumentException>(() => new PuzzleGraph(new[] { a, b }));
        }

        [Test]
        public void GraphWithoutAnExitIsRejected()
        {
            var orphan = new PuzzleNode(new PuzzleNodeId(1), new Solution(SolutionKind.Ordered, 1));

            Assert.Throws<ArgumentException>(() => new PuzzleGraph(new[] { orphan }));
        }

        [Test]
        public void DanglingPrerequisiteIsRejected()
        {
            var exit = new PuzzleNode(
                new PuzzleNodeId(1),
                new Solution(SolutionKind.Ordered, 1),
                requiredNodes: new[] { new PuzzleNodeId(404) },
                isRoomExit: true);

            Assert.Throws<ArgumentException>(() => new PuzzleGraph(new[] { exit }));
        }
    }

    public sealed class PuzzleRuntimeTests
    {
        private static readonly PlayerId Actor = new PlayerId(0);

        private static PuzzleGraph TwoStageGraph()
        {
            var first = new PuzzleNode(new PuzzleNodeId(1), new Solution(SolutionKind.Ordered, 3, 9));

            var exit = new PuzzleNode(
                new PuzzleNodeId(2),
                new Solution(SolutionKind.Ordered, 4, 1, 7, 2),
                requiredClues: null,
                requiredNodes: new[] { new PuzzleNodeId(1) },
                isRoomExit: true);

            return new PuzzleGraph(new[] { first, exit });
        }

        [Test]
        public void CorrectTokensSolveTheNode()
        {
            var runtime = new PuzzleRuntime(TwoStageGraph());

            Assert.AreEqual(
                AttemptOutcome.Accepted,
                runtime.Submit(Actor, new PuzzleNodeId(1), stackalloc int[] { 3, 9 }));
            Assert.IsTrue(runtime.IsSolved(new PuzzleNodeId(1)));
        }

        [Test]
        public void WrongTokensChangeNothing()
        {
            var runtime = new PuzzleRuntime(TwoStageGraph());

            Assert.AreEqual(
                AttemptOutcome.Rejected,
                runtime.Submit(Actor, new PuzzleNodeId(1), stackalloc int[] { 9, 3 }));
            Assert.IsFalse(runtime.IsSolved(new PuzzleNodeId(1)));
            Assert.AreEqual(0, runtime.SolvedCount);
        }

        [Test]
        public void LockedNodeRejectsEvenWithTheCorrectAnswer()
        {
            // A client that has somehow learned the exit code still cannot skip the first stage.
            var runtime = new PuzzleRuntime(TwoStageGraph());

            Assert.AreEqual(
                AttemptOutcome.Locked,
                runtime.Submit(Actor, new PuzzleNodeId(2), stackalloc int[] { 4, 1, 7, 2 }));
            Assert.IsFalse(runtime.IsComplete);
        }

        [Test]
        public void RoomCompletesOnlyWhenTheExitIsSolved()
        {
            var runtime = new PuzzleRuntime(TwoStageGraph());

            runtime.Submit(Actor, new PuzzleNodeId(1), stackalloc int[] { 3, 9 });
            Assert.IsFalse(runtime.IsComplete, "First stage should not open the door.");

            runtime.Submit(Actor, new PuzzleNodeId(2), stackalloc int[] { 4, 1, 7, 2 });
            Assert.IsTrue(runtime.IsComplete);
        }

        [Test]
        public void ResubmittingASolvedNodeIsIdempotent()
        {
            var runtime = new PuzzleRuntime(TwoStageGraph());

            runtime.Submit(Actor, new PuzzleNodeId(1), stackalloc int[] { 3, 9 });

            Assert.AreEqual(
                AttemptOutcome.AlreadySolved,
                runtime.Submit(Actor, new PuzzleNodeId(1), stackalloc int[] { 3, 9 }));
            Assert.AreEqual(1, runtime.SolvedCount);
        }

        [Test]
        public void UnknownNodeIsReportedNotThrown()
        {
            var runtime = new PuzzleRuntime(TwoStageGraph());

            Assert.AreEqual(
                AttemptOutcome.UnknownNode,
                runtime.Submit(Actor, new PuzzleNodeId(999), stackalloc int[] { 1 }));
        }

        [Test]
        public void ResetReturnsTheRoomToUnsolved()
        {
            var runtime = new PuzzleRuntime(TwoStageGraph());

            runtime.Submit(Actor, new PuzzleNodeId(1), stackalloc int[] { 3, 9 });
            runtime.Submit(Actor, new PuzzleNodeId(2), stackalloc int[] { 4, 1, 7, 2 });
            Assert.IsTrue(runtime.IsComplete);

            runtime.Reset();

            Assert.IsFalse(runtime.IsComplete);
            Assert.AreEqual(0, runtime.SolvedCount);
            Assert.IsFalse(runtime.IsSolved(new PuzzleNodeId(1)));
        }
    }

    public sealed class SolutionTests
    {
        [Test]
        public void OrderedSolutionRequiresExactOrder()
        {
            var solution = new Solution(SolutionKind.Ordered, 4, 1, 7, 2);

            Assert.IsTrue(solution.Matches(stackalloc int[] { 4, 1, 7, 2 }));
            Assert.IsFalse(solution.Matches(stackalloc int[] { 2, 7, 1, 4 }));
        }

        [Test]
        public void UnorderedSolutionAcceptsAnyOrder()
        {
            var solution = new Solution(SolutionKind.Unordered, 4, 1, 7, 2);

            Assert.IsTrue(solution.Matches(stackalloc int[] { 2, 7, 1, 4 }));
            Assert.IsTrue(solution.Matches(stackalloc int[] { 4, 1, 7, 2 }));
        }

        [Test]
        public void UnorderedSolutionRespectsDuplicateCounts()
        {
            var solution = new Solution(SolutionKind.Unordered, 5, 5, 3);

            Assert.IsTrue(solution.Matches(stackalloc int[] { 3, 5, 5 }));
            Assert.IsFalse(solution.Matches(stackalloc int[] { 5, 3, 3 }));
        }

        [Test]
        public void WrongLengthNeverMatches()
        {
            var solution = new Solution(SolutionKind.Ordered, 4, 1, 7, 2);

            Assert.IsFalse(solution.Matches(stackalloc int[] { 4, 1, 7 }));
            Assert.IsFalse(solution.Matches(stackalloc int[] { 4, 1, 7, 2, 0 }));
            Assert.IsFalse(solution.Matches(ReadOnlySpan<int>.Empty));
        }
    }
}
