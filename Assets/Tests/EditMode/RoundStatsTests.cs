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
        public void StepScored_BestChainIsTheMostStepsFromOneSwap()
        {
            var stats = new RoundStats();

            // One swap that cascades twice (steps 0, 1, 2), then one that doesn't cascade.
            stats.StepScored(new ClearStep(0, new[] { 3 }, 0, 3));
            stats.StepScored(new ClearStep(1, new[] { 3 }, 0, 3));
            stats.StepScored(new ClearStep(2, new[] { 3 }, 0, 3));
            stats.StepScored(new ClearStep(0, new[] { 3 }, 0, 3));

            Assert.That(stats.bestChain, Is.EqualTo(3));
        }

        [Test]
        public void StepScored_HypercubeSwapWithNoCascade_IsAChainOfOne()
        {
            // A swapped Hypercube's step has no runs, but it's still a step.
            var stats = new RoundStats();

            stats.StepScored(new ClearStep(0, new int[0], blastedGems: 7, gemsCleared: 8));

            Assert.That(stats.bestChain, Is.EqualTo(1));
        }

        [Test]
        public void StepScored_KeepsBasePointsUnderTheirMultiplier()
        {
            var stats = new RoundStats();

            // A run of 3 with a Flame that blasted 6 (x1), a cascade of a 4 (x2), and a step far
            // enough down the chain to be capped at x5.
            stats.StepScored(new ClearStep(0, new[] { 3 }, blastedGems: 6, gemsCleared: 9));
            stats.StepScored(new ClearStep(1, new[] { 4 }, blastedGems: 0, gemsCleared: 4));
            stats.StepScored(new ClearStep(7, new[] { 3 }, blastedGems: 0, gemsCleared: 3));

            Assert.AreEqual(new[] { 2500, 2000, 0, 0, 1000 }, stats.basePointsAtMultiplier);
            Assert.AreEqual(6, stats.blastedGems);
        }

        [Test]
        public void StepScored_BasePointsTimesMultipliersAreTheScore()
        {
            // The check the server makes: what's sent adds back up to the score.
            var stats = new RoundStats();
            var steps = new[]
            {
                new ClearStep(0, new[] { 3, 4 }, blastedGems: 8, gemsCleared: 15),
                new ClearStep(1, new[] { 5 }, blastedGems: 0, gemsCleared: 5),
                new ClearStep(2, new int[0], blastedGems: 12, gemsCleared: 12),
                new ClearStep(6, new[] { 3 }, blastedGems: 1, gemsCleared: 4),
            };
            int score = 0;
            foreach (ClearStep step in steps)
            {
                stats.StepScored(step);
                score += step.Points;
            }

            int fromStats = 0;
            for (int i = 0; i < stats.basePointsAtMultiplier.Length; i++)
            {
                fromStats += stats.basePointsAtMultiplier[i] * (i + 1);
            }
            Assert.AreEqual(score, fromStats);
        }

        [Test]
        public void MostGemsOneSwap_AddsUpEveryStepOfASwap()
        {
            var stats = new RoundStats();

            stats.SwapStarted();
            stats.StepScored(new ClearStep(0, new[] { 3 }, 6, gemsCleared: 9));
            stats.StepScored(new ClearStep(1, new[] { 3 }, 0, gemsCleared: 3));
            stats.SwapStarted();
            stats.StepScored(new ClearStep(0, new[] { 4 }, 0, gemsCleared: 4));

            Assert.AreEqual(12, stats.mostGemsOneSwap);
        }

        [Test]
        public void PowerGems_AreCountedByKind()
        {
            var stats = new RoundStats();

            stats.PowerGemCreated(PowerKind.Flame);
            stats.PowerGemCreated(PowerKind.Flame);
            stats.PowerGemCreated(PowerKind.Star);
            stats.PowerGemCreated(PowerKind.Hypercube);
            stats.PowerGemDetonated(PowerKind.Flame);
            stats.PowerGemDetonated(PowerKind.Hypercube);
            stats.PowerGemDetonated(PowerKind.Hypercube);

            Assert.AreEqual(2, stats.flamesCreated);
            Assert.AreEqual(1, stats.starsCreated);
            Assert.AreEqual(1, stats.hypercubesCreated);
            Assert.AreEqual(1, stats.flamesDetonated);
            Assert.AreEqual(0, stats.starsDetonated);
            Assert.AreEqual(2, stats.hypercubesDetonated);
        }

        [Test]
        public void Reset_GoesBackToZero()
        {
            var stats = new RoundStats();
            stats.SwapStarted();
            stats.RunCleared(5);
            stats.StepScored(new ClearStep(1, new[] { 5 }, 3, 8));
            stats.PowerGemCreated(PowerKind.Star);
            stats.PowerGemDetonated(PowerKind.Star);

            stats.Reset();

            Assert.That(stats.swaps, Is.Zero);
            Assert.That(stats.runs5, Is.Zero);
            Assert.That(stats.longestRun, Is.Zero);
            Assert.That(stats.bestChain, Is.Zero);
            Assert.That(stats.blastedGems, Is.Zero);
            Assert.That(stats.basePointsAtMultiplier, Has.All.Zero);
            Assert.That(stats.mostGemsOneSwap, Is.Zero);
            Assert.That(stats.starsCreated + stats.starsDetonated, Is.Zero);
            Assert.AreEqual(ScoreRules.Version, stats.scoringVersion);
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
        public void Copy_HasItsOwnBasePoints()
        {
            // The one field that's an array: a careless copy would share it with the original.
            var stats = new RoundStats();
            RoundStats copy = stats.Copy();

            stats.StepScored(new ClearStep(0, new[] { 3 }, 0, 3));

            Assert.That(copy.basePointsAtMultiplier, Has.All.Zero);
        }

        [Test]
        public void ToJson_UsesTheNamesTheServerExpects()
        {
            var stats = new RoundStats();
            stats.SwapStarted();
            stats.RunCleared(4);
            stats.PowerGemCreated(PowerKind.Flame);
            stats.StepScored(new ClearStep(0, new[] { 4 }, 0, 3));

            // The private per-swap count must not be in it: the server only wants totals.
            Assert.That(JsonUtility.ToJson(stats), Is.EqualTo(
                "{\"scoringVersion\":2,\"swaps\":1,\"runs3\":0,\"runs4\":1,\"runs5\":0,\"longestRun\":4,\"bestChain\":1," +
                "\"blastedGems\":0,\"basePointsAtMultiplier\":[2000,0,0,0,0]," +
                "\"flamesCreated\":1,\"starsCreated\":0,\"hypercubesCreated\":0," +
                "\"flamesDetonated\":0,\"starsDetonated\":0,\"hypercubesDetonated\":0,\"mostGemsOneSwap\":3}"));
        }
    }
}
