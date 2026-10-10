using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for chain reactions in <see cref="BoardModel"/>: a power gem caught in
    /// another power gem's blast goes off too, and so does anything it catches, wave after wave
    /// (<see cref="BoardModel.ClearResult.Waves"/>).
    ///
    /// Two of the simplest chains live with the gems they're about: a Flame setting off a Flame
    /// is in BoardModelFlameTests, and a Hypercube swap setting off a Flame of its colour is in
    /// BoardModelHypercubeTests.
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell (or, in a powers drawing, "no power here").
    /// </summary>
    public class BoardModelChainTests
    {
        // The board most of these tests start from. The three R's in the middle row are its only
        // match; every other row is the one below it shifted two places, so nothing else lines
        // up. What changes from test to test is which pieces are power gems.
        private static readonly string[] MiddleRunLayout =
        {
            "DEABC", // y = 4
            "BCDEA",
            "ERRRD",
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

        /// <summary>
        /// Checks the two promises every chain makes, whatever the board: no power gem went off
        /// twice, and no cell was cleared twice.
        /// </summary>
        private static void AssertNothingHappenedTwice(BoardModel.ClearResult result)
        {
            // HashSet.Add returns false for something already in the set, which is exactly the
            // "have I seen this before?" question.
            var detonated = new HashSet<Vector2Int>();
            var cleared = new HashSet<Vector2Int>(result.Matched);
            foreach (BoardModel.Detonation detonation in result.Detonations)
            {
                Assert.IsTrue(detonated.Add(detonation.Cell),
                    $"The gem in ({detonation.Cell.x},{detonation.Cell.y}) went off twice.");
                foreach (Vector2Int cell in detonation.Blasted)
                {
                    Assert.IsTrue(cleared.Add(cell), $"Cell ({cell.x},{cell.y}) was cleared twice.");
                }
            }
        }

        // ---------------------------------------------------------------- One gem sets off another

        [Test]
        public void StarSetsOffAFlameAtTheFarEndOfItsRow()
        {
            BoardModel board = Grid(MiddleRunLayout);
            // The matched R at (1,2) is a Star. The D at (4,2), the far end of its row, is a
            // Flame: nowhere near the match, but in the path of the beam.
            grids.SetPowers(board,
                ".....",
                ".....",
                ".S..F",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            // Row 2 and column 1 went to the Star. The hole on the right is the Flame's 3x3,
            // clipped by the board's edge.
            Assert.AreEqual(new[]
            {
                "D.ABC",
                "B.D..",
                ".....",
                "C.E..",
                "A.CDE",
            }, grids.Rows(board));

            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual(PowerKind.Star, result.Waves[0][0].Kind);
            Assert.AreEqual("(0,2) (4,2) (1,0) (1,1) (1,3) (1,4)", Describe(result.Waves[0][0].Blasted));
            Assert.AreEqual(PowerKind.Flame, result.Waves[1][0].Kind);
            Assert.AreEqual(Cell(4, 2), result.Waves[1][0].Cell);
            Assert.AreEqual("(3,1) (3,3) (4,1) (4,3)", Describe(result.Waves[1][0].Blasted));
            AssertNothingHappenedTwice(result);
        }

        [Test]
        public void ThreeGemChain_ReportsThreeWavesInOrder()
        {
            BoardModel board = Grid(MiddleRunLayout);
            // A staircase down to the corner: the matched Flame at (2,2) reaches the Flame at
            // (1,1), whose blast reaches the Star at (0,0). Neither of those is in the first
            // blast's reach... except (1,1), and (0,0) is two steps away from the match.
            grids.SetPowers(board,
                ".....",
                ".....",
                "..F..",
                ".F...",
                "S....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                ".EABC",
                "....A",
                "....D",
                "....B",
                ".....",
            }, grids.Rows(board));

            // One gem per wave, each set off by the one before.
            Assert.AreEqual(3, result.Waves.Count);
            Assert.AreEqual(1, result.Waves[0].Count);
            Assert.AreEqual(1, result.Waves[1].Count);
            Assert.AreEqual(1, result.Waves[2].Count);

            Assert.AreEqual(Cell(2, 2), result.Waves[0][0].Cell);
            Assert.AreEqual("(1,1) (1,3) (2,1) (2,3) (3,1) (3,3)", Describe(result.Waves[0][0].Blasted));
            Assert.AreEqual(Cell(1, 1), result.Waves[1][0].Cell);
            Assert.AreEqual("(0,0) (0,1) (0,2) (1,0) (2,0)", Describe(result.Waves[1][0].Blasted));
            Assert.AreEqual(Cell(0, 0), result.Waves[2][0].Cell);
            Assert.AreEqual(PowerKind.Star, result.Waves[2][0].Kind);
            // By the time the Star goes off, most of its row and column is already gone.
            Assert.AreEqual("(3,0) (4,0) (0,3) (0,4)", Describe(result.Waves[2][0].Blasted));

            // The flat list is the same three, in the same order.
            Assert.AreEqual(3, result.Detonations.Count);
            Assert.AreEqual(Cell(0, 0), result.Detonations[2].Cell);
            AssertNothingHappenedTwice(result);
        }

        [Test]
        public void GemInTwoBlasts_DetonatesOnce()
        {
            BoardModel board = Grid(MiddleRunLayout);
            // Both ends of the run are Flames, and the Flame at (2,1) sits in both their blasts.
            grids.SetPowers(board,
                ".....",
                ".....",
                ".F.F.",
                "..F..",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[]
            {
                "DEABC",
                ".....",
                ".....",
                ".....",
                "A...E",
            }, grids.Rows(board));

            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual(2, result.Waves[0].Count, "Both matched Flames are in the first wave.");
            // The first blast to reach (2,1) is the one that lists it; the second finds it gone.
            Assert.AreEqual("(0,1) (0,2) (0,3) (1,1) (1,3) (2,1) (2,3)", Describe(result.Waves[0][0].Blasted));
            Assert.AreEqual("(3,1) (3,3) (4,1) (4,2) (4,3)", Describe(result.Waves[0][1].Blasted));

            Assert.AreEqual(1, result.Waves[1].Count, "Caught by two blasts, but it's one gem: it goes off once.");
            Assert.AreEqual(Cell(2, 1), result.Waves[1][0].Cell);
            Assert.AreEqual("(1,0) (2,0) (3,0)", Describe(result.Waves[1][0].Blasted));
            AssertNothingHappenedTwice(result);
        }

        [Test]
        public void NoPowerGemInTheBlast_IsASingleWave()
        {
            BoardModel board = Grid(MiddleRunLayout);
            grids.SetPowers(board,
                ".....",
                ".....",
                "..F..",
                ".....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(1, result.Waves.Count, "With nothing to set off, the chain stops at the first wave.");
        }

        [Test]
        public void NoPowerGemInTheMatch_HasNoWavesAtAll()
        {
            BoardModel board = Grid(MiddleRunLayout);
            // A Flame right next to the match, but not in it, and nothing blasts it.
            grids.SetPowers(board,
                ".....",
                ".....",
                ".....",
                ".F...",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.IsEmpty(result.Waves);
            Assert.IsEmpty(result.Detonations);
            Assert.AreEqual(PowerKind.Flame, board.PieceAt(1, 1).Power, "An ordinary match shouldn't set off its neighbours.");
        }

        // ---------------------------------------------------------------- What a chain leaves alone

        [Test]
        public void GemMadeByTheMatch_SurvivesAChainThatReachesIt()
        {
            BoardModel board = Grid(
                "ABCDE",
                "RRRRD",
                "CDEAB");
            // The R at (0,1) is a Flame, in a line of four that leaves a new Flame at (1,1).
            // The B at (1,2) is a Flame too: the first blast catches it, and its own blast then
            // covers (1,1).
            grids.SetPowers(board,
                ".F...",
                "F....",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            Assert.AreEqual(new[] { "...DE", ".R..D", "..EAB" }, grids.Rows(board));
            Assert.AreEqual(new[] { ".....", ".F...", "....." }, grids.PowerRows(board));

            // Two waves went off around it, and the new gem is in neither: it wasn't on the
            // board when the match happened.
            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual(Cell(1, 1), result.Created[0].Cell);
            Assert.AreEqual("(2,2)", Describe(result.Waves[1][0].Blasted));
            AssertNothingHappenedTwice(result);
        }

        [Test]
        public void Chain_ResolvesFully_BeforeAnythingFalls()
        {
            BoardModel board = Grid(MiddleRunLayout);
            grids.SetPowers(board,
                ".....",
                ".....",
                "..F..",
                ".F...",
                "S....");

            ClearAll(board);

            // Three waves have been and gone, and the pieces above the holes are still exactly
            // where they started: the whole top row, for one.
            Assert.AreEqual(".EABC", grids.Rows(board)[0]);
            Assert.AreEqual(grids.Type('A'), board.TypeAt(4, 3), "The A above the cleared cells shouldn't have dropped yet.");

            // Gravity is a separate step, and finds nothing left to set off.
            List<BoardModel.Fall> falls = board.Collapse();
            Assert.That(falls, Is.Not.Empty);
            Assert.AreEqual(new[]
            {
                ".....",
                "....C",
                "....A",
                "....D",
                ".EABB",
            }, grids.Rows(board));
        }

        // ---------------------------------------------------------------- It always ends

        [Test]
        public void BoardFullOfPowerGems_EveryGemGoesOffExactlyOnce_AndTheChainEnds()
        {
            // The worst case for a chain: every single piece is a power gem, so every blast
            // catches more gems, which catch more gems... If a gem could go off twice, two gems
            // in each other's blast would set each other off forever.
            BoardModel board = Grid(
                "*EABC",
                "BCDEA",
                "ERRRD",
                "CDEAB",
                "ABCD*");
            grids.SetPowers(board,
                ".SFSF",
                "SFSFS",
                "FSFSF",
                "SFSFS",
                "FSFS.");
            int gems = board.Width * board.Height;

            BoardModel.ClearResult result = ClearAll(board);

            // 25 gems, 25 detonations: each one once.
            Assert.AreEqual(gems, result.Detonations.Count);
            AssertNothingHappenedTwice(result);
            Assert.AreEqual(3, result.Waves.Count);
            Assert.AreEqual(3, result.Waves[0].Count, "The first wave is the three matched gems.");
            Assert.AreEqual(new[] { ".....", ".....", ".....", ".....", "....." }, grids.Rows(board));
        }

        // ---------------------------------------------------------------- Hypercubes in a chain

        [Test]
        public void HypercubeInABlast_PicksTheColourThatIsMostCommonWhenItGoesOff()
        {
            BoardModel board = Grid(
                "GABCD",
                "CB*BA",
                "DRRRG",
                "GBABG");
            // Before anything happens B is the most common colour: five of them to four G's.
            // But four of the B's are in the 3x3 around the Flame, along with the cube.
            Assert.AreEqual(grids.Type('B'), board.MostCommonColour());
            grids.SetPowers(board,
                ".....",
                ".....",
                "..F..",
                ".....");

            BoardModel.ClearResult result = ClearAll(board);

            // So by the time the cube goes off, a wave later, one B is left against four G's,
            // and G is what it clears.
            Assert.AreEqual(new[] { ".ABCD", "C...A", "D....", "....." }, grids.Rows(board));
            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual("(1,0) (1,2) (2,0) (2,2) (3,0) (3,2)", Describe(result.Waves[0][0].Blasted));

            BoardModel.Detonation cube = result.Waves[1][0];
            Assert.AreEqual(PowerKind.Hypercube, cube.Kind);
            Assert.AreEqual(Cell(2, 2), cube.Cell);
            Assert.AreEqual(grids.Type('G'), cube.Target);
            Assert.AreEqual("(0,0) (0,3) (4,0) (4,1)", Describe(cube.Blasted));
            AssertNothingHappenedTwice(result);
        }

        [Test]
        public void HypercubeInABlast_WithNoColouredGemLeft_JustGoes()
        {
            BoardModel board = Grid(
                ".*.",
                "RRR");
            grids.SetPowers(board,
                "...",
                ".F.");

            BoardModel.ClearResult result = ClearAll(board);

            // Once the R's are gone there's no colour left to pick. The cube still goes off (it
            // was caught), it just has nothing to clear.
            Assert.AreEqual(new[] { "...", "..." }, grids.Rows(board));
            Assert.AreEqual(2, result.Waves.Count);
            Assert.AreEqual(PowerKind.Hypercube, result.Waves[1][0].Kind);
            Assert.IsNull(result.Waves[1][0].Target);
            Assert.IsEmpty(result.Waves[1][0].Blasted);
        }

        [Test]
        public void HypercubeSwap_SetsOffAStarOfItsColour_WhichSetsOffAFlame()
        {
            BoardModel board = Grid(
                "GBAB",
                "BRGA",
                "*GBG");
            // The only R is a Star. The A at the far end of its row is a Flame.
            grids.SetPowers(board,
                "....",
                ".S.F",
                "....");

            BoardModel.ClearResult result = board.ActivateHypercube(Cell(0, 0), grids.Type('R'));

            Assert.AreEqual(new[] { "G...", "....", "...." }, grids.Rows(board));

            // The cube, then the Star it cleared, then the Flame the Star's beam reached.
            Assert.AreEqual(3, result.Waves.Count);
            Assert.AreEqual(PowerKind.Hypercube, result.Waves[0][0].Kind);
            Assert.AreEqual("(1,1)", Describe(result.Waves[0][0].Blasted));
            Assert.AreEqual(PowerKind.Star, result.Waves[1][0].Kind);
            Assert.AreEqual("(0,1) (2,1) (3,1) (1,0) (1,2)", Describe(result.Waves[1][0].Blasted));
            Assert.AreEqual(PowerKind.Flame, result.Waves[2][0].Kind);
            Assert.AreEqual("(2,0) (2,2) (3,0) (3,2)", Describe(result.Waves[2][0].Blasted));
            AssertNothingHappenedTwice(result);
        }

        // ---------------------------------------------------------------- Counting colours

        [Test]
        public void MostCommonColour_IsTheOneWithTheMostPieces_PowerGemsIncluded()
        {
            BoardModel board = Grid(
                "GRG",
                "R*R");
            grids.SetPowers(board,
                "...",
                "F..");

            // Three R's (one of them a Flame) to two G's. The cube has no colour to count.
            Assert.AreEqual(grids.Type('R'), board.MostCommonColour());
        }

        [Test]
        public void MostCommonColour_Tie_GoesToTheTypeListedFirst()
        {
            TokenType a = grids.Type('A');
            TokenType b = grids.Type('B');

            // Two of each, on two boards that differ only in the order their types are listed.
            // (Built by hand, because the order of the type list is the whole point here.)
            foreach (TokenType[] types in new[] { new[] { a, b }, new[] { b, a } })
            {
                var board = new BoardModel(4, 1, types, new System.Random(0));
                board.SetTypeAt(0, 0, a);
                board.SetTypeAt(1, 0, b);
                board.SetTypeAt(2, 0, b);
                board.SetTypeAt(3, 0, a);

                Assert.AreEqual(types[0], board.MostCommonColour(), "The first type in the list should win a tie.");
            }
        }

        [Test]
        public void MostCommonColour_NoColouredPieces_IsNull()
        {
            Assert.IsNull(Grid("*.").MostCommonColour());
        }

        [Test]
        public void CellsOfColour_ListsEveryPieceOfTheColour_ColumnByColumn()
        {
            BoardModel board = Grid(
                "RGR",
                "GRG");

            Assert.AreEqual("(0,1) (1,0) (2,1)", Describe(board.CellsOfColour(grids.Type('R'))));
            Assert.IsEmpty(board.CellsOfColour(null), "No colour means nothing to clear, not \"every empty cell\".");
        }

        // ---------------------------------------------------------------- Last Hurrah
        //
        // When time runs out, the Board sets off every power gem still on the board by asking
        // the model two things in a loop: "is there one, and where?" (TryFindPowerGem) and "set
        // that one off" (DetonatePowerGem). No match and no swap is involved.

        // A board with no matches at all: each row is the one below it shifted two places.
        private static readonly string[] NoMatchLayout =
        {
            "DEABC", // y = 4
            "BCDEA",
            "EABCD",
            "CDEAB",
            "ABCDE", // y = 0
        };

        [Test]
        public void TryFindPowerGem_NoPowerGems_IsFalse()
        {
            Assert.IsFalse(Grid(NoMatchLayout).TryFindPowerGem(out _));
        }

        [Test]
        public void LastHurrah_SetsGemsOffRowByRowFromTheBottomLeft()
        {
            BoardModel board = Grid(NoMatchLayout);
            // Three gems out of each other's reach. The Star is further left than the bottom
            // Flame, but one row up, so the Flame goes first: rows count before columns.
            grids.SetPowers(board,
                "....F",
                ".....",
                ".....",
                "S....",
                "...F.");

            // The Board's loop, without the animations.
            var order = new List<Vector2Int>();
            while (board.TryFindPowerGem(out Vector2Int cell))
            {
                order.Add(cell);
                BoardModel.ClearResult result = board.DetonatePowerGem(cell);
                Assert.AreEqual(1, result.Waves.Count, "None of these gems can reach another.");
            }

            Assert.AreEqual("(3,0) (0,1) (4,4)", Describe(order));
            Assert.AreEqual(new[] { ".....", ".....", ".....", ".....", "....." }, grids.PowerRows(board));
        }

        [Test]
        public void DetonatePowerGem_IsAStepOfItsOwn_WithNothingMatchedOrMade()
        {
            BoardModel board = Grid(NoMatchLayout);
            grids.SetPowers(board,
                ".....",
                ".....",
                ".....",
                ".F...",
                ".....");

            BoardModel.ClearResult result = board.DetonatePowerGem(Cell(1, 1));

            // The Flame's 3x3, itself included.
            Assert.AreEqual(new[]
            {
                "DEABC",
                "BCDEA",
                "...CD",
                "...AB",
                "...DE",
            }, grids.Rows(board));
            Assert.IsEmpty(result.Matched);
            Assert.IsEmpty(result.Created);
            Assert.AreEqual(1, result.Detonations.Count);
            Assert.AreEqual(Cell(1, 1), result.Detonations[0].Cell);
            Assert.AreEqual(8, result.Detonations[0].Blasted.Count);

            // Scored like a swapped Hypercube's step: the blasted gems, and nothing for runs.
            ClearStep step = ClearStep.From(result, new List<MatchGroup>(), cascadeIndex: 0);
            Assert.AreEqual(8, step.BlastedGems);
            Assert.AreEqual(9, step.GemsCleared, "The eight blasted gems and the Flame itself.");
            Assert.AreEqual(8 * ScoreRules.PointsPerBlastedGem, step.Points);
        }

        [Test]
        public void DetonatePowerGem_CatchesOtherGemsInItsChain_SoTheyAreNotFoundAgain()
        {
            BoardModel board = Grid(NoMatchLayout);
            grids.SetPowers(board,
                ".....",
                ".....",
                "..F..",
                ".F...",
                ".....");

            Assert.IsTrue(board.TryFindPowerGem(out Vector2Int first));
            Assert.AreEqual(Cell(1, 1), first);
            BoardModel.ClearResult result = board.DetonatePowerGem(first);

            Assert.AreEqual(2, result.Waves.Count, "The Flame at (2,2) is in the first one's blast.");
            Assert.AreEqual(Cell(2, 2), result.Waves[1][0].Cell);
            Assert.IsFalse(board.TryFindPowerGem(out _), "Both gems have gone off.");
            AssertNothingHappenedTwice(result);
        }

        [Test]
        public void DetonatePowerGem_Hypercube_GoesOffOnTheMostCommonColour()
        {
            // Four A's, three B's and one C: nobody swapped the cube, so it picks A itself.
            BoardModel board = Grid(
                "ABA",
                "BAB",
                "A*C");

            BoardModel.ClearResult result = board.DetonatePowerGem(Cell(1, 0));

            Assert.AreEqual(grids.Type('A'), result.Detonations[0].Target);
            Assert.AreEqual(new[]
            {
                ".B.",
                "B.B",
                "..C",
            }, grids.Rows(board));
        }

        [Test]
        public void DetonatePowerGem_OnAnOrdinaryPiece_Throws()
        {
            BoardModel board = Grid(NoMatchLayout);

            Assert.Throws<System.ArgumentException>(() => board.DetonatePowerGem(Cell(0, 0)));
        }
    }
}
