using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="BoardModel.FindMatchGroups"/>: which runs are merged into
    /// one <see cref="MatchGroup"/>, what <see cref="MatchShape"/> each group is, and where its
    /// anchor cell lands.
    ///
    /// Calling FindMatchGroups with no cells is the cascade case (nobody swapped anything);
    /// passing two cells is the player's swap.
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell.
    /// </summary>
    public class BoardModelMatchGroupTests
    {
        // How many random boards the property test tries.
        private const int RandomBoardCount = 200;

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

        private static Vector2Int Cell(int x, int y)
        {
            return new Vector2Int(x, y);
        }

        // Cells are compared as text like "(0,0) (1,0) (2,0)", as in BoardModelMatchTests: it
        // makes failure messages easy to read.
        private static string Describe(IEnumerable<Vector2Int> cells)
        {
            var parts = new List<string>();
            foreach (Vector2Int cell in cells)
            {
                parts.Add($"({cell.x},{cell.y})");
            }
            return string.Join(" ", parts);
        }

        /// <summary>The board's one group, failing the test if there isn't exactly one.</summary>
        private static MatchGroup OnlyGroup(BoardModel board, Vector2Int? swappedA = null, Vector2Int? swappedB = null)
        {
            List<MatchGroup> groups = board.FindMatchGroups(swappedA, swappedB);
            Assert.AreEqual(1, groups.Count, "Expected exactly one match group.");
            return groups[0];
        }

        // ---------------------------------------------------------------- Lines

        [Test]
        public void LineOfThree_IsLine3_AnchoredOnItsMiddleCell()
        {
            BoardModel board = Grid(
                "GBGB",
                "BGBG",
                "RRRG");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Line3, group.Shape);
            Assert.AreEqual(grids.Type('R'), group.Colour);
            Assert.AreEqual(1, group.Runs.Count);
            Assert.AreEqual("(0,0) (1,0) (2,0)", Describe(group.Cells));
            Assert.AreEqual(Cell(1, 0), group.Anchor);
        }

        [Test]
        public void HorizontalLineOfFour_IsLine4_AnchoredOnTheLeftOfItsTwoMiddleCells()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "YRRRRY",
                "BGBGBG");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Line4, group.Shape);
            Assert.AreEqual("(1,1) (2,1) (3,1) (4,1)", Describe(group.Cells));
            // The middle cells are (2,1) and (3,1); an even length takes the lower index.
            Assert.AreEqual(Cell(2, 1), group.Anchor);
        }

        [Test]
        public void VerticalLineOfFour_IsLine4_AnchoredOnTheLowerOfItsTwoMiddleCells()
        {
            BoardModel board = Grid(
                "BGR",
                "GBR",
                "BGR",
                "GBR",
                "BGB");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Line4, group.Shape);
            Assert.AreEqual("(2,1) (2,2) (2,3) (2,4)", Describe(group.Cells));
            Assert.AreEqual(Cell(2, 2), group.Anchor);
        }

        [Test]
        public void LineOfFive_IsLine5_AnchoredOnItsMiddleCell()
        {
            BoardModel board = Grid(
                "RRRRR",
                "GBGBG",
                "BGBGB");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Line5, group.Shape);
            Assert.AreEqual(5, group.Cells.Count);
            Assert.AreEqual(Cell(2, 2), group.Anchor);
        }

        [Test]
        public void LineOfSix_IsStillLine5()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "RRRRRR",
                "BGBGBG");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Line5, group.Shape);
            Assert.AreEqual(6, group.Cells.Count);
            // The middle cells are (2,1) and (3,1).
            Assert.AreEqual(Cell(2, 1), group.Anchor);
        }

        // ---------------------------------------------------------------- Crosses

        [Test]
        public void LShape_IsOneCross_AnchoredOnTheCorner()
        {
            BoardModel board = Grid(
                "RGB",
                "RBG",
                "RRR");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Cross, group.Shape);
            Assert.AreEqual(2, group.Runs.Count);
            // Five cells, not six: the corner is in both runs but listed once.
            Assert.AreEqual("(0,0) (1,0) (2,0) (0,1) (0,2)", Describe(group.Cells));
            Assert.AreEqual(Cell(0, 0), group.Anchor);
        }

        [Test]
        public void TShape_IsOneCross_AnchoredOnTheJunction()
        {
            BoardModel board = Grid(
                "RRR",
                "GRB",
                "BRG");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Cross, group.Shape);
            Assert.AreEqual(5, group.Cells.Count);
            Assert.AreEqual(Cell(1, 2), group.Anchor);
        }

        [Test]
        public void PlusShape_IsOneCross_AnchoredOnTheCentre()
        {
            BoardModel board = Grid(
                "GRB",
                "RRR",
                "BRG");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Cross, group.Shape);
            Assert.AreEqual(5, group.Cells.Count);
            Assert.AreEqual(Cell(1, 1), group.Anchor);
        }

        [Test]
        public void RunOfFourCrossedByAnotherRun_IsCross_NotLine4()
        {
            BoardModel board = Grid(
                "RBGB",
                "RGBG",
                "RRRR");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Cross, group.Shape);
            Assert.AreEqual(6, group.Cells.Count);
            Assert.AreEqual(Cell(0, 0), group.Anchor);
        }

        [Test]
        public void RunOfFiveCrossedByAnotherRun_IsLine5_AnchoredOnTheMiddleOfTheFive()
        {
            BoardModel board = Grid(
                "RBGBG",
                "RGBGB",
                "RRRRR");

            MatchGroup group = OnlyGroup(board);

            // Line5 is the stronger shape, so it wins over Cross...
            Assert.AreEqual(MatchShape.Line5, group.Shape);
            Assert.AreEqual(2, group.Runs.Count);
            Assert.AreEqual(7, group.Cells.Count);
            // ...and it's anchored like a line: on the 5-run's middle, not on the crossing at (0,0).
            Assert.AreEqual(Cell(2, 0), group.Anchor);
        }

        [Test]
        public void ThreeRunsJoinedInAChain_AreOneCross_AnchoredOnTheLowestLeftmostCrossing()
        {
            // The two rows don't touch each other. It's the column, found last, that joins
            // them: the group it's already part of has to absorb the other one.
            BoardModel board = Grid(
                "RRRBG",
                "GBRGB",
                "BGRRR");

            MatchGroup group = OnlyGroup(board);

            Assert.AreEqual(MatchShape.Cross, group.Shape);
            Assert.AreEqual(3, group.Runs.Count);
            Assert.AreEqual(7, group.Cells.Count);
            // The column crosses a row at (2,0) and at (2,2); the lower one is the anchor.
            Assert.AreEqual(Cell(2, 0), group.Anchor);
        }

        // ---------------------------------------------------------------- Separate groups

        [Test]
        public void TwoMatchesInOneStep_AreTwoGroups_EachWithItsOwnShape()
        {
            BoardModel board = Grid(
                "BGBGBGBG",
                "RRRGYYYY");

            List<MatchGroup> groups = board.FindMatchGroups();

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(MatchShape.Line3, groups[0].Shape);
            Assert.AreEqual(grids.Type('R'), groups[0].Colour);
            Assert.AreEqual(Cell(1, 0), groups[0].Anchor);
            Assert.AreEqual(MatchShape.Line4, groups[1].Shape);
            Assert.AreEqual(grids.Type('Y'), groups[1].Colour);
            Assert.AreEqual(Cell(5, 0), groups[1].Anchor);
        }

        [Test]
        public void SameColourRunsThatDoNotTouch_StaySeparate()
        {
            BoardModel board = Grid("RRR.RRR");

            List<MatchGroup> groups = board.FindMatchGroups();

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("(0,0) (1,0) (2,0)", Describe(groups[0].Cells));
            Assert.AreEqual("(4,0) (5,0) (6,0)", Describe(groups[1].Cells));
        }

        [Test]
        public void SameColourRunsSideBySideWithNoSharedCell_StaySeparate()
        {
            // The two rows of R are neighbours, but no cell is in both runs (and a column of
            // two isn't a run), so they're two lines rather than one shape.
            BoardModel board = Grid(
                "RRRG",
                "GRRR");

            List<MatchGroup> groups = board.FindMatchGroups();

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(MatchShape.Line3, groups[0].Shape);
            Assert.AreEqual("(1,0) (2,0) (3,0)", Describe(groups[0].Cells));
            Assert.AreEqual(MatchShape.Line3, groups[1].Shape);
            Assert.AreEqual("(0,1) (1,1) (2,1)", Describe(groups[1].Cells));
        }

        [Test]
        public void BoardWithNoMatches_HasNoGroups()
        {
            BoardModel board = Grid(
                "RRGG",
                "GGRR",
                "RBRB");

            Assert.IsEmpty(board.FindMatchGroups());
        }

        // ---------------------------------------------------------------- Anchor for a player swap

        [Test]
        public void Swap_AnchorsALineOnTheSwappedCell_NotItsMiddle()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "YRRRRY",
                "BGBGBG");

            // The player dragged the R at the right-hand end of the run down from the row above.
            MatchGroup group = OnlyGroup(board, Cell(4, 1), Cell(4, 2));

            Assert.AreEqual(MatchShape.Line4, group.Shape);
            Assert.AreEqual(Cell(4, 1), group.Anchor);
        }

        [Test]
        public void Swap_AnchorIsTheSameWhicheverOrderTheSwappedCellsAreGiven()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "YRRRRY",
                "BGBGBG");

            MatchGroup group = OnlyGroup(board, Cell(4, 2), Cell(4, 1));

            Assert.AreEqual(Cell(4, 1), group.Anchor);
        }

        [Test]
        public void Swap_AnchorsACrossOnTheSwappedCell_NotTheCrossing()
        {
            BoardModel board = Grid(
                "RGB",
                "RBG",
                "RRR");

            MatchGroup group = OnlyGroup(board, Cell(2, 0), Cell(2, 1));

            Assert.AreEqual(MatchShape.Cross, group.Shape);
            Assert.AreEqual(Cell(2, 0), group.Anchor);
        }

        [Test]
        public void Swap_ThatMatchesOnBothSides_AnchorsEachGroupOnItsOwnSwappedCell()
        {
            BoardModel board = Grid(
                "RG",
                "RG",
                "RG",
                "RG");

            List<MatchGroup> groups = board.FindMatchGroups(Cell(0, 3), Cell(1, 3));

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual(Cell(0, 3), groups[0].Anchor);
            Assert.AreEqual(Cell(1, 3), groups[1].Anchor);
        }

        [Test]
        public void Swap_GroupWithNeitherSwappedCell_IsAnchoredLikeACascade()
        {
            BoardModel board = Grid(
                "GBGB",
                "BGBG",
                "RRRG");

            MatchGroup group = OnlyGroup(board, Cell(3, 1), Cell(3, 2));

            Assert.AreEqual(Cell(1, 0), group.Anchor);
        }

        // ---------------------------------------------------------------- Same runs as FindMatches

        [Test]
        public void RandomBoards_GroupsHoldExactlyTheRunsFindMatchesReports()
        {
            // The score and the round stats count every run in the groups. So for them to be
            // right, grouping must neither lose a run nor report one twice, whatever the board
            // looks like.
            for (int seed = 0; seed < RandomBoardCount; seed++)
            {
                BoardModel board = RandomBoard(seed);
                List<MatchGroup> groups = board.FindMatchGroups();

                var groupedRuns = new List<string>();
                var groupedCells = new List<Vector2Int>();
                foreach (MatchGroup group in groups)
                {
                    foreach (List<Vector2Int> run in group.Runs)
                    {
                        groupedRuns.Add(Describe(run));
                    }
                    foreach (Vector2Int cell in group.Cells)
                    {
                        Assert.AreEqual(group.Colour, board.TypeAt(cell), $"Seed {seed}: a group mixes colours.");
                    }
                    groupedCells.AddRange(group.Cells);
                    CollectionAssert.Contains(group.Cells, group.Anchor, $"Seed {seed}: an anchor is outside its group.");
                }

                CollectionAssert.AreEquivalent(board.FindMatches().ConvertAll(Describe), groupedRuns, $"Seed {seed}");
                // No cell twice: neither within a group, nor in two different groups.
                CollectionAssert.AllItemsAreUnique(groupedCells, $"Seed {seed}: a cell is in two groups.");
                Assert.AreEqual(BoardModel.LongestRunPerCell(board.FindMatches()).Count, groupedCells.Count,
                    $"Seed {seed}: the groups don't cover every matched cell.");
            }
        }

        /// <summary>A full 6x6 board of three colours: few enough colours that most boards have several matches, often crossing.</summary>
        private BoardModel RandomBoard(int seed)
        {
            const string letters = "RGB";
            var random = new System.Random(seed);
            var rows = new string[6];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = new char[6];
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = letters[random.Next(letters.Length)];
                }
                rows[i] = new string(row);
            }
            return Grid(rows);
        }
    }
}
