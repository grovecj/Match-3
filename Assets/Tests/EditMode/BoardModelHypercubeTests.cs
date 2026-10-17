using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the Hypercube's rules in <see cref="BoardModel"/>: a line of 5 makes
    /// one, it never matches anything, swapping it with a coloured neighbour is always a valid
    /// move, and setting it off (<see cref="BoardModel.ActivateHypercube"/>) clears every gem
    /// of that neighbour's colour. Nothing else goes, unless one of those gems is a Flame or
    /// Star: that starts a chain reaction (more of those in BoardModelChainTests).
    ///
    /// A Hypercube has no colour, so it's drawn as '*' in the grids (and shows up as 'H' in
    /// <see cref="TestGrids.PowerRows"/>).
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell.
    /// </summary>
    public class BoardModelHypercubeTests
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

        private static Vector2Int Cell(int x, int y)
        {
            return new Vector2Int(x, y);
        }

        // Cells are compared as text like "(0,0) (1,0) (2,0)": it makes failure messages easy to read.
        private static string Describe(IEnumerable<Vector2Int> cells)
        {
            var parts = new List<string>();
            foreach (Vector2Int cell in cells)
            {
                parts.Add($"({cell.x},{cell.y})");
            }
            return string.Join(" ", parts);
        }

        /// <summary>Finds the board's matches (as a cascade would) and clears them.</summary>
        private static BoardModel.ClearResult ClearAll(BoardModel board)
        {
            return board.ClearMatches(board.FindMatchGroups());
        }

        // ---------------------------------------------------------------- Creating a Hypercube

        [Test]
        public void LineOfFive_LeavesAColourlessHypercubeInTheMiddle()
        {
            BoardModel board = Grid(
                "GBGBGBG",
                "YRRRRRY",
                "BGBGBGB");

            // No swapped cells, so this is a cascade: the anchor is the middle of the five.
            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[] { "GBGBGBG", "Y..*..Y", "BGBGBGB" }, grids.Rows(board));
            Assert.AreEqual(new[] { ".......", "...H...", "......." }, grids.PowerRows(board));

            Assert.AreEqual("(1,1) (2,1) (4,1) (5,1)", Describe(result.Matched));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(Cell(3, 1), result.Created[0].Cell);
            Assert.AreEqual(PowerKind.Hypercube, result.Created[0].Kind);
            // Unlike a Flame or Star, it doesn't keep the colour of the match that made it.
            Assert.IsNull(result.Created[0].Piece.Colour);
            Assert.IsFalse(board.PieceAt(3, 1).IsEmpty, "No colour isn't the same as nothing there.");
            Assert.IsEmpty(result.Detonations);
        }

        [Test]
        public void LineOfFive_FromASwap_LeavesTheHypercubeAtTheSwappedCell()
        {
            BoardModel board = Grid(
                "GRBGB",
                "RGRRR");
            Vector2Int a = Cell(1, 0);
            Vector2Int b = Cell(1, 1);

            // What the Board does for a swap: swap, find the matches with the swapped cells, clear.
            board.Swap(a, b);
            BoardModel.ClearResult result = board.ClearMatches(board.FindMatchGroups(a, b));

            // (1,0) is where the player put the fifth R. It isn't the middle of the line, which
            // shows it's the swap that decided the spot.
            Assert.AreEqual(new[] { "GGBGB", ".*..." }, grids.Rows(board));
            Assert.AreEqual(Cell(1, 0), result.Created[0].Cell);
            Assert.AreEqual(PowerKind.Hypercube, result.Created[0].Kind);
        }

        [Test]
        public void LineOfSix_LeavesOneHypercube()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "RRRRRR");

            BoardModel.ClearResult result = ClearAll(board);

            // Six is even, so "the middle" is the left of the two middle cells.
            Assert.AreEqual(new[] { "GBGBGB", "..*..." }, grids.Rows(board));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(PowerKind.Hypercube, result.Created[0].Kind);
            Assert.AreEqual(5, result.Matched.Count);
        }

        [Test]
        public void ColumnOfFive_LeavesAHypercubeToo()
        {
            BoardModel board = Grid(
                "RG",
                "RB",
                "RG",
                "RB",
                "RG");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[] { ".G", ".B", "*G", ".B", ".G" }, grids.Rows(board));
            Assert.AreEqual(Cell(0, 2), result.Created[0].Cell);
        }

        // ---------------------------------------------------------------- It never matches

        [Test]
        public void Hypercube_BetweenGemsOfOneColour_DoesNotJoinThemIntoARun()
        {
            // If the cube counted as an R, both the row and the column would be runs of 5.
            BoardModel board = Grid(
                "GBRBG",
                "BGRGB",
                "RR*RR",
                "GBRBG",
                "BGRGB");

            Assert.IsEmpty(board.FindMatchGroups());
        }

        [Test]
        public void Hypercube_FallingIntoPlace_IsNotMatched()
        {
            // The run under the cube is cleared, and the whole row above drops into its place,
            // the cube still between two R's. A red gem there would make a match; the cube doesn't.
            BoardModel board = Grid(
                "R*R",
                "GGG");

            BoardModel.ClearResult result = ClearAll(board);
            Assert.AreEqual("(0,0) (1,0) (2,0)", Describe(result.Matched), "Only the G's should have matched.");
            board.Collapse();

            Assert.AreEqual(new[] { "...", "R*R" }, grids.Rows(board));
            Assert.IsEmpty(board.FindMatchGroups(), "The cube landed, and nothing should match because of it.");
            Assert.AreEqual(PowerKind.Hypercube, board.PieceAt(1, 0).Power, "The cube should still be there.");
        }

        [Test]
        public void Hypercube_CaughtInAFlamesBlast_GoesOffOnTheMostCommonColour()
        {
            BoardModel board = Grid(
                "B*GBG",
                "RRRBB");
            grids.SetPowers(board,
                ".....",
                ".F...");

            BoardModel.ClearResult result = ClearAll(board);

            // The Flame at (1,0) takes the 3x3 around it, cube included. That leaves three B's
            // and a G in the last two columns. Nobody swapped the cube with anything, so it
            // picks for itself: B, the colour there's most of.
            Assert.AreEqual(new[] { "....G", "....." }, grids.Rows(board));
            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual("(0,1) (1,1) (2,1)", Describe(result.Waves[0][0].Blasted));

            BoardModel.Detonation cube = result.Waves[1][0];
            Assert.AreEqual(PowerKind.Hypercube, cube.Kind);
            Assert.AreEqual(Cell(1, 1), cube.Cell);
            Assert.AreEqual(grids.Type('B'), cube.Target);
            Assert.AreEqual("(3,0) (3,1) (4,0)", Describe(cube.Blasted));
        }

        // ---------------------------------------------------------------- Which swaps are valid

        [Test]
        public void IsHypercubeSwap_CubeAndColouredGem_IsTrue_WhicheverWayRound()
        {
            BoardModel board = Grid("*R");

            Assert.IsTrue(board.IsHypercubeSwap(Cell(0, 0), Cell(1, 0)));
            Assert.IsTrue(board.IsHypercubeSwap(Cell(1, 0), Cell(0, 0)));
        }

        [Test]
        public void IsHypercubeSwap_CubeAndAnotherPowerGem_IsTrue()
        {
            // A Flame has a colour, and that's all the cube needs.
            BoardModel board = Grid("*R");
            grids.SetPowers(board, ".F");

            Assert.IsTrue(board.IsHypercubeSwap(Cell(0, 0), Cell(1, 0)));
        }

        [Test]
        public void IsHypercubeSwap_TwoHypercubes_IsFalse_ItsACombo()
        {
            BoardModel board = Grid("**");

            Assert.IsFalse(board.IsHypercubeSwap(Cell(0, 0), Cell(1, 0)));
            // Not a plain Hypercube swap, but still a move: see BoardModelComboTests.
            Assert.IsTrue(board.IsComboSwap(Cell(0, 0), Cell(1, 0)));
            Assert.IsTrue(board.HasValidMove());
        }

        [Test]
        public void IsHypercubeSwap_CubeAndEmptyCell_IsRejected()
        {
            BoardModel board = Grid("*.");

            Assert.IsFalse(board.IsHypercubeSwap(Cell(0, 0), Cell(1, 0)));
            Assert.IsFalse(board.HasValidMove());
        }

        [Test]
        public void IsHypercubeSwap_TwoOrdinaryGems_IsFalse()
        {
            BoardModel board = Grid("RG");

            Assert.IsFalse(board.IsHypercubeSwap(Cell(0, 0), Cell(1, 0)));
        }

        [Test]
        public void HasValidMove_DeadBoardWithAHypercubeOnIt_IsPlayable()
        {
            // Every row and column holds one of each type, so no swap can line three up...
            Assert.IsFalse(Grid(
                "ABC",
                "CAB",
                "BCA").HasValidMove());

            // ...but put a Hypercube in the middle and there is a move after all: the cube itself.
            BoardModel board = Grid(
                "ABC",
                "C*B",
                "BCA");

            Assert.IsTrue(board.HasValidMove());
            Assert.IsTrue(board.TryFindValidMove(out Vector2Int a, out Vector2Int b));
            Assert.IsTrue(board.IsHypercubeSwap(a, b), "The only move on this board is swapping the cube.");
            // Looking for a move mustn't make one.
            Assert.AreEqual(new[] { "ABC", "C*B", "BCA" }, grids.Rows(board));
        }

        // ---------------------------------------------------------------- Setting it off

        [Test]
        public void ActivateHypercube_ClearsEveryGemOfTheColourAndTheCube_AndNothingElse()
        {
            BoardModel board = Grid(
                "RGBR",
                "G*RB",
                "BRGG");

            BoardModel.ClearResult result = board.ActivateHypercube(Cell(1, 1), grids.Type('R'));

            Assert.AreEqual(new[]
            {
                ".GB.",
                "G..B",
                "B.GG",
            }, grids.Rows(board));

            // The cube is the only thing that went off: one wave, with one gem in it.
            Assert.AreEqual(1, result.Waves.Count);
            Assert.AreEqual(1, result.Detonations.Count);
            BoardModel.Detonation cube = result.Detonations[0];
            Assert.AreEqual(PowerKind.Hypercube, cube.Kind);
            Assert.AreEqual(Cell(1, 1), cube.Cell);
            Assert.AreEqual(grids.Type('R'), cube.Target);
            // Column by column from the left, each bottom to top. The cube's own cell isn't listed.
            Assert.AreEqual("(0,2) (1,0) (2,1) (3,2)", Describe(cube.Blasted));
            // It wasn't a match, so nothing counts as matched and no gem was made.
            Assert.IsEmpty(result.Matched);
            Assert.IsEmpty(result.Created);
        }

        [Test]
        public void ActivateHypercube_AfterTheSwap_ClearsFromTheCubesNewCell()
        {
            BoardModel board = Grid(
                "RGB",
                "*RG",
                "GBR");
            Vector2Int cubeCell = Cell(0, 1);
            Vector2Int gemCell = Cell(1, 1);
            Assert.IsTrue(board.IsHypercubeSwap(cubeCell, gemCell));

            // What the Board does: make the swap, then set the cube off where it ended up, on
            // the colour of the gem that's now where the cube was.
            board.Swap(cubeCell, gemCell);
            Assert.IsTrue(board.IsHypercubeSwap(cubeCell, gemCell), "It's still a Hypercube swap after the swap is made.");
            BoardModel.ClearResult result = board.ActivateHypercube(gemCell, board.TypeAt(cubeCell));

            Assert.AreEqual(new[] { ".GB", "..G", "GB." }, grids.Rows(board));
            Assert.AreEqual(gemCell, result.Detonations[0].Cell);
            Assert.AreEqual("(0,1) (0,2) (2,0)", Describe(result.Detonations[0].Blasted));
        }

        [Test]
        public void ActivateHypercube_PowerGemOfTheColour_GoesOffInTheNextWave()
        {
            BoardModel board = Grid(
                "GBG",
                "BRB",
                "G*G");
            grids.SetPowers(board,
                "...",
                ".F.",
                "...");

            BoardModel.ClearResult result = board.ActivateHypercube(Cell(1, 0), grids.Type('R'));

            // The cube clears the one R, which is a Flame, so the Flame goes off: on this small
            // board its 3x3 is everything that's left.
            Assert.AreEqual(new[] { "...", "...", "..." }, grids.Rows(board));
            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual(PowerKind.Hypercube, result.Waves[0][0].Kind);
            Assert.AreEqual("(1,1)", Describe(result.Waves[0][0].Blasted));
            Assert.AreEqual(PowerKind.Flame, result.Waves[1][0].Kind);
            Assert.AreEqual(Cell(1, 1), result.Waves[1][0].Cell);
            Assert.AreEqual("(0,0) (0,1) (0,2) (1,2) (2,0) (2,1) (2,2)", Describe(result.Waves[1][0].Blasted));
        }

        [Test]
        public void ActivateHypercube_LeavesOtherHypercubesAlone()
        {
            BoardModel board = Grid("*R*");

            board.ActivateHypercube(Cell(0, 0), grids.Type('R'));

            Assert.AreEqual(new[] { "..*" }, grids.Rows(board));
        }

        [Test]
        public void ActivateHypercube_ColourThatIsAlsoInALine_MakesNoNewPowerGem()
        {
            // Four R's in a row would leave a Flame if they were cleared as a match. Set off by
            // a cube they're just cleared.
            BoardModel board = Grid("RRRR*");

            BoardModel.ClearResult result = board.ActivateHypercube(Cell(4, 0), grids.Type('R'));

            Assert.AreEqual(new[] { "....." }, grids.Rows(board));
            Assert.AreEqual(4, result.Detonations[0].Blasted.Count);
            Assert.IsEmpty(result.Created);
        }

        [Test]
        public void ActivateHypercube_ThenGravityAndRefill_LeaveAFullBoard()
        {
            BoardModel board = Grid(
                "RGB",
                "G*R",
                "BRG");

            board.ActivateHypercube(Cell(1, 1), grids.Type('R'));
            List<BoardModel.Fall> falls = board.Collapse();
            board.Refill();

            // The G that sat on the cube, and the B over the R in the last column, both dropped.
            Assert.That(falls, Is.Not.Empty);
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    Assert.IsFalse(board.PieceAt(x, y).IsEmpty, $"Cell ({x},{y}) was left empty.");
                }
            }
        }

        [Test]
        public void ActivateHypercube_OnACellWithNoHypercube_Throws()
        {
            BoardModel board = Grid("RG");

            Assert.Throws<System.ArgumentException>(() => board.ActivateHypercube(Cell(0, 0), grids.Type('G')));
            Assert.AreEqual(new[] { "RG" }, grids.Rows(board), "A refused call shouldn't change the board.");
        }

        [Test]
        public void ActivateHypercube_WithNoColour_Throws()
        {
            BoardModel board = Grid("*R");

            Assert.Throws<System.ArgumentNullException>(() => board.ActivateHypercube(Cell(0, 0), null));
            Assert.AreEqual(new[] { "*R" }, grids.Rows(board), "A refused call shouldn't change the board.");
        }

        // ---------------------------------------------------------------- Timing the zaps
        //
        // These are the Board's and the bolt's numbers rather than the model's, but they're the
        // rest of "what a Hypercube does", and they're plain static methods: no scene needed.

        [Test]
        public void ZapDelay_GivesEachGemItsTurn_OneIntervalApart()
        {
            const float interval = 0.05f;

            Assert.AreEqual(0f, Board.ZapDelay(0, interval), "The first gem goes straight away.");
            Assert.AreEqual(0.05f, Board.ZapDelay(1, interval), 0.0001f);
            Assert.AreEqual(0.15f, Board.ZapDelay(3, interval), 0.0001f);
        }

        [Test]
        public void ZapDelay_WithNoInterval_ZapsEverythingAtOnce()
        {
            Assert.AreEqual(0f, Board.ZapDelay(7, 0f));
        }

        [Test]
        public void ZapOpacity_IsNothingUntilItsTurn_ThenFlashesAndFades()
        {
            const float delay = 0.2f;

            Assert.AreEqual(0f, HypercubeZap.Opacity(0f, delay), "A bolt whose turn hasn't come shouldn't be drawn.");
            Assert.AreEqual(0f, HypercubeZap.Opacity(0.19f, delay));
            Assert.AreEqual(1f, HypercubeZap.Opacity(delay, delay), "It appears at full strength, all at once.");
            Assert.AreEqual(0.5f, HypercubeZap.Opacity(delay + HypercubeZap.FadeDuration / 2f, delay), 0.0001f);
            Assert.AreEqual(0f, HypercubeZap.Opacity(delay + HypercubeZap.FadeDuration, delay), 0.0001f);
            Assert.AreEqual(0f, HypercubeZap.Opacity(delay + 5f, delay), "Long after, it stays gone.");
        }
    }
}
