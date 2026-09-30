using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for what happens after a match is cleared: <see cref="BoardModel.Collapse"/>
    /// (gravity) and <see cref="BoardModel.Refill"/>, plus the Fall/Spawn lists they hand to
    /// <see cref="Board"/> for animation.
    ///
    /// Two styles of test live here:
    /// - Example tests draw one small board, run a rule, and compare against the board we expect.
    ///   They're easy to read and pin down specific cases (a gap at the top, a gap at the bottom...).
    /// - Property tests ("for many inputs, X always holds") generate hundreds of random boards
    ///   from fixed seeds and check a rule that must be true for all of them. They catch cases
    ///   nobody thought to draw. Because the seeds are fixed, they're still repeatable: a
    ///   failure prints its seed, and rerunning gives the exact same board.
    ///
    /// Reminder when reading the grids: the first string is the TOP row, and (0, 0) is the
    /// bottom-left cell. '.' is an empty cell.
    /// </summary>
    public class BoardModelGravityTests
    {
        // How many random boards each property test tries. Enough to hit plenty of odd shapes,
        // small enough that the whole file still runs in a few milliseconds.
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

        /// <summary>Asserts <paramref name="board"/> looks exactly like <paramref name="expectedRows"/>.</summary>
        private void AssertBoard(BoardModel board, params string[] expectedRows)
        {
            // Comparing string arrays makes NUnit print both boards row by row on failure.
            Assert.AreEqual(expectedRows, grids.Rows(board));
        }

        private static string Describe(BoardModel.Fall fall)
        {
            return $"({fall.From.x},{fall.From.y})->({fall.To.x},{fall.To.y})";
        }

        private static List<string> Describe(List<BoardModel.Fall> falls)
        {
            return falls.ConvertAll(Describe);
        }

        /// <summary>
        /// An 8x8 board filled from <paramref name="seed"/>, with roughly a third of its cells
        /// emptied at random, like the aftermath of a big clear. Uses letters A-F.
        /// </summary>
        private BoardModel RandomBoardWithGaps(int seed)
        {
            var types = new List<TokenType>();
            foreach (char letter in "ABCDEF")
            {
                types.Add(grids.Type(letter));
            }

            var random = new System.Random(seed);
            var board = new BoardModel(8, 8, types, random);
            board.FillWithoutMatches();
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    if (random.Next(3) == 0)
                    {
                        board.SetTypeAt(x, y, null);
                    }
                }
            }
            return board;
        }

        private static int CountEmpty(BoardModel board, int x)
        {
            int empty = 0;
            for (int y = 0; y < board.Height; y++)
            {
                if (board.TypeAt(x, y) == null)
                {
                    empty++;
                }
            }
            return empty;
        }

        // ---------------------------------------------------------------- Gravity: examples

        [Test]
        public void Collapse_OneGap_TokensAboveDropByOne()
        {
            BoardModel board = Grid(
                "G",
                "B",
                ".",
                "R");

            List<BoardModel.Fall> falls = board.Collapse();

            AssertBoard(board,
                ".",
                "G",
                "B",
                "R");
            // Moved bottom to top, so each token lands in a cell that's already empty.
            CollectionAssert.AreEqual(new[] { "(0,2)->(0,1)", "(0,3)->(0,2)" }, Describe(falls));
        }

        [Test]
        public void Collapse_SeveralGaps_TokensKeepTheirOrder()
        {
            // Every token is a different letter, so if any two swapped places on the way down
            // we'd see it in the result.
            BoardModel board = Grid(
                "A",
                ".",
                "B",
                "C",
                ".",
                "D",
                ".",
                "E");

            List<BoardModel.Fall> falls = board.Collapse();

            AssertBoard(board,
                ".",
                ".",
                ".",
                "A",
                "B",
                "C",
                "D",
                "E");
            CollectionAssert.AreEqual(
                new[] { "(0,2)->(0,1)", "(0,4)->(0,2)", "(0,5)->(0,3)", "(0,7)->(0,4)" },
                Describe(falls));
        }

        [Test]
        public void Collapse_GapsAlreadyAtTop_NothingMoves()
        {
            BoardModel board = Grid(
                ".",
                ".",
                "G",
                "R");

            List<BoardModel.Fall> falls = board.Collapse();

            AssertBoard(board,
                ".",
                ".",
                "G",
                "R");
            Assert.IsEmpty(falls);
        }

        [Test]
        public void Collapse_GapsAtBottom_WholeColumnDropsByTheGap()
        {
            BoardModel board = Grid(
                "G",
                "B",
                ".",
                ".");

            List<BoardModel.Fall> falls = board.Collapse();

            AssertBoard(board,
                ".",
                ".",
                "G",
                "B");
            CollectionAssert.AreEqual(new[] { "(0,2)->(0,0)", "(0,3)->(0,1)" }, Describe(falls));
        }

        [Test]
        public void Collapse_FullyClearedColumn_StaysEmptyAndNeighborsDontMove()
        {
            BoardModel board = Grid(
                "R.G",
                "B.R",
                "G.B");

            List<BoardModel.Fall> falls = board.Collapse();

            AssertBoard(board,
                "R.G",
                "B.R",
                "G.B");
            Assert.IsEmpty(falls);
        }

        [Test]
        public void Collapse_EachColumnFallsIndependently()
        {
            // Tokens only ever move straight down: a gap in one column never pulls a token
            // sideways from the next.
            BoardModel board = Grid(
                "RBG",
                ".B.",
                "G..");

            List<BoardModel.Fall> falls = board.Collapse();

            AssertBoard(board,
                "...",
                "RB.",
                "GBG");
            CollectionAssert.AreEqual(
                new[] { "(0,2)->(0,1)", "(1,1)->(1,0)", "(1,2)->(1,1)", "(2,2)->(2,0)" },
                Describe(falls));
        }

        // ---------------------------------------------------------------- Gravity: properties

        [Test]
        public void Collapse_ManyRandomBoards_GapsEndUpAtTheTopOfEveryColumn()
        {
            for (int seed = 0; seed < RandomBoardCount; seed++)
            {
                BoardModel board = RandomBoardWithGaps(seed);
                board.Collapse();

                for (int x = 0; x < board.Width; x++)
                {
                    // Once we see an empty cell going up, everything above it must be empty too.
                    bool seenEmpty = false;
                    for (int y = 0; y < board.Height; y++)
                    {
                        bool empty = board.TypeAt(x, y) == null;
                        if (seenEmpty && !empty)
                        {
                            Assert.Fail($"Seed {seed}: token at ({x},{y}) is floating above a gap.\n"
                                + string.Join("\n", grids.Rows(board)));
                        }
                        seenEmpty |= empty;
                    }
                }
            }
        }

        [Test]
        public void Collapse_ManyRandomBoards_FallsDescribeExactlyWhatMoved()
        {
            // Board animates tokens using only the Fall list, so the list must be a faithful
            // record: replaying it on a copy of the old board has to produce the new board.
            for (int seed = 0; seed < RandomBoardCount; seed++)
            {
                BoardModel board = RandomBoardWithGaps(seed);
                string[] before = grids.Rows(board);

                List<BoardModel.Fall> falls = board.Collapse();

                string[] replayed = Replay(before, falls, seed);
                Assert.AreEqual(grids.Rows(board), replayed, $"Seed {seed}: replaying the falls didn't match the board.");
            }
        }

        /// <summary>
        /// Applies <paramref name="falls"/> in order to a copy of <paramref name="rows"/>, checking
        /// along the way that each one is a legal move: straight down, from a filled cell, into
        /// an empty one.
        /// </summary>
        private static string[] Replay(string[] rows, List<BoardModel.Fall> falls, int seed)
        {
            int height = rows.Length;
            // char[][] is an array of rows we can edit; strings can't be changed in place.
            var cells = new char[height][];
            for (int i = 0; i < height; i++)
            {
                cells[i] = rows[i].ToCharArray();
            }

            foreach (BoardModel.Fall fall in falls)
            {
                string move = $"Seed {seed}: fall {Describe(fall)}";
                Assert.AreEqual(fall.From.x, fall.To.x, $"{move} changes column.");
                Assert.Less(fall.To.y, fall.From.y, $"{move} doesn't go down.");

                // Row index in the strings counts from the top; y counts from the bottom.
                // `ref` makes each local an alias for that slot in the array (not a copy), so
                // assigning to it below writes straight into `cells`.
                ref char fromCell = ref cells[height - 1 - fall.From.y][fall.From.x];
                ref char toCell = ref cells[height - 1 - fall.To.y][fall.To.x];
                Assert.AreNotEqual(TestGrids.Empty, fromCell, $"{move} starts from an empty cell.");
                Assert.AreEqual(TestGrids.Empty, toCell, $"{move} lands on a token.");

                toCell = fromCell;
                fromCell = TestGrids.Empty;
            }

            var result = new string[height];
            for (int i = 0; i < height; i++)
            {
                result[i] = new string(cells[i]);
            }
            return result;
        }

        // ---------------------------------------------------------------- Refill: examples

        [Test]
        public void Refill_FillsOnlyTheEmptyCells_BottomToTopByColumn()
        {
            BoardModel board = Grid(
                "R..",
                "GB.",
                "RGB");

            List<BoardModel.Spawn> spawns = board.Refill();

            // Column 0 was full, column 1 had one gap, column 2 had two.
            Assert.AreEqual(3, spawns.Count);
            Assert.AreEqual(new Vector2Int(1, 2), spawns[0].Cell);
            Assert.AreEqual(new Vector2Int(2, 1), spawns[1].Cell);
            Assert.AreEqual(new Vector2Int(2, 2), spawns[2].Cell);

            // Each new token starts as far above its cell as its column's gap is tall.
            Assert.AreEqual(1, spawns[0].FallDistance);
            Assert.AreEqual(2, spawns[1].FallDistance);
            Assert.AreEqual(2, spawns[2].FallDistance);

            // The tokens that were already there are untouched.
            Assert.AreEqual(grids.Type('R'), board.TypeAt(0, 2));
            Assert.AreEqual(grids.Type('G'), board.TypeAt(0, 1));
            Assert.AreEqual(grids.Type('B'), board.TypeAt(1, 1));
            Assert.AreEqual(grids.Type('R'), board.TypeAt(0, 0));
            Assert.AreEqual(grids.Type('G'), board.TypeAt(1, 0));
            Assert.AreEqual(grids.Type('B'), board.TypeAt(2, 0));
        }

        [Test]
        public void Refill_FullBoard_SpawnsNothing()
        {
            BoardModel board = Grid(
                "RG",
                "GR");

            Assert.IsEmpty(board.Refill());
            AssertBoard(board,
                "RG",
                "GR");
        }

        // ---------------------------------------------------------------- Refill: properties

        [Test]
        public void CollapseThenRefill_ManyRandomBoards_EveryCellIsFilled()
        {
            for (int seed = 0; seed < RandomBoardCount; seed++)
            {
                BoardModel board = RandomBoardWithGaps(seed);
                board.Collapse();
                board.Refill();

                for (int x = 0; x < board.Width; x++)
                {
                    Assert.AreEqual(0, CountEmpty(board, x), $"Seed {seed}: column {x} still has gaps.");
                }
            }
        }

        [Test]
        public void CollapseThenRefill_ManyRandomBoards_SpawnsPerColumnEqualCellsCleared()
        {
            for (int seed = 0; seed < RandomBoardCount; seed++)
            {
                BoardModel board = RandomBoardWithGaps(seed);

                // How many cells each column lost, counted before gravity moves anything.
                var cleared = new int[board.Width];
                for (int x = 0; x < board.Width; x++)
                {
                    cleared[x] = CountEmpty(board, x);
                }

                board.Collapse();
                List<BoardModel.Spawn> spawns = board.Refill();

                var spawned = new int[board.Width];
                foreach (BoardModel.Spawn spawn in spawns)
                {
                    spawned[spawn.Cell.x]++;
                }
                Assert.AreEqual(cleared, spawned, $"Seed {seed}: spawns per column (actual) vs cells cleared per column (expected).");
            }
        }

        [Test]
        public void CollapseThenRefill_ManyRandomBoards_SpawnsDescribeExactlyWhatChanged()
        {
            // Board creates a GameObject for each Spawn, so every spawn must match the grid:
            // one per cell that was empty, of the type now in that cell, and nothing else changed.
            for (int seed = 0; seed < RandomBoardCount; seed++)
            {
                BoardModel board = RandomBoardWithGaps(seed);
                board.Collapse();

                // Snapshot the collapsed board: which cells are empty, and what's in the rest.
                var before = new TokenType[board.Width, board.Height];
                for (int x = 0; x < board.Width; x++)
                {
                    for (int y = 0; y < board.Height; y++)
                    {
                        before[x, y] = board.TypeAt(x, y);
                    }
                }

                List<BoardModel.Spawn> spawns = board.Refill();

                var spawnedCells = new HashSet<Vector2Int>();
                foreach (BoardModel.Spawn spawn in spawns)
                {
                    string where = $"Seed {seed}: spawn at ({spawn.Cell.x},{spawn.Cell.y})";
                    Assert.IsTrue(spawnedCells.Add(spawn.Cell), $"{where} is listed twice.");
                    Assert.IsNull(before[spawn.Cell.x, spawn.Cell.y], $"{where} replaced an existing token.");
                    Assert.IsNotNull(spawn.Type, $"{where} has no type.");
                    Assert.AreEqual(board.TypeAt(spawn.Cell), spawn.Type, $"{where} doesn't match the grid.");
                    Assert.AreEqual(CountNull(before, spawn.Cell.x), spawn.FallDistance, $"{where} has the wrong fall distance.");
                }

                // Every cell not in the spawn list must be exactly as it was.
                for (int x = 0; x < board.Width; x++)
                {
                    for (int y = 0; y < board.Height; y++)
                    {
                        if (!spawnedCells.Contains(new Vector2Int(x, y)))
                        {
                            Assert.AreEqual(before[x, y], board.TypeAt(x, y), $"Seed {seed}: ({x},{y}) changed without a spawn.");
                        }
                    }
                }
            }
        }

        private static int CountNull(TokenType[,] cells, int x)
        {
            int count = 0;
            for (int y = 0; y < cells.GetLength(1); y++)
            {
                if (cells[x, y] == null)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
