using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="ScoreRules"/> (the scoring arithmetic) and
    /// <see cref="ClearStep.From"/> (how a clear step's result becomes the numbers the score is
    /// worked out from).
    /// </summary>
    public class ScoreRulesTests
    {
        // ---------------------------------------------------------------- ScoreRules

        [TestCase(3, 1000)]
        [TestCase(4, 2000)]
        [TestCase(5, 5000)]
        [TestCase(7, 5000)]
        public void PointsForRun_KeepsTheRunValues(int runLength, int points)
        {
            Assert.AreEqual(points, ScoreRules.PointsForRun(runLength));
        }

        [Test]
        public void RunsOnly_AddUpTheirValues()
        {
            // A T shape is two runs (3 and 4) cleared in one step.
            Assert.AreEqual(3000, ScoreRules.PointsForStep(new[] { 3, 4 }, blastedGems: 0, cascadeIndex: 0));
        }

        [Test]
        public void BlastOnly_Scores250PerGem()
        {
            // A swapped Hypercube clears no runs: just the gems it zaps.
            Assert.AreEqual(7 * 250, ScoreRules.PointsForStep(new int[0], blastedGems: 7, cascadeIndex: 0));
        }

        [Test]
        public void FlameBlastOnThePlayersOwnMatch_AddsItsGemsOnTopOfTheRun()
        {
            // The issue's example: a run of 3 whose Flame destroys 8 more gems.
            Assert.AreEqual(1000 + 8 * 250, ScoreRules.PointsForStep(new[] { 3 }, blastedGems: 8, cascadeIndex: 0));
        }

        [Test]
        public void TheSameBlastOnTheFirstCascade_IsWorthDouble()
        {
            int ownMatch = ScoreRules.PointsForStep(new[] { 3 }, blastedGems: 8, cascadeIndex: 0);
            int firstCascade = ScoreRules.PointsForStep(new[] { 3 }, blastedGems: 8, cascadeIndex: 1);

            Assert.AreEqual(2 * ownMatch, firstCascade);
        }

        // cascade index -> multiplier: x1 for the player's own match, one more per cascade,
        // and never more than x5.
        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(3, 4)]
        [TestCase(4, 5)]
        [TestCase(5, 5)]
        [TestCase(20, 5)]
        public void Multiplier_ClimbsWithTheCascadeUpToTheCap(int cascadeIndex, int multiplier)
        {
            Assert.AreEqual(multiplier, ScoreRules.Multiplier(cascadeIndex));
        }

        [Test]
        public void Mixed_RunsAndBlastsShareTheStepsMultiplier()
        {
            // Two runs and 5 blasted gems on the third cascade (x4), and the same past the cap.
            int basePoints = 1000 + 5000 + 5 * 250;

            Assert.AreEqual(basePoints, ScoreRules.BasePoints(new[] { 3, 6 }, 5));
            Assert.AreEqual(basePoints * 4, ScoreRules.PointsForStep(new[] { 3, 6 }, 5, cascadeIndex: 3));
            Assert.AreEqual(basePoints * 5, ScoreRules.PointsForStep(new[] { 3, 6 }, 5, cascadeIndex: 9));
        }

        // ---------------------------------------------------------------- ClearStep.From

        private TestGrids grids;

        [SetUp]
        public void SetUp()
        {
            grids = new TestGrids();
        }

        [TearDown]
        public void TearDown()
        {
            grids.DestroyAll();
        }

        [Test]
        public void PlainMatch_IsItsRunsAndNoBlast()
        {
            BoardModel board = grids.Build(
                "DEABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCDE");
            List<MatchGroup> groups = board.FindMatchGroups();

            ClearStep step = ClearStep.From(board.ClearMatches(groups), groups, cascadeIndex: 2);

            Assert.AreEqual(new[] { 3 }, step.RunLengths);
            Assert.AreEqual(0, step.BlastedGems);
            Assert.AreEqual(3, step.GemsCleared);
            Assert.AreEqual(2, step.CascadeIndex);
            Assert.AreEqual(1000 * 3, step.Points, "The second cascade is x3.");
        }

        [Test]
        public void MatchedFlame_CountsTheGemsItsBlastDestroyed()
        {
            // The Flame is in the middle of the run of 3, so its 3x3 holds the run plus six
            // gems that weren't matched.
            BoardModel board = grids.Build(
                "DEABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCDE");
            grids.SetPowers(board,
                ".....",
                ".....",
                "..F..",
                ".....",
                ".....");
            List<MatchGroup> groups = board.FindMatchGroups();

            ClearStep step = ClearStep.From(board.ClearMatches(groups), groups, cascadeIndex: 0);

            Assert.AreEqual(new[] { 3 }, step.RunLengths);
            Assert.AreEqual(6, step.BlastedGems);
            Assert.AreEqual(9, step.GemsCleared);
            Assert.AreEqual(1000 + 6 * 250, step.Points);
        }

        [Test]
        public void SwappedHypercube_HasNoRunsAndCountsItselfAsCleared()
        {
            // The cube in the top-left corner is swapped with the B beside it: every B goes.
            BoardModel board = grids.Build(
                "*BCA",
                "CABC",
                "ABCB");

            BoardModel.ClearResult result = board.ActivateHypercube(new Vector2Int(0, 2), grids.Type('B'));
            ClearStep step = ClearStep.From(result, new List<MatchGroup>(), cascadeIndex: 0);

            Assert.That(step.RunLengths, Is.Empty);
            Assert.AreEqual(4, step.BlastedGems, "The four B's.");
            Assert.AreEqual(5, step.GemsCleared, "The four B's and the cube itself.");
            Assert.AreEqual(4 * 250, step.Points);
        }
    }
}
