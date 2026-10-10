using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the Flame gem's rules in <see cref="BoardModel"/>: which matches make
    /// one and where (<see cref="BoardModel.ClearMatches"/>), what its blast clears when it's
    /// matched, and that it keeps its power when the board is rearranged
    /// (<see cref="BoardModel.Collapse"/>, <see cref="BoardModel.Shuffle"/> and
    /// <see cref="BoardModel.RedealKeepingPowers"/>).
    ///
    /// Most tests draw the board twice: once before, once as it should be afterwards. Power
    /// gems are drawn separately (see <see cref="TestGrids.SetPowers"/>): 'F' is a Flame.
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell.
    /// </summary>
    public class BoardModelFlameTests
    {
        // How many seeds the property tests try.
        private const int SeedCount = 200;

        // A 5x5 board with no matches on it: each row is the one below shifted two places.
        // Tests swap one row out for a row with R's in it, and since R appears nowhere else,
        // the R's are the only match.
        private static readonly string[] Quiet =
        {
            "DEABC", // y = 4
            "BCDEA",
            "EABCD",
            "CDEAB",
            "ABCDE", // y = 0
        };

        // The same idea at the real game's size, as in BoardModelMovesTests: no matches and no
        // valid moves, so it's a board that needs reshuffling.
        private static readonly string[] DeadBoard =
        {
            "EABCDEAB", // y = 7
            "CDEABCDE",
            "ABCDEABC",
            "DEABCDEA",
            "BCDEABCD",
            "EABCDEAB",
            "CDEABCDE",
            "ABCDEABC", // y = 0
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

        /// <summary>Finds the board's matches (as a cascade would) and clears them.</summary>
        private static BoardModel.ClearResult ClearAll(BoardModel board)
        {
            return board.ClearMatches(board.FindMatchGroups());
        }

        /// <summary>
        /// A copy of <paramref name="drawn"/> (powers included) that picks from the types A to E
        /// and has its own seed, for running the same board through many different shuffles.
        /// </summary>
        private BoardModel Seeded(int seed, BoardModel drawn)
        {
            var types = new List<TokenType>();
            foreach (char letter in "ABCDE")
            {
                types.Add(grids.Type(letter));
            }

            var board = new BoardModel(drawn.Width, drawn.Height, types, new System.Random(seed));
            for (int x = 0; x < drawn.Width; x++)
            {
                for (int y = 0; y < drawn.Height; y++)
                {
                    board.SetPieceAt(x, y, drawn.PieceAt(x, y));
                }
            }
            return board;
        }

        // ---------------------------------------------------------------- Creating a Flame

        [Test]
        public void HorizontalLineOfFour_LeavesAFlameOfItsColourAtTheCascadeAnchor()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "YRRRRY",
                "BGBGBG");

            // No swapped cells, so this is a cascade: the anchor is the left of the two middle cells.
            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "GBGBGB",
                "Y.R..Y",
                "BGBGBG",
            }, grids.Rows(board));
            Assert.AreEqual(new[]
            {
                "......",
                "..F...",
                "......",
            }, grids.PowerRows(board));

            // The result says the same thing, split up the way the Board animates it.
            Assert.AreEqual("(1,1) (3,1) (4,1)", Describe(result.Matched));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(Cell(2, 1), result.Created[0].Cell);
            Assert.AreEqual(PowerKind.Flame, result.Created[0].Kind);
            Assert.AreEqual(grids.Type('R'), result.Created[0].Piece.Colour);
            Assert.IsEmpty(result.Detonations, "A Flame must not go off in the step that made it.");
        }

        [Test]
        public void VerticalLineOfFour_LeavesAFlameAtTheLowerMiddleCell()
        {
            BoardModel board = Grid(
                "BGR",
                "GBR",
                "BGR",
                "GBR",
                "BGB");

            ClearAll(board);

            Assert.AreEqual(new[]
            {
                "BG.",
                "GB.",
                "BGR",
                "GB.",
                "BGB",
            }, grids.Rows(board));
            Assert.AreEqual(PowerKind.Flame, board.PieceAt(2, 2).Power);
        }

        [Test]
        public void LineOfFourMadeByASwap_LeavesTheFlameAtTheSwappedCell()
        {
            BoardModel board = Grid(
                "BGBRBG",
                "YRRGRY",
                "GBGBGB");
            Vector2Int from = Cell(3, 2);
            Vector2Int to = Cell(3, 1);

            // The player drags the top R down into the gap in the row of R's.
            board.Swap(from, to);
            BoardModel.ClearResult result = board.ClearMatches(board.FindMatchGroups(from, to));

            // Left to itself the anchor would be (2,1); the swapped cell wins.
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(to, result.Created[0].Cell);
            Assert.AreEqual(new[]
            {
                "BGBGBG",
                "Y..R.Y",
                "GBGBGB",
            }, grids.Rows(board));
            Assert.AreEqual(new[]
            {
                "......",
                "...F..",
                "......",
            }, grids.PowerRows(board));
        }

        [Test]
        public void LineOfThree_MakesNoFlame()
        {
            BoardModel board = Grid(
                "GBGB",
                "BGBG",
                "RRRG");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.IsEmpty(result.Created);
            Assert.AreEqual("(0,0) (1,0) (2,0)", Describe(result.Matched));
            Assert.AreEqual(new[] { "GBGB", "BGBG", "...G" }, grids.Rows(board));
        }

        [Test]
        public void LineOfFive_MakesNoFlame()
        {
            BoardModel board = Grid(
                "GBGBGBG",
                "YRRRRRY",
                "BGBGBGB");

            BoardModel.ClearResult result = ClearAll(board);

            // A line of 5 leaves a different gem behind: see BoardModelHypercubeTests.
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreNotEqual(PowerKind.Flame, result.Created[0].Kind);
            Assert.AreEqual(new[] { ".......", "...H...", "......." }, grids.PowerRows(board));
        }

        [Test]
        public void CrossWithARunOfFourInIt_MakesAStarNotAFlame()
        {
            // A row of 3 and a column of 4 sharing (1,0). There's a run of 4 here, but the
            // match as a whole is a Cross, not a Line4, and only a Line4 makes a Flame. A Cross
            // makes a Star instead (see BoardModelStarTests), where the two runs meet.
            BoardModel board = Grid(
                "GRBG",
                "BRGB",
                "GRBG",
                "RRRB");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(MatchShape.Cross, MatchGroup.Classify(new List<List<Vector2Int>>
            {
                new List<Vector2Int> { Cell(0, 0), Cell(1, 0), Cell(2, 0) },
                new List<Vector2Int> { Cell(1, 0), Cell(1, 1), Cell(1, 2), Cell(1, 3) },
            }));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(PowerKind.Star, result.Created[0].Kind);
            Assert.AreEqual(Cell(1, 0), result.Created[0].Cell);
            Assert.AreEqual(5, result.Matched.Count);
            Assert.AreEqual(new[] { "G.BG", "B.GB", "G.BG", ".R.B" }, grids.Rows(board));
        }

        [TestCase(MatchShape.Line3, PowerKind.None)]
        [TestCase(MatchShape.Line4, PowerKind.Flame)]
        [TestCase(MatchShape.Cross, PowerKind.Star)]
        [TestCase(MatchShape.Line5, PowerKind.Hypercube)]
        public void PowerMadeBy_EachShapeBiggerThanALineOfThreeMakesItsOwnGem(MatchShape shape, PowerKind expected)
        {
            Assert.AreEqual(expected, BoardModel.PowerMadeBy(shape));
        }

        // ---------------------------------------------------------------- The blast area

        [Test]
        public void FlameBlastCells_InTheMiddle_IsTheWhole3x3()
        {
            BoardModel board = Grid(Quiet);

            Assert.AreEqual("(1,1) (1,2) (1,3) (2,1) (2,2) (2,3) (3,1) (3,2) (3,3)",
                Describe(board.FlameBlastCells(Cell(2, 2))));
        }

        [Test]
        public void FlameBlastCells_OnAnEdge_IsClippedToSixCells()
        {
            BoardModel board = Grid(Quiet);

            Assert.AreEqual("(0,1) (0,2) (0,3) (1,1) (1,2) (1,3)", Describe(board.FlameBlastCells(Cell(0, 2))));
            Assert.AreEqual("(1,3) (1,4) (2,3) (2,4) (3,3) (3,4)", Describe(board.FlameBlastCells(Cell(2, 4))));
        }

        [Test]
        public void FlameBlastCells_InACorner_IsClippedToFourCells()
        {
            BoardModel board = Grid(Quiet);

            Assert.AreEqual("(0,0) (0,1) (1,0) (1,1)", Describe(board.FlameBlastCells(Cell(0, 0))));
            Assert.AreEqual("(3,3) (3,4) (4,3) (4,4)", Describe(board.FlameBlastCells(Cell(4, 4))));
        }

        // ---------------------------------------------------------------- Detonating

        [Test]
        public void MatchedFlame_InTheMiddle_ClearsThe3x3AroundIt()
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
                "..F..",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "DEABC",
                "B...A",
                "E...D",
                "C...B",
                "ABCDE",
            }, grids.Rows(board));

            // "Matched" and "blasted" are reported separately, and no cell is in both.
            Assert.AreEqual("(1,2) (2,2) (3,2)", Describe(result.Matched));
            Assert.AreEqual(1, result.Detonations.Count);
            BoardModel.Detonation detonation = result.Detonations[0];
            Assert.AreEqual(PowerKind.Flame, detonation.Kind);
            Assert.AreEqual(Cell(2, 2), detonation.Cell);
            Assert.AreEqual(grids.Type('R'), detonation.Piece.Colour, "The detonation should remember the gem's colour.");
            Assert.AreEqual("(1,1) (1,3) (2,1) (2,3) (3,1) (3,3)", Describe(detonation.Blasted));
            Assert.IsEmpty(result.Created, "A match of 3 makes no new gem, even with a Flame in it.");
        }

        [Test]
        public void MatchedFlame_OnAnEdge_BlastIsClippedAtTheEdge()
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
                "F....",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "DEABC",
                "..DEA",
                "..BCD",
                "..EAB",
                "ABCDE",
            }, grids.Rows(board));
            Assert.AreEqual("(1,1) (1,2) (1,3)", Describe(result.Detonations[0].Blasted));
        }

        [Test]
        public void MatchedFlame_InACorner_BlastIsClippedAtBothEdges()
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
                "F....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "DEABC",
                "BCDEA",
                "EABCD",
                "..EAB",
                "...DE",
            }, grids.Rows(board));
            Assert.AreEqual("(0,1) (1,1)", Describe(result.Detonations[0].Blasted));
        }

        [Test]
        public void ClearMatches_DoesNotApplyGravity()
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
                "..F..",
                ".....",
                ".....");

            ClearAll(board);

            // The top row is still hanging over the hole: nothing falls until Collapse is called,
            // so the Board can show the match and the blast before anything moves.
            Assert.AreEqual("DEABC", grids.Rows(board)[0]);
            Assert.AreEqual("B...A", grids.Rows(board)[1]);

            board.Collapse();
            Assert.AreEqual(new[]
            {
                "D...C",
                "B...A",
                "E...D",
                "CEABB",
                "ABCDE",
            }, grids.Rows(board));
        }

        [Test]
        public void FlameAtTheAnchorOfALineOfFour_GoesOff_AndANewFlameTakesItsPlace()
        {
            BoardModel board = Grid(
                "GBGBGB",
                "YRRRRY",
                "BGBGBG");
            grids.SetPowers(board,
                "......",
                "..F...",
                "......");

            BoardModel.ClearResult result = ClearAll(board);

            // The old Flame's blast takes the cells above and below the row, but not the new
            // Flame sitting in its own cell: that one wasn't on the board when the match happened.
            Assert.AreEqual(new[]
            {
                "G...GB",
                "Y.R..Y",
                "B...BG",
            }, grids.Rows(board));
            Assert.AreEqual(new[]
            {
                "......",
                "..F...",
                "......",
            }, grids.PowerRows(board));
            Assert.AreEqual(1, result.Created.Count);
            Assert.AreEqual(1, result.Detonations.Count);
            Assert.AreEqual(Cell(2, 1), result.Detonations[0].Cell);
            Assert.AreEqual("(1,0) (1,2) (2,0) (2,2) (3,0) (3,2)", Describe(result.Detonations[0].Blasted));
        }

        [Test]
        public void BlastHittingAnotherFlame_RemovesItWithoutSettingItOff()
        {
            BoardModel board = Grid(
                "DEABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCDE");
            // The D at (1,1) is a Flame too, but it isn't part of the match: it's only in the
            // matched Flame's blast.
            grids.SetPowers(board,
                ".....",
                ".....",
                "..F..",
                ".F...",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            // Exactly the same hole as with one Flame: chain reactions are a later feature.
            Assert.AreEqual(1, result.Detonations.Count);
            Assert.AreEqual(new[]
            {
                "DEABC",
                "B...A",
                "E...D",
                "C...B",
                "ABCDE",
            }, grids.Rows(board));
            Assert.AreEqual(new[] { ".....", ".....", ".....", ".....", "....." }, grids.PowerRows(board));
        }

        [Test]
        public void TwoFlamesInOneMatch_BothGoOff_AndNoCellIsBlastedTwice()
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
                ".F.F.",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "DEABC",
                ".....",
                ".....",
                ".....",
                "ABCDE",
            }, grids.Rows(board));

            // The two blasts overlap in column 2. Whichever Flame goes off first gets those
            // cells; the other one doesn't list them again.
            Assert.AreEqual(2, result.Detonations.Count);
            Assert.AreEqual("(0,1) (0,2) (0,3) (1,1) (1,3) (2,1) (2,3)", Describe(result.Detonations[0].Blasted));
            Assert.AreEqual("(3,1) (3,3) (4,1) (4,2) (4,3)", Describe(result.Detonations[1].Blasted));
        }

        // ---------------------------------------------------------------- Keeping the power

        [Test]
        public void NewFlame_KeepsItsPowerWhenItFalls()
        {
            BoardModel board = Grid(
                "BGR",
                "GBR",
                "BGR",
                "GBR",
                "BGB");

            // The column of 4 leaves a Flame at (2,2) with a gap under it at (2,1).
            ClearAll(board);
            List<BoardModel.Fall> falls = board.Collapse();

            Assert.AreEqual(1, falls.Count);
            Assert.AreEqual(Cell(2, 1), falls[0].To);
            Assert.AreEqual(PowerKind.Flame, falls[0].Piece.Power);
            Assert.AreEqual(new[] { "...", "...", "...", "..F", "..." }, grids.PowerRows(board));
            Assert.AreEqual(grids.Type('R'), board.TypeAt(2, 1));
        }

        [Test]
        public void Shuffle_ManySeeds_TheFlameMovesWithItsPiece()
        {
            var flameCell = new Vector2Int(3, 4);
            BoardModel drawn = Grid(DeadBoard);
            TokenType flameColour = drawn.TypeAt(flameCell);
            drawn.SetPieceAt(flameCell.x, flameCell.y, new Piece(flameColour, PowerKind.Flame));

            for (int seed = 0; seed < SeedCount; seed++)
            {
                BoardModel board = Seeded(seed, drawn);

                List<BoardModel.Move> moves = board.Shuffle();

                Assert.IsNotNull(moves, $"Seed {seed}: the shuffle gave up.");
                Assert.AreEqual(1, CountPower(board, PowerKind.Flame), $"Seed {seed}: there should still be exactly one Flame.");
                // The move that started on the Flame's cell says where it went.
                BoardModel.Move flameMove = moves.Find(move => move.From == flameCell);
                Piece moved = board.PieceAt(flameMove.To);
                Assert.AreEqual(PowerKind.Flame, moved.Power, $"Seed {seed}: the Flame's piece lost its power.");
                Assert.AreSame(flameColour, moved.Colour, $"Seed {seed}: the Flame changed colour.");
            }
        }

        [Test]
        public void RedealKeepingPowers_ManySeeds_KeepsEveryPowerAndLeavesAPlayableBoard()
        {
            // A board Shuffle can't fix: every piece is the same colour, so however they're
            // arranged it's one giant match. It holds one of each power gem, and one gap.
            BoardModel drawn = Grid(
                "AAAAA*",
                "AAAAAA",
                "AAA.AA",
                "AAAAAA",
                "AAAAAA",
                "AAAAAA");
            grids.SetPowers(drawn,
                "......",
                "......",
                "......",
                "..F...",
                "......",
                "S.....");
            string[] powersBefore = grids.PowerRows(drawn);

            for (int seed = 0; seed < SeedCount; seed++)
            {
                BoardModel board = Seeded(seed, drawn);
                Assert.IsNull(board.Shuffle(), "This board is meant to be one Shuffle can't fix.");

                Assert.IsTrue(board.RedealKeepingPowers(), $"Seed {seed} gave up.");

                string drawnAfter = string.Join("\n", grids.Rows(board));
                Assert.IsEmpty(board.FindMatches(), $"Seed {seed}: the redeal left a match:\n{drawnAfter}");
                Assert.IsTrue(board.HasValidMove(), $"Seed {seed}: the redeal left no valid move:\n{drawnAfter}");
                // Every power gem is still where it was...
                Assert.AreEqual(powersBefore, grids.PowerRows(board), $"Seed {seed}: the redeal changed the power gems.");
                // ...the Hypercube still has no colour, and the gap now holds an ordinary piece.
                Assert.IsNull(board.TypeAt(5, 5), $"Seed {seed}: the Hypercube was given a colour.");
                Assert.IsNotNull(board.TypeAt(3, 3), $"Seed {seed}: the empty cell wasn't filled.");
                Assert.IsNotNull(board.TypeAt(2, 2), $"Seed {seed}: the Flame lost its colour.");
            }
        }

        [Test]
        public void RedealKeepingPowers_BoardTooSmallForAMove_ReturnsFalseButKeepsThePower()
        {
            BoardModel drawn = Grid(
                "AB",
                "BA");
            grids.SetPowers(drawn,
                "..",
                "F.");
            BoardModel board = Seeded(0, drawn);

            Assert.IsFalse(board.RedealKeepingPowers());

            Assert.AreEqual(new[] { "..", "F." }, grids.PowerRows(board));
        }

        private static int CountPower(BoardModel board, PowerKind power)
        {
            int count = 0;
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    if (board.PieceAt(x, y).Power == power)
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
