using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the numbers behind the clear effects (issue #54): how big each
    /// cell's burst is (<see cref="BoardModel.LongestRunPerCell"/> and <see cref="Explosion"/>'s
    /// mappings) and the shape of the pop (<see cref="Board.PopScale"/>). All plain static
    /// methods, so no particles, frames or scene are needed.
    /// </summary>
    public class ClearEffectsTests
    {
        // ---------------------------------------------------------------- Run length per cell

        private static List<Vector2Int> Run(params (int x, int y)[] cells)
        {
            var run = new List<Vector2Int>();
            foreach ((int x, int y) in cells)
            {
                run.Add(new Vector2Int(x, y));
            }
            return run;
        }

        [Test]
        public void LongestRunPerCell_SingleRun_GivesEveryCellItsLength()
        {
            var runs = new List<List<Vector2Int>> { Run((0, 0), (1, 0), (2, 0), (3, 0)) };

            Dictionary<Vector2Int, int> lengths = BoardModel.LongestRunPerCell(runs);

            Assert.AreEqual(4, lengths.Count);
            Assert.That(lengths.Values, Has.All.EqualTo(4));
        }

        [Test]
        public void LongestRunPerCell_TShape_SharedCellAppearsOnceWithTheLongerRun()
        {
            // A row of 3 and a column of 4 that share the cell (2, 1).
            var row = Run((1, 1), (2, 1), (3, 1));
            var column = Run((2, 0), (2, 1), (2, 2), (2, 3));

            Dictionary<Vector2Int, int> lengths = BoardModel.LongestRunPerCell(
                new List<List<Vector2Int>> { row, column });

            // 3 + 4 cells, minus the one they share.
            Assert.AreEqual(6, lengths.Count);
            Assert.AreEqual(4, lengths[new Vector2Int(2, 1)], "The shared cell should burst at the longer run's size.");
            Assert.AreEqual(3, lengths[new Vector2Int(1, 1)]);
            Assert.AreEqual(4, lengths[new Vector2Int(2, 3)]);
        }

        [Test]
        public void LongestRunPerCell_OrderOfRunsDoesNotMatter()
        {
            var row = Run((0, 0), (1, 0), (2, 0), (3, 0), (4, 0));
            var column = Run((2, 0), (2, 1), (2, 2));

            Assert.AreEqual(5, BoardModel.LongestRunPerCell(new List<List<Vector2Int>> { row, column })[new Vector2Int(2, 0)]);
            Assert.AreEqual(5, BoardModel.LongestRunPerCell(new List<List<Vector2Int>> { column, row })[new Vector2Int(2, 0)]);
        }

        // ---------------------------------------------------------------- Explosion sizing

        [TestCase(3, 1f)]
        [TestCase(4, 1.5f)]
        [TestCase(5, 2f)]
        [TestCase(7, 2f)]
        public void CountMultiplier_GrowsWithTheRun(int runLength, float expected)
        {
            Assert.AreEqual(expected, Explosion.CountMultiplier(runLength));
        }

        [Test]
        public void SpeedMultiplier_IsNormalFor3AndFasterForBiggerRuns()
        {
            Assert.AreEqual(1f, Explosion.SpeedMultiplier(3));
            Assert.Greater(Explosion.SpeedMultiplier(4), Explosion.SpeedMultiplier(3));
            Assert.Greater(Explosion.SpeedMultiplier(5), Explosion.SpeedMultiplier(4));
        }

        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        public void HasFlash_OnlyFor5OrMore(int runLength, bool expected)
        {
            Assert.AreEqual(expected, Explosion.HasFlash(runLength));
        }

        // ---------------------------------------------------------------- Pop

        [Test]
        public void PopScale_StartsAtNormalSizeAndEndsAtNothing()
        {
            Assert.AreEqual(1f, Board.PopScale(0f), 0.0001f);
            Assert.AreEqual(0f, Board.PopScale(1f), 0.0001f);
        }

        [Test]
        public void PopScale_SwellsToThePeakBeforeShrinking()
        {
            // Sample the whole pop and find the biggest size and when it happens.
            float peak = 0f;
            float peakTime = 0f;
            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                if (Board.PopScale(t) > peak)
                {
                    peak = Board.PopScale(t);
                    peakTime = t;
                }
            }

            Assert.AreEqual(Board.PopPeakScale, peak, 0.01f);
            Assert.That(peakTime, Is.GreaterThan(0f).And.LessThan(0.5f),
                "The swell should be the quick first part of the pop.");
        }

        [Test]
        public void PopScale_OutOfRangeTimesAreClamped()
        {
            Assert.AreEqual(1f, Board.PopScale(-1f), 0.0001f);
            Assert.AreEqual(0f, Board.PopScale(2f), 0.0001f);
        }
    }
}
