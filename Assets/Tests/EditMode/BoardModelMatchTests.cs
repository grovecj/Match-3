using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="BoardModel.FindMatches"/>. Because BoardModel is a plain
    /// C# class, each test just draws a small board as strings (see <see cref="TestGrids"/>),
    /// asks for matches, and compares. No GameObjects or frames needed.
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell.
    /// </summary>
    public class BoardModelMatchTests
    {
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

        private BoardModel Grid(params string[] rows)
        {
            return grids.Build(rows);
        }

        // Runs are compared as text like "(0,0) (1,0) (2,0)". That makes failure messages easy to
        // read, and lets CollectionAssert.AreEquivalent (same items, any order) compare whole
        // runs without us writing an equality check for List<Vector2Int>.
        // `params (int x, int y)[]` takes any number of tuples, so a run is written as
        // Run((0, 0), (1, 0), (2, 0)).
        private static string Run(params (int x, int y)[] cells)
        {
            var parts = new List<string>();
            foreach ((int x, int y) in cells)
            {
                parts.Add($"({x},{y})");
            }
            return string.Join(" ", parts);
        }

        private static string Describe(List<Vector2Int> run)
        {
            return string.Join(" ", run.ConvertAll(cell => $"({cell.x},{cell.y})"));
        }

        /// <summary>Asserts that <paramref name="board"/>'s matches are exactly <paramref name="expectedRuns"/>, in any order.</summary>
        private static void AssertMatches(BoardModel board, params string[] expectedRuns)
        {
            List<string> actual = board.FindMatches().ConvertAll(Describe);
            CollectionAssert.AreEquivalent(expectedRuns, actual);
        }

        // ---------------------------------------------------------------- TestGrids itself

        [Test]
        public void Build_FirstStringIsTopRow()
        {
            BoardModel board = Grid(
                "R.",
                ".G");

            Assert.AreEqual(2, board.Width);
            Assert.AreEqual(2, board.Height);
            Assert.AreEqual(grids.Type('R'), board.TypeAt(0, 1));
            Assert.AreEqual(grids.Type('G'), board.TypeAt(1, 0));
            Assert.IsNull(board.TypeAt(0, 0));
            Assert.IsNull(board.TypeAt(1, 1));
        }

        // ---------------------------------------------------------------- Horizontal runs

        [Test]
        public void FindMatches_HorizontalThree_IsOneRun()
        {
            BoardModel board = Grid(
                "GBGB",
                "BGBG",
                "RRRG");

            AssertMatches(board, Run((0, 0), (1, 0), (2, 0)));
        }

        [Test]
        public void FindMatches_HorizontalFour_IsOneRunOfFour()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "YRRRRY",
                "BGBGBG");

            AssertMatches(board, Run((1, 1), (2, 1), (3, 1), (4, 1)));
        }

        [Test]
        public void FindMatches_HorizontalFiveAcrossWholeTopRow_IsOneRunOfFive()
        {
            // Touches the left, right, and top edges at once.
            BoardModel board = Grid(
                "RRRRR",
                "GBGBG",
                "BGBGB");

            AssertMatches(board, Run((0, 2), (1, 2), (2, 2), (3, 2), (4, 2)));
        }

        [Test]
        public void FindMatches_HorizontalRunEndingAtRightEdge_IsFound()
        {
            // The last run in a line is only checked after the loop steps past the end, so this
            // guards against an off-by-one there.
            BoardModel board = Grid("GRRR");

            AssertMatches(board, Run((1, 0), (2, 0), (3, 0)));
        }

        [Test]
        public void FindMatches_TwoRunsInOneRow_AreSeparateRuns()
        {
            BoardModel board = Grid("RRRGGG");

            AssertMatches(board,
                Run((0, 0), (1, 0), (2, 0)),
                Run((3, 0), (4, 0), (5, 0)));
        }

        // ---------------------------------------------------------------- Vertical runs

        [Test]
        public void FindMatches_VerticalThree_IsOneRun()
        {
            BoardModel board = Grid(
                "GB",
                "RB",
                "RG",
                "RB");

            AssertMatches(board, Run((0, 0), (0, 1), (0, 2)));
        }

        [Test]
        public void FindMatches_VerticalFourAtRightAndTopEdges_IsOneRunOfFour()
        {
            BoardModel board = Grid(
                "BGR",
                "GBR",
                "BGR",
                "GBR",
                "BGB");

            AssertMatches(board, Run((2, 1), (2, 2), (2, 3), (2, 4)));
        }

        [Test]
        public void FindMatches_VerticalFiveInWholeColumn_IsOneRunOfFive()
        {
            BoardModel board = Grid(
                "YB",
                "YG",
                "YB",
                "YG",
                "YB");

            AssertMatches(board, Run((0, 0), (0, 1), (0, 2), (0, 3), (0, 4)));
        }

        // ---------------------------------------------------------------- Shapes

        [Test]
        public void FindMatches_LShape_IsTwoRunsSharingTheCorner()
        {
            BoardModel board = Grid(
                "RGB",
                "RBG",
                "RRR");

            // (0, 0) is in both runs. FindMatches reports each run separately; it's
            // BoardModel.Clear's job to clear the shared cell only once.
            AssertMatches(board,
                Run((0, 0), (1, 0), (2, 0)),
                Run((0, 0), (0, 1), (0, 2)));
        }

        [Test]
        public void FindMatches_TShape_IsTwoRunsSharingTheJunction()
        {
            BoardModel board = Grid(
                "RRR",
                "GRB",
                "BRG");

            AssertMatches(board,
                Run((0, 2), (1, 2), (2, 2)),
                Run((1, 0), (1, 1), (1, 2)));
        }

        // ---------------------------------------------------------------- No false positives

        [Test]
        public void FindMatches_OnlyPairs_FindsNothing()
        {
            BoardModel board = Grid(
                "RRGG",
                "GGRR",
                "RBRB");

            AssertMatches(board);
        }

        [Test]
        public void FindMatches_TwoPairsSplitByAnotherType_FindsNothing()
        {
            BoardModel board = Grid("RRGRR");

            AssertMatches(board);
        }

        // ---------------------------------------------------------------- Empty cells

        [Test]
        public void FindMatches_EmptyCellInsideHorizontalLine_BreaksTheRun()
        {
            BoardModel board = Grid("RR.RR");

            AssertMatches(board);
        }

        [Test]
        public void FindMatches_EmptyCellInsideVerticalLine_BreaksTheRun()
        {
            BoardModel board = Grid(
                "R",
                "R",
                ".",
                "R",
                "R");

            AssertMatches(board);
        }

        [Test]
        public void FindMatches_RunsOnEitherSideOfAnEmptyCell_AreSeparateRuns()
        {
            BoardModel board = Grid("RRR.RRR");

            AssertMatches(board,
                Run((0, 0), (1, 0), (2, 0)),
                Run((4, 0), (5, 0), (6, 0)));
        }

        [Test]
        public void FindMatches_EmptyBoard_FindsNothing()
        {
            // Empty cells are all "the same" (null), but they must never count as a match.
            BoardModel board = Grid(
                "...",
                "...",
                "...");

            AssertMatches(board);
        }
    }
}
