using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="RoundStats"/>, the count of what happened in a round
    /// that's sent to the server with the score. It's a plain class, so the tests just call the
    /// methods the Board's events would.
    /// </summary>
    public class RoundStatsTests
    {
        [Test]
        public void NewStats_AreAllZero()
        {
            var stats = new RoundStats();

            Assert.That(stats.swaps, Is.Zero);
            Assert.That(stats.runs3 + stats.runs4 + stats.runs5, Is.Zero);
            Assert.That(stats.longestRun, Is.Zero);
            Assert.That(stats.bestChain, Is.Zero);
        }

        [Test]
        public void SwapStarted_CountsEverySwap()
        {
            var stats = new RoundStats();

            stats.SwapStarted();
            stats.SwapStarted();

            Assert.That(stats.swaps, Is.EqualTo(2));
        }

        // The groups have to be the score's groups (3, 4, and 5 or more): the server checks that
        // the matches add up to the score.
        [TestCase(3, 1, 0, 0)]
        [TestCase(4, 0, 1, 0)]
        [TestCase(5, 0, 0, 1)]
        [TestCase(6, 0, 0, 1)]
        [TestCase(8, 0, 0, 1)]
        public void RunCleared_CountsTheRunInItsGroup(int runLength, int runs3, int runs4, int runs5)
        {
            var stats = new RoundStats();

            stats.RunCleared(runLength);

            Assert.That(stats.runs3, Is.EqualTo(runs3));
            Assert.That(stats.runs4, Is.EqualTo(runs4));
            Assert.That(stats.runs5, Is.EqualTo(runs5));
        }

        [Test]
        public void RunCleared_RemembersTheLongestRun()
        {
            var stats = new RoundStats();

            stats.RunCleared(4);
            stats.RunCleared(6);
            stats.RunCleared(3);

            Assert.That(stats.longestRun, Is.EqualTo(6));
        }

        [Test]
        public void StepResolved_BestChainIsTheMostStepsFromOneSwap()
        {
            var stats = new RoundStats();

            // One swap that cascades twice (steps 0, 1, 2), then one that doesn't cascade.
            stats.StepResolved(0);
            stats.StepResolved(1);
            stats.StepResolved(2);
            stats.StepResolved(0);

            Assert.That(stats.bestChain, Is.EqualTo(3));
        }

        [Test]
        public void Reset_GoesBackToZero()
        {
            var stats = new RoundStats();
            stats.SwapStarted();
            stats.RunCleared(5);
            stats.StepResolved(1);

            stats.Reset();

            Assert.That(stats.swaps, Is.Zero);
            Assert.That(stats.runs5, Is.Zero);
            Assert.That(stats.longestRun, Is.Zero);
            Assert.That(stats.bestChain, Is.Zero);
        }

        [Test]
        public void Copy_IsNotChangedByLaterCounting()
        {
            var stats = new RoundStats();
            stats.RunCleared(3);

            RoundStats copy = stats.Copy();
            stats.RunCleared(3);

            Assert.That(copy.runs3, Is.EqualTo(1));
            Assert.That(stats.runs3, Is.EqualTo(2));
        }

        [Test]
        public void ToJson_UsesTheNamesTheServerExpects()
        {
            var stats = new RoundStats();
            stats.SwapStarted();
            stats.RunCleared(4);
            stats.StepResolved(0);

            Assert.That(JsonUtility.ToJson(stats), Is.EqualTo(
                "{\"swaps\":1,\"runs3\":0,\"runs4\":1,\"runs5\":0,\"longestRun\":4,\"bestChain\":1}"));
        }
    }
}
