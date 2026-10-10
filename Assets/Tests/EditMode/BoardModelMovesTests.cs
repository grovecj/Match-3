using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for "is there anything left to play?" (<see cref="BoardModel.HasValidMove"/>
    /// and <see cref="BoardModel.TryFindValidMove"/>) and for what happens when there isn't
    /// (<see cref="BoardModel.Shuffle"/> and <see cref="BoardModel.FillWithValidMove"/>).
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell.
    /// </summary>
    public class BoardModelMovesTests
    {
        private const int SeedCount = 1000;

        // A board with no valid moves and no matches. Each row is the one below it shifted two
        // places, so the same type never ends up close enough to itself for one swap to line
        // three up. (8x8, like the real game, using five of its six types.)
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

        /// <summary>
        /// Like <see cref="TestGrids.Build"/>, but with a chosen seed, for running the same
        /// board through many different shuffles.
        /// </summary>
        private BoardModel SeededGrid(int seed, string[] rows)
        {
            BoardModel drawn = grids.Build(rows);
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
                    board.SetTypeAt(x, y, drawn.TypeAt(x, y));
                }
            }
            return board;
        }

        /// <summary>How many of each letter are in <paramref name="rows"/>, as text like "A13 B13 C13".</summary>
        private static string LetterCounts(string[] rows)
        {
            // A SortedDictionary keeps its keys in order, so two boards with the same tokens
            // always give the same text, wherever those tokens are.
            var counts = new SortedDictionary<char, int>();
            foreach (string row in rows)
            {
                foreach (char letter in row)
                {
                    counts.TryGetValue(letter, out int count);
                    counts[letter] = count + 1;
                }
            }

            var parts = new List<string>();
            foreach (KeyValuePair<char, int> pair in counts)
            {
                parts.Add($"{pair.Key}{pair.Value}");
            }
            return string.Join(" ", parts);
        }

        // ---------------------------------------------------------------- Finding a move

        [Test]
        public void TryFindValidMove_SwapThatCompletesARow_IsFound()
        {
            // Swapping the B with the A on its right makes AAA along the bottom.
            BoardModel board = Grid("AABA");

            Assert.IsTrue(board.TryFindValidMove(out Vector2Int a, out Vector2Int b));

            Assert.AreEqual(new Vector2Int(2, 0), a);
            Assert.AreEqual(new Vector2Int(3, 0), b);
        }

        [Test]
        public void TryFindValidMove_SwapThatCompletesAColumn_IsFound()
        {
            // Swapping the B with the A above it makes AAA up the column.
            BoardModel board = Grid(
                "A",
                "B",
                "A",
                "A");

            Assert.IsTrue(board.TryFindValidMove(out Vector2Int a, out Vector2Int b));

            Assert.AreEqual(new Vector2Int(0, 2), a);
            Assert.AreEqual(new Vector2Int(0, 3), b);
        }

        [Test]
        public void TryFindValidMove_TheMoveItReturns_ReallyMakesAMatch()
        {
            // The same board the Play Mode tests start from.
            BoardModel board = Grid(
                "ABCDE",
                "CDEAB",
                "EABCD",
                "BCAEB",
                "AABCD");
            Assert.IsEmpty(board.FindMatches(), "The board should start with no matches.");

            Assert.IsTrue(board.TryFindValidMove(out Vector2Int a, out Vector2Int b));
            board.Swap(a, b);

            Assert.IsNotEmpty(board.FindMatches());
        }

        [Test]
        public void TryFindValidMove_LeavesTheBoardAsItWas()
        {
            // Every "what if" swap has to be undone, whether or not it made a match.
            BoardModel playable = Grid("ABCDE", "CDEAB", "EABCD", "BCAEB", "AABCD");
            string[] before = grids.Rows(playable);
            playable.TryFindValidMove(out _, out _);
            Assert.AreEqual(before, grids.Rows(playable));

            BoardModel dead = Grid(DeadBoard);
            dead.TryFindValidMove(out _, out _);
            Assert.AreEqual(DeadBoard, grids.Rows(dead));
        }

        [Test]
        public void HasValidMove_BoardWithNoMoves_IsFalse()
        {
            Assert.IsFalse(Grid(DeadBoard).HasValidMove());
        }

        [Test]
        public void HasValidMove_SmallestDeadBoard_IsFalse()
        {
            // Every row and column holds one of each type, and a single swap can change at most
            // one cell of any row or column, so none can become all the same.
            BoardModel board = Grid(
                "ABC",
                "CAB",
                "BCA");

            Assert.IsFalse(board.HasValidMove());
        }

        [Test]
        public void HasValidMove_OnlyMoveWouldNeedAnEmptyCell_IsFalse()
        {
            // Sliding the last A into the gap would make AAA, but an empty cell can't be swapped.
            Assert.IsFalse(Grid("AA.A").HasValidMove());
        }

        [TestCase(1, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        [TestCase(2, 2)]
        public void HasValidMove_BoardTooSmallForAMatch_IsFalse(int width, int height)
        {
            // Also checks the edges: neighbors that are off the board must be skipped, not looked up.
            var types = new List<TokenType> { grids.Type('A'), grids.Type('B'), grids.Type('C') };
            var board = new BoardModel(width, height, types, new System.Random(0));
            board.FillWithoutMatches();

            Assert.IsFalse(board.HasValidMove());
        }

        // ---------------------------------------------------------------- Shuffling
        //
        // Shuffling is random, so (as in BoardModelFillTests) these are property tests: shuffle
        // the same dead board from 1000 seeds and check what must be true of every result.

        [Test]
        public void Shuffle_ManySeeds_LeavesNoMatchesAndAValidMove()
        {
            for (int seed = 0; seed < SeedCount; seed++)
            {
                BoardModel board = SeededGrid(seed, DeadBoard);

                List<BoardModel.Move> moves = board.Shuffle();

                string drawn = string.Join("\n", grids.Rows(board));
                if (moves == null)
                {
                    Assert.Fail($"Seed {seed}: the shuffle gave up.");
                }
                if (board.FindMatches().Count > 0)
                {
                    Assert.Fail($"Seed {seed}: the shuffled board has a match:\n{drawn}");
                }
                if (!board.HasValidMove())
                {
                    Assert.Fail($"Seed {seed}: the shuffled board has no valid move:\n{drawn}");
                }
            }
        }

        [Test]
        public void Shuffle_ManySeeds_KeepsTheSameTokens()
        {
            string expected = LetterCounts(DeadBoard);
            for (int seed = 0; seed < SeedCount; seed++)
            {
                BoardModel board = SeededGrid(seed, DeadBoard);

                board.Shuffle();

                // A shuffle moves tokens around; it never adds, removes, or changes one.
                Assert.AreEqual(expected, LetterCounts(grids.Rows(board)), $"Seed {seed} changed which tokens are on the board.");
            }
        }

        [Test]
        public void Shuffle_Moves_SayWhereEveryTokenWent()
        {
            BoardModel before = Grid(DeadBoard);
            BoardModel board = SeededGrid(7, DeadBoard);

            List<BoardModel.Move> moves = board.Shuffle();

            // One move per token: every cell is left exactly once and arrived at exactly once...
            Assert.AreEqual(board.Width * board.Height, moves.Count);
            var from = new HashSet<Vector2Int>();
            var to = new HashSet<Vector2Int>();
            foreach (BoardModel.Move move in moves)
            {
                Assert.IsTrue(from.Add(move.From), $"Two tokens left {move.From}.");
                Assert.IsTrue(to.Add(move.To), $"Two tokens arrived at {move.To}.");
                // ...and the token that arrives is the type that left.
                Assert.AreSame(before.TypeAt(move.From), board.TypeAt(move.To), $"{move.From} -> {move.To} changed type.");
            }
        }

        [Test]
        public void Shuffle_SameSeed_SameResult()
        {
            BoardModel first = SeededGrid(42, DeadBoard);
            BoardModel second = SeededGrid(42, DeadBoard);

            first.Shuffle();
            second.Shuffle();

            Assert.AreEqual(grids.Rows(first), grids.Rows(second));
        }

        [Test]
        public void Shuffle_NoPlayableArrangementExists_ReturnsNullAndChangesNothing()
        {
            // A 2x2 board can never hold a run of 3, however its tokens are arranged.
            string[] rows = { "AB", "BA" };
            BoardModel board = Grid(rows);

            Assert.IsNull(board.Shuffle());

            Assert.AreEqual(rows, grids.Rows(board));
        }

        [Test]
        public void Shuffle_EmptyCells_StayEmpty()
        {
            string[] rows =
            {
                "ABC.",
                "CAB.",
                "BCA.",
            };
            BoardModel board = Grid(rows);

            Assert.IsNotNull(board.Shuffle());

            foreach (string row in grids.Rows(board))
            {
                Assert.AreEqual(TestGrids.Empty, row[3]);
                StringAssert.DoesNotContain(TestGrids.Empty.ToString(), row.Substring(0, 3));
            }
        }

        // ---------------------------------------------------------------- Starting fill

        [Test]
        public void FillWithValidMove_ManySeeds_HasAMoveAndNoMatches()
        {
            var types = new List<TokenType>();
            foreach (char letter in "ABCDE")
            {
                types.Add(grids.Type(letter));
            }

            for (int seed = 0; seed < SeedCount; seed++)
            {
                var board = new BoardModel(8, 8, types, new System.Random(seed));

                Assert.IsTrue(board.FillWithValidMove(), $"Seed {seed} gave up.");
                Assert.IsEmpty(board.FindMatches(), $"Seed {seed} started with a match.");
                Assert.IsTrue(board.HasValidMove(), $"Seed {seed} started with no valid move.");
            }
        }

        [Test]
        public void FillWithValidMove_BoardTooSmallForAMove_ReturnsFalseButStillFills()
        {
            var types = new List<TokenType> { grids.Type('A'), grids.Type('B'), grids.Type('C') };
            var board = new BoardModel(2, 2, types, new System.Random(0));

            Assert.IsFalse(board.FillWithValidMove());

            foreach (string row in grids.Rows(board))
            {
                StringAssert.DoesNotContain(TestGrids.Empty.ToString(), row);
            }
        }
    }
}
