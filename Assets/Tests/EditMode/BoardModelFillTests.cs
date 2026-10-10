using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="BoardModel.FillWithoutMatches"/> (the starting board) and
    /// for determinism: the same seed must always produce the same board.
    ///
    /// The starting fill is random, so no single example can prove it never makes a match. The
    /// tests here are "property tests" instead: fill the board from 1000 different seeds and
    /// check that the property (zero matches) holds for every one. Each seed is fixed, so a
    /// failure is repeatable, and the message says which seed to rerun.
    /// </summary>
    public class BoardModelFillTests
    {
        private const int SeedCount = 1000;

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

        /// <summary>Token types for letters A, B, C... (<paramref name="count"/> of them).</summary>
        private List<TokenType> Types(int count)
        {
            var types = new List<TokenType>();
            for (int i = 0; i < count; i++)
            {
                types.Add(grids.Type((char)('A' + i)));
            }
            return types;
        }

        private BoardModel FilledBoard(int width, int height, int typeCount, int seed)
        {
            var board = new BoardModel(width, height, Types(typeCount), new System.Random(seed));
            board.FillWithoutMatches();
            return board;
        }

        /// <summary>
        /// Fills a board from each of <paramref name="seedCount"/> seeds and fails, naming the
        /// seed and drawing the board, on the first one that has a match or an empty cell.
        /// </summary>
        private void AssertFillsNeverMatch(int width, int height, int typeCount, int seedCount)
        {
            for (int seed = 0; seed < seedCount; seed++)
            {
                BoardModel board = FilledBoard(width, height, typeCount, seed);

                // Checking with `if` and calling Assert.Fail only on failure (rather than an
                // Assert every time round) means we only build the message when it's needed.
                List<List<Vector2Int>> matches = board.FindMatches();
                string[] rows = grids.Rows(board);
                if (matches.Count > 0)
                {
                    Assert.Fail($"Seed {seed} ({width}x{height}, {typeCount} types) started with {matches.Count} match(es):\n"
                        + string.Join("\n", rows));
                }
                foreach (string row in rows)
                {
                    if (row.Contains(TestGrids.Empty.ToString()))
                    {
                        Assert.Fail($"Seed {seed} ({width}x{height}, {typeCount} types) left a cell empty:\n"
                            + string.Join("\n", rows));
                    }
                }
            }
        }

        // ---------------------------------------------------------------- No starting matches

        [Test]
        public void FillWithoutMatches_ManySeeds_8x8WithSixTypes_HasNoMatches()
        {
            AssertFillsNeverMatch(8, 8, 6, SeedCount);
        }

        [Test]
        public void FillWithoutMatches_ManySeeds_8x8WithThreeTypes_HasNoMatches()
        {
            // 3 is the fewest types that can work: each cell can have at most 2 forbidden types
            // (one from the row, one from the column), so there's always one left to pick.
            // It's also the hardest case, since so few choices means near-misses are everywhere.
            AssertFillsNeverMatch(8, 8, 3, SeedCount);
        }

        // [TestCase] runs the same test once per set of arguments; each shows up as its own
        // entry in the Test Runner, e.g. "FillWithoutMatches_NonSquareBoards_HaveNoMatches(3,12)".
        [TestCase(3, 12)]
        [TestCase(12, 3)]
        [TestCase(1, 10)]
        [TestCase(10, 1)]
        [TestCase(1, 1)]
        public void FillWithoutMatches_NonSquareBoards_HaveNoMatches(int width, int height)
        {
            AssertFillsNeverMatch(width, height, 3, SeedCount);
        }

        [Test]
        public void FillWithoutMatches_OnlyUsesTheGivenTypes()
        {
            BoardModel board = FilledBoard(8, 8, 4, seed: 7);

            // Every letter drawn must be one of A-D (a type the builder doesn't know would be '?').
            foreach (string row in grids.Rows(board))
            {
                StringAssert.IsMatch("^[ABCD]+$", row);
            }
        }

        // ---------------------------------------------------------------- Equal frequency

        [Test]
        public void FillWithoutMatches_ManySeeds_SixTypes_AppearEquallyOften()
        {
            var counts = new Dictionary<char, int>();
            for (int seed = 0; seed < SeedCount; seed++)
            {
                CountLetters(FilledBoard(8, 8, 6, seed), counts);
            }

            AssertRoughlyEqualShares(counts, typeCount: 6);
        }

        [Test]
        public void Refill_ManySeeds_SixTypes_AppearEquallyOften()
        {
            // A new BoardModel is empty, so Refill has all 64 cells to fill: the same random
            // pick the game makes for every token that falls in from the top.
            var counts = new Dictionary<char, int>();
            for (int seed = 0; seed < SeedCount; seed++)
            {
                var board = new BoardModel(8, 8, Types(6), new System.Random(seed));
                board.Refill();
                CountLetters(board, counts);
            }

            AssertRoughlyEqualShares(counts, typeCount: 6);
        }

        /// <summary>Adds one to <paramref name="counts"/> for every token on <paramref name="board"/>.</summary>
        private void CountLetters(BoardModel board, Dictionary<char, int> counts)
        {
            foreach (string row in grids.Rows(board))
            {
                foreach (char letter in row)
                {
                    counts.TryGetValue(letter, out int soFar);
                    counts[letter] = soFar + 1;
                }
            }
        }

        /// <summary>
        /// Fails unless <paramref name="counts"/> holds exactly <paramref name="typeCount"/>
        /// types, each within 5% of an even share of the total.
        /// </summary>
        private static void AssertRoughlyEqualShares(Dictionary<char, int> counts, int typeCount)
        {
            Assert.AreEqual(typeCount, counts.Count, "Every type should have appeared, and nothing else.");

            int total = 0;
            foreach (int count in counts.Values)
            {
                total += count;
            }

            // Random picks never come out exactly even. With 64,000 tokens an even share is
            // about 10,667 and chance alone moves it by about 100 either way, so a 5% margin
            // (about 530) only fails if one type really is favoured.
            double evenShare = (double)total / typeCount;
            foreach (KeyValuePair<char, int> pair in counts)
            {
                Assert.That(pair.Value, Is.EqualTo(evenShare).Within(5).Percent,
                    $"Type {pair.Key} appeared {pair.Value} times out of {total}.");
            }
        }

        [Test]
        public void FillWithoutMatches_FewerThanThreeDistinctTypes_Throws()
        {
            // Three entries, but only two distinct types: listing a type twice makes it more
            // likely, it doesn't add a new one.
            var types = new List<TokenType> { grids.Type('A'), grids.Type('B'), grids.Type('A') };
            var board = new BoardModel(4, 4, types, new System.Random(0));

            Assert.Throws<System.InvalidOperationException>(() => board.FillWithoutMatches());
        }

        // ---------------------------------------------------------------- Determinism

        [Test]
        public void FillWithoutMatches_SameSeed_SameBoard()
        {
            BoardModel first = FilledBoard(8, 8, 6, seed: 123);
            BoardModel second = FilledBoard(8, 8, 6, seed: 123);

            Assert.AreEqual(grids.Rows(first), grids.Rows(second));
        }

        [Test]
        public void FillWithoutMatches_DifferentSeeds_DifferentBoards()
        {
            // A sanity check that the seed is actually used. (Two seeds *could* in theory give the
            // same 8x8 board, but for these two fixed seeds they don't, and they always won't.)
            BoardModel first = FilledBoard(8, 8, 6, seed: 1);
            BoardModel second = FilledBoard(8, 8, 6, seed: 2);

            Assert.AreNotEqual(grids.Rows(first), grids.Rows(second));
        }

        [Test]
        public void ClearCollapseRefill_SameSeed_SameResult()
        {
            // The whole sequence (fill, clear, fall, refill) is deterministic, not just the fill.
            string[] first = FillClearAndRefill(seed: 99);
            string[] second = FillClearAndRefill(seed: 99);

            Assert.AreEqual(first, second);
        }

        private string[] FillClearAndRefill(int seed)
        {
            BoardModel board = FilledBoard(8, 8, 6, seed);

            // Clear the bottom row and the middle of column 3, as if two matches were made.
            for (int x = 0; x < board.Width; x++)
            {
                board.SetTypeAt(x, 0, null);
            }
            for (int y = 3; y < 6; y++)
            {
                board.SetTypeAt(3, y, null);
            }

            board.Collapse();
            board.Refill();
            return grids.Rows(board);
        }
    }
}
