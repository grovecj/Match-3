using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the Star gem's rules in <see cref="BoardModel"/>: which matches make
    /// one and where, and what its blast clears when it's matched (its whole row and column).
    /// Both go through <see cref="BoardModel.ClearMatches"/>, the same clear step the Flame
    /// uses: see <see cref="BoardModelFlameTests"/> for the rules the two gems share.
    ///
    /// Power gems are drawn separately (see <see cref="TestGrids.SetPowers"/>): 'S' is a Star
    /// and 'F' is a Flame.
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell.
    /// </summary>
    public class BoardModelStarTests
    {
        // A 5x5 board with no matches on it: each row is the one below shifted two places.
        // Tests swap one row or column out for R's, and since R appears nowhere else, the R's
        // are the only match.
        private static readonly string[] Quiet =
        {
            "DEABC", // y = 4
            "BCDEA",
            "EABCD",
            "CDEAB",
            "ABCDE", // y = 0
        };

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

        // ---------------------------------------------------------------- Creating a Star

        [Test]
        public void LShape_LeavesAStarOfItsColourAtTheCorner()
        {
            BoardModel board = Grid(
                "RGBG",
                "RBGB",
                "RRRG");

            // No swapped cells, so this is a cascade: the anchor is where the two runs cross.
            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                ".GBG",
                ".BGB",
                "R..G",
            }, grids.Rows(board));
            Assert.AreEqual(new[]
            {
                "....",
                "....",
                "S...",
            }, grids.PowerRows(board));

            Assert.AreEqual("(1,0) (2,0) (0,1) (0,2)", Describe(result.Matched));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(Cell(0, 0), result.Created[0].Cell);
            Assert.AreEqual(PowerKind.Star, result.Created[0].Kind);
            Assert.AreEqual(grids.Type('R'), result.Created[0].Piece.Colour);
            Assert.IsEmpty(result.Detonations, "A Star must not go off in the step that made it.");
        }

        [Test]
        public void TShape_LeavesAStarWhereTheStemMeetsTheBar()
        {
            BoardModel board = Grid(
                "RRR",
                "GRB",
                "BRG");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                ".R.",
                "G.B",
                "B.G",
            }, grids.Rows(board));
            Assert.AreEqual(new[] { ".S.", "...", "..." }, grids.PowerRows(board));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(4, result.Matched.Count);
        }

        [Test]
        public void PlusShape_LeavesAStarInTheMiddle()
        {
            BoardModel board = Grid(
                "GRB",
                "RRR",
                "BRG");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "G.B",
                ".R.",
                "B.G",
            }, grids.Rows(board));
            Assert.AreEqual(new[] { "...", ".S.", "..." }, grids.PowerRows(board));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(4, result.Matched.Count);
        }

        [Test]
        public void CrossMadeByASwap_LeavesTheStarAtTheSwappedCell()
        {
            BoardModel board = Grid(
                "GRBG",
                "BRGB",
                "RGRB",
                "GRBG");
            Vector2Int from = Cell(1, 0);
            Vector2Int to = Cell(1, 1);

            // The player drags the bottom R up into the gap. That one gem finishes both the row
            // of R's and the column of R's above it, so it's also where the two runs cross.
            board.Swap(from, to);
            BoardModel.ClearResult result = board.ClearMatches(board.FindMatchGroups(from, to));

            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(to, result.Created[0].Cell);
            Assert.AreEqual(PowerKind.Star, result.Created[0].Kind);
            Assert.AreEqual(new[]
            {
                "G.BG",
                "B.GB",
                ".R.B",
                "GGBG",
            }, grids.Rows(board));
            Assert.AreEqual(new[] { "....", "....", ".S..", "...." }, grids.PowerRows(board));
        }

        [Test]
        public void CrossWithARunOfFiveInIt_MakesNoStar()
        {
            // A row of 5 with a column of 3 standing on its middle. Runs cross here, but a match
            // containing a run of 5 is a Line5, not a Cross, and a Line5 makes a Hypercube (see
            // BoardModelHypercubeTests). It appears in the middle of the five.
            BoardModel board = Grid(
                "GBRBG",
                "BGRGB",
                "RRRRR");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(PowerKind.Hypercube, result.Created[0].Kind);
            Assert.AreEqual(Cell(2, 0), result.Created[0].Cell);
            Assert.AreEqual(6, result.Matched.Count);
            Assert.AreEqual(new[] { "GB.BG", "BG.GB", "..*.." }, grids.Rows(board));
        }

        [Test]
        public void NewStar_KeepsItsPowerWhenItFalls()
        {
            BoardModel board = Grid(
                "RRR",
                "GRB",
                "BRG");

            // The T leaves a Star at the top of the middle column, with two gaps under it.
            ClearAll(board);
            List<BoardModel.Fall> falls = board.Collapse();

            Assert.AreEqual(1, falls.Count);
            Assert.AreEqual(Cell(1, 0), falls[0].To);
            Assert.AreEqual(PowerKind.Star, falls[0].Piece.Power);
            Assert.AreEqual(new[] { "...", "...", ".S." }, grids.PowerRows(board));
            Assert.AreEqual(grids.Type('R'), board.TypeAt(1, 0));
        }

        // ---------------------------------------------------------------- The blast area

        [Test]
        public void StarBlastCells_InTheMiddle_IsItsRowThenTheRestOfItsColumn()
        {
            BoardModel board = Grid(Quiet);

            Assert.AreEqual("(0,2) (1,2) (2,2) (3,2) (4,2) (2,0) (2,1) (2,3) (2,4)",
                Describe(board.StarBlastCells(Cell(2, 2))));
        }

        [Test]
        public void StarBlastCells_OnAnEdgeOrInACorner_IsStillAWholeRowAndColumn()
        {
            BoardModel board = Grid(Quiet);

            // Unlike a Flame's 3x3, nothing is lost at the edge: the row and the column are the
            // same length wherever on them the Star sits.
            Assert.AreEqual("(0,2) (1,2) (2,2) (3,2) (4,2) (0,0) (0,1) (0,3) (0,4)",
                Describe(board.StarBlastCells(Cell(0, 2))));
            Assert.AreEqual("(0,0) (1,0) (2,0) (3,0) (4,0) (0,1) (0,2) (0,3) (0,4)",
                Describe(board.StarBlastCells(Cell(0, 0))));
            Assert.AreEqual("(0,4) (1,4) (2,4) (3,4) (4,4) (4,0) (4,1) (4,2) (4,3)",
                Describe(board.StarBlastCells(Cell(4, 4))));
        }

        [Test]
        public void StarBlastCells_OnABoardThatIsNotSquare_CoversWidthPlusHeightMinusOneCells()
        {
            BoardModel board = Grid(
                "ABCDEA",
                "CDEABC",
                "EABCDE");

            Assert.AreEqual("(0,1) (1,1) (2,1) (3,1) (4,1) (5,1) (4,0) (4,2)",
                Describe(board.StarBlastCells(Cell(4, 1))));
        }

        [Test]
        public void BlastCells_PicksTheBlastForThePower()
        {
            BoardModel board = Grid(Quiet);
            Vector2Int centre = Cell(2, 2);

            Assert.AreEqual(Describe(board.FlameBlastCells(centre)), Describe(board.BlastCells(PowerKind.Flame, centre)));
            Assert.AreEqual(Describe(board.StarBlastCells(centre)), Describe(board.BlastCells(PowerKind.Star, centre)));
            Assert.IsEmpty(board.BlastCells(PowerKind.None, centre), "An ordinary gem has no blast.");
            Assert.IsEmpty(board.BlastCells(PowerKind.Hypercube, centre), "A Hypercube is set off by a swap, not a match, so it has no blast.");
        }

        // ---------------------------------------------------------------- Detonating

        [Test]
        public void MatchedStar_InTheMiddle_ClearsExactlyItsRowAndColumn()
        {
            BoardModel board = Grid(
                "DEABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCDE");
            grids.SetPowers(board,
                ".....",
                ".....",
                "..S..",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "DE.BC",
                "BC.EA",
                ".....",
                "CD.AB",
                "AB.DE",
            }, grids.Rows(board));

            // "Matched" and "blasted" are reported separately, and no cell is in both.
            Assert.AreEqual("(1,2) (2,2) (3,2)", Describe(result.Matched));
            Assert.AreEqual(1, result.Detonations.Count);
            BoardModel.Detonation detonation = result.Detonations[0];
            Assert.AreEqual(PowerKind.Star, detonation.Kind);
            Assert.AreEqual(Cell(2, 2), detonation.Cell);
            Assert.AreEqual(grids.Type('R'), detonation.Piece.Colour, "The detonation should remember the gem's colour.");
            Assert.AreEqual("(0,2) (4,2) (2,0) (2,1) (2,3) (2,4)", Describe(detonation.Blasted));
            Assert.IsEmpty(result.Created, "A match of 3 makes no new gem, even with a Star in it.");
        }

        [Test]
        public void MatchedStar_OnAnEdge_ClearsExactlyItsRowAndColumn()
        {
            BoardModel board = Grid(
                "DEABC",
                "RCDEA",
                "RABCD",
                "RDEAB",
                "ABCDE");
            grids.SetPowers(board,
                ".....",
                ".....",
                "S....",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                ".EABC",
                ".CDEA",
                ".....",
                ".DEAB",
                ".BCDE",
            }, grids.Rows(board));
            Assert.AreEqual("(1,2) (2,2) (3,2) (4,2) (0,0) (0,4)", Describe(result.Detonations[0].Blasted));
        }

        [Test]
        public void MatchedStar_InACorner_ClearsExactlyItsRowAndColumn()
        {
            BoardModel board = Grid(
                "DEABC",
                "BCDEA",
                "EABCD",
                "CDEAB",
                "RRRDE");
            grids.SetPowers(board,
                ".....",
                ".....",
                ".....",
                ".....",
                "S....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                ".EABC",
                ".CDEA",
                ".ABCD",
                ".DEAB",
                ".....",
            }, grids.Rows(board));
            Assert.AreEqual("(3,0) (4,0) (0,1) (0,2) (0,3) (0,4)", Describe(result.Detonations[0].Blasted));
        }

        [Test]
        public void StarAtTheAnchorOfALineOfFour_GoesOff_AndAFlameTakesItsPlace()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "YRRRRY",
                "BGBGBG");
            grids.SetPowers(board,
                "......",
                "..S...",
                "......");

            BoardModel.ClearResult result = ClearAll(board);

            // The Star's beam takes the rest of its row and column, but not the Flame the line
            // of 4 left in its own cell: that one wasn't on the board when the match happened.
            Assert.AreEqual(new[]
            {
                "GB.BGB",
                "..R...",
                "BG.GBG",
            }, grids.Rows(board));
            Assert.AreEqual(new[]
            {
                "......",
                "..F...",
                "......",
            }, grids.PowerRows(board));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(1, result.Detonations.Count);
            Assert.AreEqual(PowerKind.Star, result.Detonations[0].Kind);
            Assert.AreEqual("(0,1) (5,1) (2,0) (2,2)", Describe(result.Detonations[0].Blasted));
        }

        [Test]
        public void StarInACross_GoesOff_AndANewStarIsLeftAtTheCrossing()
        {
            BoardModel board = Grid(
                "GRB",
                "RRR",
                "BRG");
            // The old Star is the top of the +, not its middle.
            grids.SetPowers(board,
                ".S.",
                "...",
                "...");

            BoardModel.ClearResult result = ClearAll(board);

            // Its beam clears the rest of the top row. Its column is the match itself, and the
            // new Star in the middle of that is left alone.
            Assert.AreEqual(new[]
            {
                "...",
                ".R.",
                "B.G",
            }, grids.Rows(board));
            Assert.AreEqual(new[] { "...", ".S.", "..." }, grids.PowerRows(board));
            Assert.AreEqual(Cell(1, 2), result.Detonations[0].Cell);
            Assert.AreEqual("(0,2) (2,2)", Describe(result.Detonations[0].Blasted));
            Assert.AreEqual(Cell(1, 1), result.Created[0].Cell);
        }

        [Test]
        public void BeamHittingOtherPowerGems_RemovesThemWithoutSettingThemOff()
        {
            BoardModel board = Grid(
                "DEABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCDE");
            // The E at (0,2) is a Star and the C at (2,0) is a Flame, but neither is part of the
            // match: they're only in the matched Star's row and column.
            grids.SetPowers(board,
                ".....",
                ".....",
                "S.S..",
                ".....",
                "..F..");

            BoardModel.ClearResult result = ClearAll(board);

            // Exactly the same gaps as with one Star: chain reactions are a later feature.
            Assert.AreEqual(1, result.Detonations.Count);
            Assert.AreEqual(new[]
            {
                "DE.BC",
                "BC.EA",
                ".....",
                "CD.AB",
                "AB.DE",
            }, grids.Rows(board));
            Assert.AreEqual(new[] { ".....", ".....", ".....", ".....", "....." }, grids.PowerRows(board));
        }

        [Test]
        public void TwoStarsInOneMatch_BothGoOff_AndNoCellIsBlastedTwice()
        {
            BoardModel board = Grid(
                "DEABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCDE");
            grids.SetPowers(board,
                ".....",
                ".....",
                ".S.S.",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "D.A.C",
                "B.D.A",
                ".....",
                "C.E.B",
                "A.C.E",
            }, grids.Rows(board));

            // Both Stars are in the same row. The first to go off gets what's left of it, so the
            // second only lists its own column.
            Assert.AreEqual(2, result.Detonations.Count);
            Assert.AreEqual("(0,2) (4,2) (1,0) (1,1) (1,3) (1,4)", Describe(result.Detonations[0].Blasted));
            Assert.AreEqual("(3,0) (3,1) (3,3) (3,4)", Describe(result.Detonations[1].Blasted));
        }

        [Test]
        public void FlameAndStarInOneMatch_EachGoesOffWithItsOwnBlast()
        {
            BoardModel board = Grid(
                "DEABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCDE");
            grids.SetPowers(board,
                ".....",
                ".....",
                ".F.S.",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            // The Flame takes the 3x3 around (1,2); the Star takes the rest of row 2 and all of
            // column 3.
            Assert.AreEqual(new[]
            {
                "DEA.C",
                "....A",
                ".....",
                "....B",
                "ABC.E",
            }, grids.Rows(board));
            Assert.AreEqual(2, result.Detonations.Count);
            Assert.AreEqual(PowerKind.Flame, result.Detonations[0].Kind);
            Assert.AreEqual("(0,1) (0,2) (0,3) (1,1) (1,3) (2,1) (2,3)", Describe(result.Detonations[0].Blasted));
            Assert.AreEqual(PowerKind.Star, result.Detonations[1].Kind);
            Assert.AreEqual("(4,2) (3,0) (3,1) (3,3) (3,4)", Describe(result.Detonations[1].Blasted));
        }

        // ---------------------------------------------------------------- The beam's timing

        // These two are about how the Board and the beam show a Star going off, not the model's
        // rules, but they're plain arithmetic, so they can be checked here without a scene.

        [Test]
        public void BlastDelay_ForAStar_GrowsWithDistanceFromTheGem()
        {
            Vector2Int gem = Cell(2, 3);
            const float beamSpeed = 10f; // cells per second

            Assert.AreEqual(0.1f, Board.BlastDelay(PowerKind.Star, gem, Cell(3, 3), beamSpeed), 0.0001f);
            Assert.AreEqual(0.1f, Board.BlastDelay(PowerKind.Star, gem, Cell(2, 2), beamSpeed), 0.0001f);
            Assert.AreEqual(0.2f, Board.BlastDelay(PowerKind.Star, gem, Cell(0, 3), beamSpeed), 0.0001f);
            Assert.AreEqual(0.4f, Board.BlastDelay(PowerKind.Star, gem, Cell(2, 7), beamSpeed), 0.0001f);
        }

        [Test]
        public void BlastDelay_ForAFlame_IsAlwaysZero()
        {
            // A Flame's blast hits its whole 3x3 at once.
            Assert.AreEqual(0f, Board.BlastDelay(PowerKind.Flame, Cell(2, 2), Cell(3, 3), 10f));
            Assert.AreEqual(0f, Board.BlastDelay(PowerKind.Flame, Cell(2, 2), Cell(1, 2), 10f));
        }

        [Test]
        public void StarBeam_ArmGrowsSteadily_ThenStopsAtItsFullLength()
        {
            const float speed = 10f; // units per second
            const float fullLength = 3f;

            Assert.AreEqual(0f, StarBeam.ArmLength(0f, speed, fullLength));
            Assert.AreEqual(1f, StarBeam.ArmLength(0.1f, speed, fullLength), 0.0001f);
            Assert.AreEqual(fullLength, StarBeam.ArmLength(0.3f, speed, fullLength), 0.0001f);
            Assert.AreEqual(fullLength, StarBeam.ArmLength(5f, speed, fullLength), "An arm stops at the edge of the board.");
        }

        [Test]
        public void StarBeam_StaysSolidWhileTravelling_ThenFadesOut()
        {
            const float travel = 0.4f;

            Assert.AreEqual(1f, StarBeam.Opacity(0f, travel));
            Assert.AreEqual(1f, StarBeam.Opacity(travel, travel));
            Assert.AreEqual(0.5f, StarBeam.Opacity(travel + StarBeam.FadeDuration / 2f, travel), 0.0001f);
            Assert.AreEqual(0f, StarBeam.Opacity(travel + StarBeam.FadeDuration, travel), 0.0001f);
            Assert.AreEqual(0f, StarBeam.Opacity(travel + 10f, travel));
        }
    }
}
