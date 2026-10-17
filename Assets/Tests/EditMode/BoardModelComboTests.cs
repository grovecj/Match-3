using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for power gem combos in <see cref="BoardModel"/>: swapping two Flames or
    /// Stars with each other is always a move (<see cref="BoardModel.IsComboSwap"/>), and sets
    /// off one bigger blast (<see cref="BoardModel.ActivateCombo"/>) of the shape the pair makes
    /// (<see cref="BoardModel.BigFlameBlastCells"/>, <see cref="BoardModel.StarCrossBlastCells"/>).
    ///
    /// Power gems are drawn separately (see <see cref="TestGrids.SetPowers"/>): 'F' is a Flame,
    /// 'S' a Star. The first string is the TOP row, and (0, 0) is the bottom-left cell.
    /// </summary>
    public class BoardModelComboTests
    {
        // A 7x7 board with no matches: each row is the one below shifted two places, so no
        // letter repeats along a row or a column. Big enough for a 5x5 to fit in the middle.
        private static readonly string[] Quiet =
        {
            "FGABCDE", // y = 6
            "DEFGABC",
            "BCDEFGA",
            "GABCDEF", // y = 3
            "EFGABCD",
            "CDEFGAB",
            "ABCDEFG", // y = 0
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

        private static Vector2Int Cell(int x, int y)
        {
            return new Vector2Int(x, y);
        }

        private static string Describe(IEnumerable<Vector2Int> cells)
        {
            var parts = new List<string>();
            foreach (Vector2Int cell in cells)
            {
                parts.Add($"({cell.x},{cell.y})");
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// The quiet 7x7 with the two gems marked: <paramref name="movedPower"/> in
        /// <paramref name="moved"/> and <paramref name="partnerPower"/> in <paramref name="partner"/>
        /// ('F' or 'S').
        /// </summary>
        private BoardModel QuietWithGems(Vector2Int moved, char movedPower, Vector2Int partner, char partnerPower)
        {
            BoardModel board = grids.Build(Quiet);
            var powers = new char[board.Height][];
            for (int i = 0; i < board.Height; i++)
            {
                powers[i] = new string('.', board.Width).ToCharArray();
            }
            powers[board.Height - 1 - moved.y][moved.x] = movedPower;
            powers[board.Height - 1 - partner.y][partner.x] = partnerPower;

            var rows = new string[board.Height];
            for (int i = 0; i < board.Height; i++)
            {
                rows[i] = new string(powers[i]);
            }
            grids.SetPowers(board, rows);
            return board;
        }

        /// <summary>Every cell of <paramref name="result"/>'s detonations' Blasted lists, plus the gems' own cells.</summary>
        private static HashSet<Vector2Int> EverythingCleared(BoardModel.ClearResult result)
        {
            var cleared = new HashSet<Vector2Int>();
            foreach (BoardModel.Detonation detonation in result.Detonations)
            {
                cleared.Add(detonation.Cell);
                cleared.UnionWith(detonation.Blasted);
            }
            return cleared;
        }

        private static int CountNonEmpty(BoardModel board)
        {
            int count = 0;
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    if (!board.PieceAt(x, y).IsEmpty)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        // ---------------------------------------------------------------- Which swaps combine

        [Test]
        public void ComboFor_PairsOfFlamesAndStars()
        {
            Assert.AreEqual(PowerCombo.BigFlame, BoardModel.ComboFor(PowerKind.Flame, PowerKind.Flame));
            Assert.AreEqual(PowerCombo.StarCross, BoardModel.ComboFor(PowerKind.Star, PowerKind.Star));
            Assert.AreEqual(PowerCombo.StarCross, BoardModel.ComboFor(PowerKind.Flame, PowerKind.Star));
            Assert.AreEqual(PowerCombo.StarCross, BoardModel.ComboFor(PowerKind.Star, PowerKind.Flame));
        }

        [Test]
        public void ComboFor_PairsWithAHypercube()
        {
            Assert.AreEqual(PowerCombo.Conversion, BoardModel.ComboFor(PowerKind.Hypercube, PowerKind.Flame));
            Assert.AreEqual(PowerCombo.Conversion, BoardModel.ComboFor(PowerKind.Star, PowerKind.Hypercube));
            Assert.AreEqual(PowerCombo.ClearBoard, BoardModel.ComboFor(PowerKind.Hypercube, PowerKind.Hypercube));
        }

        [Test]
        public void ComboFor_AnythingWithAnOrdinaryGem_IsNone()
        {
            Assert.AreEqual(PowerCombo.None, BoardModel.ComboFor(PowerKind.Flame, PowerKind.None));
            Assert.AreEqual(PowerCombo.None, BoardModel.ComboFor(PowerKind.None, PowerKind.Star));
            Assert.AreEqual(PowerCombo.None, BoardModel.ComboFor(PowerKind.Hypercube, PowerKind.None));
            Assert.AreEqual(PowerCombo.None, BoardModel.ComboFor(PowerKind.None, PowerKind.None));
        }

        [Test]
        public void IsComboSwap_TwoFlamesOfDifferentColours_IsTrue_WhicheverWayRound()
        {
            BoardModel board = grids.Build("RG");
            grids.SetPowers(board, "FF");

            Assert.IsTrue(board.IsComboSwap(Cell(0, 0), Cell(1, 0)));
            Assert.IsTrue(board.IsComboSwap(Cell(1, 0), Cell(0, 0)));
        }

        [Test]
        public void IsComboSwap_PowerGemAndOrdinaryGem_IsFalse()
        {
            BoardModel board = grids.Build("RG");
            grids.SetPowers(board, "S.");

            Assert.IsFalse(board.IsComboSwap(Cell(0, 0), Cell(1, 0)));
        }

        [Test]
        public void IsComboSwap_FlameAndHypercube_IsTrue_AsWellAsAHypercubeSwap()
        {
            BoardModel board = grids.Build("R*");
            grids.SetPowers(board, "F.");

            // Both are true; the Board asks about the combo first, so the combo wins.
            Assert.IsTrue(board.IsComboSwap(Cell(0, 0), Cell(1, 0)));
            Assert.IsTrue(board.IsHypercubeSwap(Cell(0, 0), Cell(1, 0)));
        }

        [Test]
        public void IsComboSwap_HypercubeAndOrdinaryGem_IsFalse_ItStaysAHypercubeSwap()
        {
            BoardModel board = grids.Build("R*");

            Assert.IsFalse(board.IsComboSwap(Cell(0, 0), Cell(1, 0)));
            Assert.IsTrue(board.IsHypercubeSwap(Cell(0, 0), Cell(1, 0)));
        }

        // ---------------------------------------------------------------- Valid moves

        [Test]
        public void HasValidMove_DeadBoardWhoseOnlyMoveIsACombo_IsPlayable()
        {
            // Every row and column holds one of each type, so no swap can line three up...
            string[] dead = { "ABC", "CAB", "BCA" };
            Assert.IsFalse(grids.Build(dead).HasValidMove());

            // ...but make two neighbours a Flame and a Star and swapping them is a move.
            BoardModel board = grids.Build(dead);
            grids.SetPowers(board,
                "...",
                "...",
                "FS.");

            Assert.IsTrue(board.HasValidMove());
            Assert.IsTrue(board.TryFindValidMove(out Vector2Int a, out Vector2Int b));
            Assert.IsTrue(board.IsComboSwap(a, b), "The only move on this board is the combo.");
            // Looking for a move mustn't make one.
            Assert.AreEqual(dead, grids.Rows(board));
            Assert.AreEqual(new[] { "...", "...", "FS." }, grids.PowerRows(board));
        }

        // ---------------------------------------------------------------- Blast shapes

        [Test]
        public void BigFlameBlastCells_InTheMiddle_IsThe5x5()
        {
            BoardModel board = grids.Build(Quiet);

            List<Vector2Int> cells = board.BigFlameBlastCells(Cell(3, 3));

            Assert.AreEqual(25, cells.Count);
            foreach (Vector2Int cell in cells)
            {
                Assert.LessOrEqual(Mathf.Abs(cell.x - 3), 2, $"{cell} is outside the 5x5.");
                Assert.LessOrEqual(Mathf.Abs(cell.y - 3), 2, $"{cell} is outside the 5x5.");
            }
        }

        [Test]
        public void BigFlameBlastCells_AtAnEdge_IsClipped()
        {
            BoardModel board = grids.Build(Quiet);

            // Three columns (x = 0..2) by five rows (y = 1..5).
            Assert.AreEqual(15, board.BigFlameBlastCells(Cell(0, 3)).Count);
        }

        [Test]
        public void BigFlameBlastCells_InACorner_IsTheClipped3x3()
        {
            BoardModel board = grids.Build(Quiet);

            Assert.AreEqual(
                "(0,0) (0,1) (0,2) (1,0) (1,1) (1,2) (2,0) (2,1) (2,2)",
                Describe(board.BigFlameBlastCells(Cell(0, 0))));
        }

        [Test]
        public void StarCrossBlastCells_InTheMiddle_IsThreeRowsAndThreeColumns_EachCellOnce()
        {
            BoardModel board = grids.Build(Quiet);

            List<Vector2Int> cells = board.StarCrossBlastCells(Cell(3, 3));

            // Three rows of 7, plus the 4 cells of each of the three columns outside those rows.
            Assert.AreEqual(3 * 7 + 3 * 4, cells.Count);
            Assert.AreEqual(cells.Count, new HashSet<Vector2Int>(cells).Count, "No cell is listed twice.");
            foreach (Vector2Int cell in cells)
            {
                bool inARow = Mathf.Abs(cell.y - 3) <= 1;
                bool inAColumn = Mathf.Abs(cell.x - 3) <= 1;
                Assert.IsTrue(inARow || inAColumn, $"{cell} is in none of the three rows or columns.");
            }
        }

        [Test]
        public void StarCrossBlastCells_AtAnEdge_HasTwoColumns()
        {
            BoardModel board = grids.Build(Quiet);

            // Rows y = 2..4 (21 cells) and columns x = 0 and 1 outside them (4 cells each).
            Assert.AreEqual(3 * 7 + 2 * 4, board.StarCrossBlastCells(Cell(0, 3)).Count);
        }

        [Test]
        public void StarCrossBlastCells_InACorner_HasTwoRowsAndTwoColumns()
        {
            BoardModel board = grids.Build(Quiet);

            // Rows y = 0 and 1 (14 cells) and columns x = 0 and 1 above them (5 cells each).
            Assert.AreEqual(2 * 7 + 2 * 5, board.StarCrossBlastCells(Cell(0, 0)).Count);
        }

        // ---------------------------------------------------------------- Setting a combo off

        [Test]
        public void ActivateCombo_TwoFlames_ClearsThe5x5AroundTheMovedGem()
        {
            BoardModel board = QuietWithGems(Cell(3, 3), 'F', Cell(4, 3), 'F');

            BoardModel.ClearResult result = board.ActivateCombo(Cell(3, 3), Cell(4, 3));

            CollectionAssert.AreEquivalent(board.BigFlameBlastCells(Cell(3, 3)), EverythingCleared(result));
            Assert.AreEqual(49 - 25, CountNonEmpty(board));
        }

        [Test]
        public void ActivateCombo_TwoFlamesInACorner_ClearsTheClipped3x3()
        {
            BoardModel board = QuietWithGems(Cell(0, 0), 'F', Cell(0, 1), 'F');

            BoardModel.ClearResult result = board.ActivateCombo(Cell(0, 0), Cell(0, 1));

            Assert.AreEqual(9, EverythingCleared(result).Count);
            Assert.AreEqual(49 - 9, CountNonEmpty(board));
        }

        [Test]
        public void ActivateCombo_FlameAndStar_ClearsThreeRowsAndThreeColumns_WhicheverWasMoved()
        {
            // The Flame is the gem the player moved: the pair still makes the Star's shape.
            BoardModel board = QuietWithGems(Cell(3, 3), 'F', Cell(3, 4), 'S');

            BoardModel.ClearResult result = board.ActivateCombo(Cell(3, 3), Cell(3, 4));

            Assert.AreEqual(PowerCombo.StarCross, result.Waves[0][0].Combo);
            CollectionAssert.AreEquivalent(board.StarCrossBlastCells(Cell(3, 3)), EverythingCleared(result));
            Assert.AreEqual(49 - 33, CountNonEmpty(board));
        }

        [Test]
        public void ActivateCombo_TwoStarsAtAnEdge_ClearsTwoColumns()
        {
            BoardModel board = QuietWithGems(Cell(6, 3), 'S', Cell(5, 3), 'S');

            BoardModel.ClearResult result = board.ActivateCombo(Cell(6, 3), Cell(5, 3));

            CollectionAssert.AreEquivalent(board.StarCrossBlastCells(Cell(6, 3)), EverythingCleared(result));
            Assert.AreEqual(3 * 7 + 2 * 4, EverythingCleared(result).Count);
        }

        [Test]
        public void ActivateCombo_FirstWave_IsTheMovedGemWithTheBlastThenThePartnerWithNothing()
        {
            BoardModel board = QuietWithGems(Cell(3, 3), 'F', Cell(4, 3), 'F');

            BoardModel.ClearResult result = board.ActivateCombo(Cell(3, 3), Cell(4, 3));

            Assert.AreEqual(1, result.Waves.Count, "Nothing else went off.");
            Assert.AreEqual(2, result.Waves[0].Count);

            BoardModel.Detonation moved = result.Waves[0][0];
            Assert.AreEqual(Cell(3, 3), moved.Cell);
            Assert.AreEqual(PowerCombo.BigFlame, moved.Combo);
            Assert.IsFalse(moved.IsComboPartner);

            BoardModel.Detonation partner = result.Waves[0][1];
            Assert.AreEqual(Cell(4, 3), partner.Cell);
            Assert.AreEqual(PowerKind.Flame, partner.Kind);
            Assert.IsTrue(partner.IsComboPartner);
            Assert.That(partner.Blasted, Is.Empty);

            Assert.That(result.Matched, Is.Empty);
            Assert.That(result.Created, Is.Empty);
        }

        [Test]
        public void ActivateCombo_TheTwoGemsAreNotCountedAsBlasted()
        {
            BoardModel board = QuietWithGems(Cell(3, 3), 'F', Cell(4, 3), 'F');

            BoardModel.ClearResult result = board.ActivateCombo(Cell(3, 3), Cell(4, 3));
            ClearStep step = ClearStep.From(result, new List<MatchGroup>(), cascadeIndex: 0);

            foreach (BoardModel.Detonation detonation in result.Detonations)
            {
                CollectionAssert.DoesNotContain(detonation.Blasted, Cell(3, 3));
                CollectionAssert.DoesNotContain(detonation.Blasted, Cell(4, 3));
            }
            Assert.That(step.RunLengths, Is.Empty);
            Assert.AreEqual(23, step.BlastedGems, "The 5x5 minus the two combo gems.");
            Assert.AreEqual(25, step.GemsCleared, "The two gems did leave the board.");
            Assert.AreEqual(23 * ScoreRules.PointsPerBlastedGem, step.Points, "Step 0 is x1.");
        }

        [Test]
        public void ActivateCombo_PowerGemCaughtInTheBlast_GoesOffInTheNextWave()
        {
            // A Star in the 5x5's corner, a Flame just outside it.
            BoardModel board = QuietWithGems(Cell(3, 3), 'F', Cell(4, 3), 'F');
            board.SetPieceAt(5, 5, new Piece(board.TypeAt(5, 5), PowerKind.Star));

            BoardModel.ClearResult result = board.ActivateCombo(Cell(3, 3), Cell(4, 3));

            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual(1, result.Waves[1].Count);
            Assert.AreEqual(Cell(5, 5), result.Waves[1][0].Cell);
            Assert.AreEqual(PowerKind.Star, result.Waves[1][0].Kind);
            Assert.AreEqual(PowerCombo.None, result.Waves[1][0].Combo, "A caught gem goes off on its own.");
            // The Star's row and column reach past the 5x5.
            Assert.IsTrue(board.PieceAt(5, 0).IsEmpty);
        }

        [Test]
        public void ActivateCombo_SwapThatAlsoLinesUpARun_IsTheComboOnly()
        {
            // As the board is after the swap: the moved Flame at (3,3) has landed at the end of a
            // line of four R's, which on its own would leave a new Flame behind.
            BoardModel board = grids.Build(
                "FGABCDE",
                "DEFGABC",
                "BCDEFGA",
                "RRRRXEF", // y = 3
                "EFGABCD",
                "CDEFGAB",
                "ABCDEFG");
            board.SetPieceAt(3, 3, new Piece(grids.Type('R'), PowerKind.Flame));
            board.SetPieceAt(4, 3, new Piece(grids.Type('X'), PowerKind.Flame));
            Assert.That(board.FindMatchGroups(Cell(4, 3), Cell(3, 3)), Is.Not.Empty, "The swap does line up a run.");
            Assert.IsTrue(board.IsComboSwap(Cell(4, 3), Cell(3, 3)));

            BoardModel.ClearResult result = board.ActivateCombo(Cell(3, 3), Cell(4, 3));

            Assert.That(result.Matched, Is.Empty, "Nothing is cleared as a match.");
            Assert.That(result.Created, Is.Empty, "No power gem is made.");
            CollectionAssert.AreEquivalent(board.BigFlameBlastCells(Cell(3, 3)), EverythingCleared(result));
            // The R at (0,3) is outside the 5x5, so the run's leftover stays: it was never matched.
            Assert.AreEqual(grids.Type('R'), board.TypeAt(0, 3));
        }

        [Test]
        public void ActivateCombo_OnCellsThatAreNotAFlameStarPair_Throws()
        {
            BoardModel board = QuietWithGems(Cell(3, 3), 'F', Cell(5, 5), 'F');

            // (4,3) is an ordinary gem.
            Assert.Throws<System.ArgumentException>(() => board.ActivateCombo(Cell(3, 3), Cell(4, 3)));
        }

        // ---------------------------------------------------------------- Hypercube combos

        /// <summary>
        /// An 8x8 board with no matches, the same trick as <see cref="Quiet"/>: each row is the one
        /// below shifted two places, so no letter repeats along a row or next to itself up a column.
        /// </summary>
        private BoardModel Quiet8x8()
        {
            const string letters = "ABCDEFGH";
            var rows = new string[8];
            for (int y = 0; y < 8; y++)
            {
                var row = new char[8];
                for (int x = 0; x < 8; x++)
                {
                    row[x] = letters[(x + 2 * y) % 8];
                }
                // rows[0] is the top row.
                rows[7 - y] = new string(row);
            }
            return grids.Build(rows);
        }

        [Test]
        public void HasValidMove_TwoAdjacentHypercubes_IsAMove()
        {
            // Nothing but the two cubes: before #109 this board had no move at all.
            BoardModel board = grids.Build("**");

            Assert.IsTrue(board.HasValidMove());
            Assert.IsTrue(board.TryFindValidMove(out Vector2Int a, out Vector2Int b));
            Assert.AreEqual(Cell(0, 0), a);
            Assert.AreEqual(Cell(1, 0), b);
        }

        [Test]
        public void HasValidMove_TwoHypercubesNotNextToEachOther_IsNoMove()
        {
            BoardModel board = grids.Build("*.*");

            Assert.IsFalse(board.HasValidMove());
        }

        [Test]
        public void Conversion_TurnsTheColourIntoFlames_ThenTheCubeSetsThemAllOff()
        {
            // Quiet has one A in every row: seven in all. The Flame partner is the A at (0,0),
            // the cube sits next to it, and the A at (1,3) is already a Star.
            BoardModel board = grids.Build(Quiet);
            TokenType a = grids.Type('A');
            board.SetPieceAt(0, 0, new Piece(a, PowerKind.Flame));
            board.SetPieceAt(1, 0, Piece.Hypercube);
            board.SetPieceAt(1, 3, new Piece(a, PowerKind.Star));
            List<Vector2Int> allAs = board.CellsOfColour(a);
            Assert.AreEqual(7, allAs.Count);

            // The Flame is the gem the player moved: the cube goes off all the same.
            BoardModel.ClearResult result = board.ActivateCombo(Cell(0, 0), Cell(1, 0));

            // The five ordinary A's became Flames; the partner and the Star aren't listed.
            Assert.AreEqual(5, result.Converted.Count);
            foreach (BoardModel.Creation conversion in result.Converted)
            {
                Assert.AreEqual(PowerKind.Flame, conversion.Kind);
                Assert.AreEqual(a, conversion.Piece.Colour);
                Assert.AreNotEqual(Cell(0, 0), conversion.Cell);
                Assert.AreNotEqual(Cell(1, 3), conversion.Cell);
            }
            Assert.That(result.Matched, Is.Empty);
            Assert.That(result.Created, Is.Empty, "Converted gems are not made gems.");

            // Wave 0: the cube alone, zapping every A.
            Assert.AreEqual(1, result.Waves[0].Count);
            BoardModel.Detonation cube = result.Waves[0][0];
            Assert.AreEqual(PowerKind.Hypercube, cube.Kind);
            Assert.AreEqual(Cell(1, 0), cube.Cell);
            Assert.AreEqual(PowerCombo.Conversion, cube.Combo);
            Assert.AreEqual(a, cube.Target);
            CollectionAssert.AreEquivalent(allAs, cube.Blasted);

            // Wave 1: every one of them goes off, the Star still as a Star.
            Assert.AreEqual(7, result.Waves[1].Count);
            int flames = 0;
            foreach (BoardModel.Detonation detonation in result.Waves[1])
            {
                if (detonation.Cell == Cell(1, 3))
                {
                    Assert.AreEqual(PowerKind.Star, detonation.Kind, "A Star of the colour keeps its power.");
                }
                else
                {
                    Assert.AreEqual(PowerKind.Flame, detonation.Kind);
                    flames++;
                }
            }
            Assert.AreEqual(6, flames, "The five converted gems and the partner.");
        }

        [Test]
        public void Conversion_EachGemOfTheColourIsABlastedGem_TheCubeIsNot()
        {
            BoardModel board = grids.Build(Quiet);
            board.SetPieceAt(0, 0, new Piece(grids.Type('A'), PowerKind.Flame));
            board.SetPieceAt(1, 0, Piece.Hypercube);

            BoardModel.ClearResult result = board.ActivateCombo(Cell(1, 0), Cell(0, 0));
            ClearStep step = ClearStep.From(result, new List<MatchGroup>(), cascadeIndex: 0);

            foreach (BoardModel.Detonation detonation in result.Detonations)
            {
                CollectionAssert.DoesNotContain(detonation.Blasted, Cell(1, 0));
            }
            // At least the seven A's; their Flames' 3x3s add more.
            Assert.That(step.BlastedGems, Is.GreaterThanOrEqualTo(7));
            Assert.AreEqual(step.BlastedGems * ScoreRules.PointsPerBlastedGem, step.Points, "Step 0 is x1.");
        }

        [Test]
        public void Conversion_WithNoOtherGemOfTheColour_ConvertsOnlyThePartner()
        {
            // The R Flame is the only R on the board.
            BoardModel board = grids.Build("GR*");
            grids.SetPowers(board, ".F.");

            BoardModel.ClearResult result = board.ActivateCombo(Cell(2, 0), Cell(1, 0));

            Assert.That(result.Converted, Is.Empty, "The partner already was a Flame.");
            CollectionAssert.AreEqual(new[] { Cell(1, 0) }, result.Waves[0][0].Blasted);
            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual(PowerKind.Flame, result.Waves[1][0].Kind);
            // The Flame's 3x3 takes the G with it.
            CollectionAssert.AreEqual(new[] { Cell(0, 0) }, result.Waves[1][0].Blasted);
        }

        [Test]
        public void Conversion_SwapThatAlsoLinesUpARun_IsTheComboOnly()
        {
            // As the board is after the swap: the R Flame at (3,3) has landed at the end of a
            // line of four R's, and the cube is next to it. A run, a Hypercube swap and a combo
            // all at once: the combo comes first.
            BoardModel board = grids.Build(
                "FGABCDE",
                "DEFGABC",
                "BCDEFGA",
                "RRRR*EF", // y = 3
                "EFGABCD",
                "CDEFGAB",
                "ABCDEFG");
            board.SetPieceAt(3, 3, new Piece(grids.Type('R'), PowerKind.Flame));
            Assert.That(board.FindMatchGroups(Cell(4, 3), Cell(3, 3)), Is.Not.Empty, "The swap does line up a run.");
            Assert.IsTrue(board.IsHypercubeSwap(Cell(4, 3), Cell(3, 3)));
            Assert.IsTrue(board.IsComboSwap(Cell(4, 3), Cell(3, 3)));

            BoardModel.ClearResult result = board.ActivateCombo(Cell(3, 3), Cell(4, 3));

            Assert.That(result.Matched, Is.Empty, "Nothing is cleared as a match.");
            Assert.That(result.Created, Is.Empty, "No power gem is made.");
            // A plain Hypercube swap wouldn't have converted the three ordinary R's.
            Assert.AreEqual(3, result.Converted.Count);
            Assert.AreEqual(PowerCombo.Conversion, result.Waves[0][0].Combo);
        }

        [Test]
        public void ClearBoard_DestroysAll62OtherPieces_AndCaughtPowerGemsBlastNothing()
        {
            BoardModel board = Quiet8x8();
            board.SetPieceAt(3, 3, Piece.Hypercube);
            board.SetPieceAt(4, 3, Piece.Hypercube);
            // Caught power gems of every kind, including a third Hypercube.
            board.SetPieceAt(0, 0, new Piece(board.TypeAt(0, 0), PowerKind.Flame));
            board.SetPieceAt(5, 5, new Piece(board.TypeAt(5, 5), PowerKind.Star));
            board.SetPieceAt(7, 7, Piece.Hypercube);

            BoardModel.ClearResult result = board.ActivateCombo(Cell(4, 3), Cell(3, 3));

            Assert.AreEqual(0, CountNonEmpty(board), "Nothing is left.");
            Assert.AreEqual(2, result.Waves.Count);

            BoardModel.Detonation moved = result.Waves[0][0];
            Assert.AreEqual(Cell(4, 3), moved.Cell);
            Assert.AreEqual(PowerCombo.ClearBoard, moved.Combo);
            Assert.AreEqual(62, moved.Blasted.Count);
            CollectionAssert.DoesNotContain(moved.Blasted, Cell(3, 3));

            BoardModel.Detonation partner = result.Waves[0][1];
            Assert.AreEqual(PowerKind.Hypercube, partner.Kind);
            Assert.IsTrue(partner.IsComboPartner);
            Assert.That(partner.Blasted, Is.Empty);

            Assert.AreEqual(3, result.Waves[1].Count);
            foreach (BoardModel.Detonation caught in result.Waves[1])
            {
                Assert.That(caught.Blasted, Is.Empty, $"The {caught.Kind} at {caught.Cell} had nothing left to hit.");
            }

            ClearStep step = ClearStep.From(result, new List<MatchGroup>(), cascadeIndex: 0);
            Assert.AreEqual(62, step.BlastedGems);
            Assert.AreEqual(64, step.GemsCleared);
            Assert.AreEqual(15500, step.Points, "The biggest single step in the game, at x1.");
        }

        [Test]
        public void ClearBoard_OnABoardOfPowerGems_Ends()
        {
            // Every piece a power gem: Flames and Stars in a checkerboard, two cubes in the middle.
            BoardModel board = Quiet8x8();
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    PowerKind power = (x + y) % 2 == 0 ? PowerKind.Flame : PowerKind.Star;
                    board.SetPieceAt(x, y, new Piece(board.TypeAt(x, y), power));
                }
            }
            board.SetPieceAt(3, 3, Piece.Hypercube);
            board.SetPieceAt(4, 3, Piece.Hypercube);

            BoardModel.ClearResult result = board.ActivateCombo(Cell(4, 3), Cell(3, 3));

            Assert.AreEqual(64, result.Detonations.Count, "Every piece went off exactly once.");
            Assert.AreEqual(0, CountNonEmpty(board));
        }

        [Test]
        public void Conversion_OnABoardOfPowerGems_Ends()
        {
            // Every piece a Star except the cube: the colour's Stars go off, their beams catch
            // everything else, and that's the end of it.
            BoardModel board = Quiet8x8();
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    board.SetPieceAt(x, y, new Piece(board.TypeAt(x, y), PowerKind.Star));
                }
            }
            board.SetPieceAt(1, 0, Piece.Hypercube);

            BoardModel.ClearResult result = board.ActivateCombo(Cell(1, 0), Cell(0, 0));

            Assert.That(result.Converted, Is.Empty, "Every gem already had a power.");
            Assert.AreEqual(64, result.Detonations.Count, "Every piece went off exactly once.");
            Assert.AreEqual(0, CountNonEmpty(board));
        }

        [Test]
        public void ComboBlastCells_ForAClearBoard_IsEveryCell()
        {
            BoardModel board = Quiet8x8();

            List<Vector2Int> cells = board.ComboBlastCells(PowerCombo.ClearBoard, Cell(0, 7));

            Assert.AreEqual(64, cells.Count);
            Assert.AreEqual(64, new HashSet<Vector2Int>(cells).Count, "No cell is listed twice.");
        }

        [Test]
        public void BlastDelay_ForAClearBoard_IsTheRingTheCellIsIn()
        {
            const float beamSpeed = 10f;
            Vector2Int centre = Cell(3, 3);

            Assert.AreEqual(0.1f, Board.BlastDelay(PowerKind.Hypercube, centre, Cell(4, 4), beamSpeed, PowerCombo.ClearBoard), 0.0001f);
            Assert.AreEqual(0.3f, Board.BlastDelay(PowerKind.Hypercube, centre, Cell(0, 2), beamSpeed, PowerCombo.ClearBoard), 0.0001f);
            Assert.AreEqual(0.4f, Board.BlastDelay(PowerKind.Hypercube, centre, Cell(7, 7), beamSpeed, PowerCombo.ClearBoard), 0.0001f);
        }

        // ---------------------------------------------------------------- Only swaps combine

        [Test]
        public void Cascade_TwoPowerGemsSideBySide_DoNotCombine()
        {
            // Two Flames next to each other, one of them in a run of three R's: as if they had
            // fallen there. Clearing the run sets off the matched Flame, which catches the other.
            BoardModel board = grids.Build(
                "FGABCDE",
                "DEFGABC",
                "BCDEFGA",
                "GRRRXEF", // y = 3
                "EFGABCD",
                "CDEFGAB",
                "ABCDEFG");
            board.SetPieceAt(3, 3, new Piece(grids.Type('R'), PowerKind.Flame));
            board.SetPieceAt(4, 3, new Piece(grids.Type('X'), PowerKind.Flame));

            BoardModel.ClearResult result = board.ClearMatches(board.FindMatchGroups());

            Assert.AreEqual(2, result.Waves.Count, "The caught Flame goes off in a wave of its own.");
            foreach (BoardModel.Detonation detonation in result.Detonations)
            {
                Assert.AreEqual(PowerCombo.None, detonation.Combo);
                Assert.IsFalse(detonation.IsComboPartner);
            }
            // Each blast is a plain 3x3: nothing reaches two cells away from both Flames.
            Assert.IsFalse(board.PieceAt(3, 6).IsEmpty);
            Assert.IsFalse(board.PieceAt(3, 0).IsEmpty);
        }

        // ---------------------------------------------------------------- Timing

        [Test]
        public void BlastDelay_ForAStarCross_IsTheDistanceAlongTheNearestBeam()
        {
            const float beamSpeed = 10f;
            Vector2Int centre = Cell(3, 3);

            // Along the centre row and an outer row: the distance from the centre's column.
            Assert.AreEqual(0.3f, Board.BlastDelay(PowerKind.Star, centre, Cell(6, 3), beamSpeed, PowerCombo.StarCross), 0.0001f);
            Assert.AreEqual(0.3f, Board.BlastDelay(PowerKind.Star, centre, Cell(0, 4), beamSpeed, PowerCombo.StarCross), 0.0001f);
            // Up an outer column: the distance from the centre's row.
            Assert.AreEqual(0.3f, Board.BlastDelay(PowerKind.Flame, centre, Cell(2, 0), beamSpeed, PowerCombo.StarCross), 0.0001f);
            // In the 3x3 middle, on a row beam and a column beam: whichever gets there first.
            Assert.AreEqual(0f, Board.BlastDelay(PowerKind.Star, centre, Cell(3, 4), beamSpeed, PowerCombo.StarCross), 0.0001f);
            Assert.AreEqual(0.1f, Board.BlastDelay(PowerKind.Star, centre, Cell(4, 4), beamSpeed, PowerCombo.StarCross), 0.0001f);
        }

        [Test]
        public void BlastDelay_ForABigFlame_IsAlwaysZero()
        {
            Assert.AreEqual(0f, Board.BlastDelay(PowerKind.Flame, Cell(3, 3), Cell(5, 5), 10f, PowerCombo.BigFlame));
        }
    }
}
