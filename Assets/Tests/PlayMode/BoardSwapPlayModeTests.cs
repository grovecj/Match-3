using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace Match3.Tests
{
    /// <summary>
    /// End-to-end Play Mode tests: a real <see cref="Board"/> runs a whole swap through its
    /// coroutines, frame by frame, exactly as in the game. That catches what the Edit Mode
    /// tests can't see: timing, Destroy (which only happens at the end of a frame), and the
    /// model and the on-screen tokens drifting apart.
    ///
    /// Play Mode tests are slower than Edit Mode ones (each needs real frames to pass), so keep
    /// them few and use them for "does it all fit together", leaving the rules to the unit tests.
    /// </summary>
    public class BoardSwapPlayModeTests
    {
        // If a resolve takes longer than this, something is stuck. With the sped-up animations
        // below a swap finishes in a handful of frames, so this is very generous.
        private const float ResolveTimeoutSeconds = 5f;

        // [Timeout] (in milliseconds) is a backstop for the whole test: if it somehow hangs
        // outside WaitUntilIdle, NUnit fails it rather than letting the Test Runner spin forever.
        private const int TestTimeoutMs = 10000;

        private static readonly Vector3EqualityComparer Vector3Comparer = new Vector3EqualityComparer(0.0001f);

        // The known starting board, top row first (like TestGrids in the Edit Mode tests). Each
        // letter is a token type. There are no matches to begin with, and:
        //   - swapping (2,0) with (2,1) moves an A down into the bottom row: AAA, a run of 3;
        //   - swapping (0,4) with (1,4) (the top-left pair) makes no match at all.
        private static readonly string[] Layout =
        {
            "ABCDE", // y = 4
            "CDEAB",
            "EABCD",
            "BCAEB",
            "AABCD", // y = 0
        };

        private static readonly Vector2Int MatchA = new Vector2Int(2, 0);
        private static readonly Vector2Int MatchB = new Vector2Int(2, 1);
        private static readonly Vector2Int NoMatchA = new Vector2Int(0, 4);
        private static readonly Vector2Int NoMatchB = new Vector2Int(1, 4);

        // A second starting board (also match-free) for a T shape: swapping (2,1) with (3,1)
        // moves an A left into (2,1), which completes both AAA along row 1 and AAAA up column 2.
        // The two runs share the cell (2,1).
        private static readonly string[] TShapeLayout =
        {
            "EBDCD", // y = 4
            "BCADE",
            "DEACD",
            "AABAC",
            "CDAEB", // y = 0
        };

        private static readonly Vector2Int TShapeA = new Vector2Int(2, 1);
        private static readonly Vector2Int TShapeB = new Vector2Int(3, 1);

        private readonly Dictionary<char, TokenType> types = new Dictionary<char, TokenType>();
        private GameObject themeObject;
        private GameObject cellTemplate;
        private GameObject tokenTemplate;
        private GameObject boardObject;
        private Board board;

        // Every run length reported through Board.MatchCleared, in order.
        private List<int> clearedRuns;

        // Every (longest run, cascade index) reported through Board.ClearStepResolved, in order.
        private List<(int longestRun, int cascadeIndex)> clearSteps;

        // Board.SwapStarted and Board.SwapRejected, recorded as "started"/"rejected" in the
        // order they fired, so a test can check both which ones fired and their order.
        private List<string> swapEvents;

        // [UnitySetUp] is [SetUp] that can span frames: it's a coroutine, so it can
        // `yield return null` ("wait one frame") to let Unity run Start before the test begins.
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            foreach (char letter in "ABCDE")
            {
                TokenType type = ScriptableObject.CreateInstance<TokenType>();
                type.name = letter.ToString();
                types.Add(letter, type);
            }

            // Stand-ins for what the scene provides. An empty ThemeManager means no colors or
            // sprites (tokens are invisible), which the logic doesn't care about. Any live
            // GameObject can be cloned with Instantiate, so plain objects work as "prefabs".
            themeObject = new GameObject("Theme Manager");
            ThemeManager themeManager = themeObject.AddComponent<ThemeManager>();
            cellTemplate = new GameObject("Cell Template", typeof(SpriteRenderer));
            tokenTemplate = new GameObject("Token Template", typeof(Token));

            // Create the Board inactive: an inactive object's Awake/OnEnable/Start wait until it's
            // activated, which gives us the chance to configure it first, like the Inspector would.
            boardObject = new GameObject("Board");
            boardObject.SetActive(false);
            board = boardObject.AddComponent<Board>();
            board.ConfigureForTests(
                width: Layout[0].Length,
                height: Layout.Length,
                cellPrefab: cellTemplate.GetComponent<SpriteRenderer>(),
                tokenPrefab: tokenTemplate.GetComponent<Token>(),
                themeManager: themeManager,
                tokenTypes: new List<TokenType>(types.Values).ToArray(),
                seed: 12345,
                // Speed the animations right up so each test only takes a few frames.
                swapDuration: 0.01f,
                clearDelay: 0f,
                fallSpeed: 1000f);
            boardObject.SetActive(true);

            // Start runs just before the object's first frame; wait for it to deal its
            // (random) board, then swap in the known layout.
            yield return null;
            board.LoadLayoutForTests(BuildLayout(Layout));

            clearedRuns = new List<int>();
            board.MatchCleared += clearedRuns.Add;
            clearSteps = new List<(int, int)>();
            // A lambda (an unnamed method written inline) turns the event's two ints into one
            // tuple for the list.
            board.ClearStepResolved += (longestRun, cascadeIndex) => clearSteps.Add((longestRun, cascadeIndex));
            swapEvents = new List<string>();
            board.SwapStarted += () => swapEvents.Add("started");
            board.SwapRejected += () => swapEvents.Add("rejected");
        }

        // Runs after every test, even a failing one. In Play Mode, Destroy is the normal way to
        // remove objects: they go at the end of the frame, before the next test starts.
        [TearDown]
        public void TearDown()
        {
            Object.Destroy(boardObject);
            Object.Destroy(tokenTemplate);
            Object.Destroy(cellTemplate);
            Object.Destroy(themeObject);
            foreach (TokenType type in types.Values)
            {
                Object.Destroy(type);
            }
            types.Clear();
        }

        // ---------------------------------------------------------------- Tests

        // [UnityTest] marks a test that is a coroutine: every `yield return` hands control back
        // to Unity, which runs a frame (Update, the Board's coroutines...) and then resumes the
        // test. That's what lets a test watch something happen over time.
        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator MatchingSwap_ClearsTheRunAndLeavesAFullSettledBoard()
        {
            Token matchedA0 = board.GetToken(0, 0);
            Token matchedA1 = board.GetToken(1, 0);
            Token movingA = board.GetToken(MatchB.x, MatchB.y);

            Assert.IsTrue(board.TrySwap(MatchA, MatchB), "Swapping two neighbors should be accepted.");
            Assert.IsTrue(board.IsResolving, "The board should lock as soon as a swap starts.");

            yield return WaitUntilIdle();

            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();

            // Unity overloads == so a destroyed object compares equal to null, even though the
            // C# reference still points at it. All three A's in the run should be gone.
            Assert.IsTrue(matchedA0 == null, "The token at (0, 0) was matched but not destroyed.");
            Assert.IsTrue(matchedA1 == null, "The token at (1, 0) was matched but not destroyed.");
            Assert.IsTrue(movingA == null, "The token swapped into (2, 0) was matched but not destroyed.");

            // The swap itself makes one run of 3, so that's reported first. Refills are random
            // (seeded, but we don't want the test to depend on exactly what they deal), so any
            // cascades that follow are only checked to be real matches.
            Assert.That(clearedRuns, Is.Not.Empty, "MatchCleared never fired.");
            Assert.AreEqual(3, clearedRuns[0], "The first run cleared should be the swap's run of 3.");
            Assert.That(clearedRuns, Has.All.GreaterThanOrEqualTo(BoardModel.MinMatchLength));

            // One step per pass of the cascade loop: the swap's own step first (index 0), then
            // each cascade numbered 1, 2, 3... in order.
            Assert.That(clearSteps, Is.Not.Empty, "ClearStepResolved never fired.");
            Assert.AreEqual((3, 0), clearSteps[0], "The first step should be the swap's run of 3, at index 0.");
            for (int i = 0; i < clearSteps.Count; i++)
            {
                Assert.AreEqual(i, clearSteps[i].cascadeIndex, $"Step {i} has the wrong cascade index.");
            }

            // One swap sound for the player's swap, and none for the refills and cascades after it.
            Assert.AreEqual(new[] { "started" }, swapEvents,
                "A matching swap should fire SwapStarted once and never SwapRejected.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator TShapedMatch_IsOneClearStepWithTheLongestRun()
        {
            board.LoadLayoutForTests(BuildLayout(TShapeLayout));

            Assert.IsTrue(board.TrySwap(TShapeA, TShapeB));
            yield return WaitUntilIdle();

            // Scoring still sees both runs (rows are reported before columns)...
            Assert.That(clearedRuns.Count, Is.GreaterThanOrEqualTo(2));
            Assert.AreEqual(3, clearedRuns[0], "The first run should be the row of 3.");
            Assert.AreEqual(4, clearedRuns[1], "The second run should be the column of 4.");

            // ...but they were cleared together, so they make a single step, sized by the longer run.
            Assert.That(clearSteps, Is.Not.Empty, "ClearStepResolved never fired.");
            Assert.AreEqual((4, 0), clearSteps[0], "The T should be one step with a longest run of 4.");
            Assert.That(clearSteps.Count, Is.LessThan(clearedRuns.Count),
                "Two runs cleared together should make fewer steps than runs.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator SwapWithoutMatch_PutsBothTokensBack()
        {
            Token tokenA = board.GetToken(NoMatchA.x, NoMatchA.y);
            Token tokenB = board.GetToken(NoMatchB.x, NoMatchB.y);

            // TrySwap only checks that the cells are neighbors; whether the swap makes a match
            // is found out while resolving, so it's still accepted and the board still locks.
            Assert.IsTrue(board.TrySwap(NoMatchA, NoMatchB));
            Assert.IsTrue(board.IsResolving);

            yield return WaitUntilIdle();

            Assert.AreSame(tokenA, board.GetToken(NoMatchA.x, NoMatchA.y), "Token A should be back in its own cell.");
            Assert.AreSame(tokenB, board.GetToken(NoMatchB.x, NoMatchB.y), "Token B should be back in its own cell.");
            AssertBoardIsFullAndSettled();
            Assert.That(clearedRuns, Is.Empty, "Nothing matched, so MatchCleared shouldn't fire.");
            Assert.AreEqual(new[] { "started", "rejected" }, swapEvents,
                "An invalid swap should fire SwapStarted, then SwapRejected, once each.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator SecondSwapWhileResolving_IsRejectedUntilTheBoardIsIdle()
        {
            Assert.IsTrue(board.TrySwap(MatchA, MatchB));

            // Any other swap, valid or not, has to wait for the first one to finish.
            Assert.IsFalse(board.TrySwap(NoMatchA, NoMatchB), "A second swap should be rejected while resolving.");
            Assert.IsTrue(board.IsResolving, "The rejected swap mustn't unlock the board.");
            // StartCoroutine runs a coroutine up to its first `yield` straight away, so the first
            // swap has already fired SwapStarted. The refused one mustn't fire anything.
            Assert.AreEqual(new[] { "started" }, swapEvents, "A refused TrySwap shouldn't fire swap events.");

            yield return WaitUntilIdle();

            // Once idle, swaps are accepted again. Let this one finish too so the test ends clean.
            Assert.IsTrue(board.TrySwap(NoMatchA, NoMatchB), "Swaps should be accepted again once the board is idle.");
            yield return WaitUntilIdle();
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>Turns <paramref name="rows"/> (top row first) into a [x, y] array of token types.</summary>
        private TokenType[,] BuildLayout(string[] rows)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            var layout = new TokenType[width, height];
            for (int i = 0; i < height; i++)
            {
                // rows[0] is the top of the board, so string index i is grid row height-1-i.
                int y = height - 1 - i;
                for (int x = 0; x < width; x++)
                {
                    layout[x, y] = types[rows[i][x]];
                }
            }
            return layout;
        }

        /// <summary>
        /// Waits (frame by frame) until the board stops resolving, failing the test if that
        /// takes longer than <see cref="ResolveTimeoutSeconds"/>.
        /// </summary>
        // A test coroutine can `yield return` another IEnumerator; the Test Runner runs it to
        // the end before carrying on, just like `yield return StartCoroutine(...)` in Board.
        private IEnumerator WaitUntilIdle()
        {
            bool timedOut = false;
            // WaitUntil re-checks its condition every frame and resumes once it's true. This
            // overload also gives up after a timeout and calls the last argument when it does.
            yield return new WaitUntil(
                () => !board.IsResolving,
                System.TimeSpan.FromSeconds(ResolveTimeoutSeconds),
                () => timedOut = true);

            Assert.IsFalse(timedOut, $"The board was still resolving after {ResolveTimeoutSeconds} seconds.");
        }

        /// <summary>
        /// Every cell holds its own live token, every token sits exactly on its cell's center,
        /// and there are no other tokens in the scene under the board. If the view array and the
        /// scene disagreed, a token would be missing, off-center, shared by two cells, or left
        /// behind as an orphan (say, a cleared token that was never destroyed).
        /// </summary>
        private void AssertBoardIsFullAndSettled()
        {
            // A HashSet holds each item at most once, so it can spot a token filed under two cells.
            var cellTokens = new HashSet<Token>();
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    Token token = board.GetToken(x, y);
                    Assert.IsTrue(token != null, $"Cell ({x}, {y}) has no token.");
                    Assert.IsTrue(cellTokens.Add(token), $"The token in cell ({x}, {y}) is also in another cell.");
                    Assert.That(token.transform.position, Is.EqualTo(board.GridToWorld(x, y)).Using(Vector3Comparer),
                        $"The token in cell ({x}, {y}) isn't on the cell's center.");
                }
            }

            // GetComponentsInChildren searches the whole hierarchy under the board, so it finds
            // every Token GameObject, including any the Board has lost track of.
            Token[] sceneTokens = boardObject.GetComponentsInChildren<Token>();
            Assert.That(sceneTokens, Is.EquivalentTo(cellTokens),
                "The tokens under the board should be exactly the ones in its cells, with no orphans.");
        }

        /// <summary>
        /// No 3-in-a-row is left on screen: the cascade loop only stops once the model has no
        /// matches, so if the tokens show one, the view has drifted from the model.
        /// </summary>
        private void AssertNoMatchesLeft()
        {
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    TokenType type = board.GetToken(x, y).Type;
                    bool rightTwoMatch = x + 2 < board.Width &&
                                         board.GetToken(x + 1, y).Type == type &&
                                         board.GetToken(x + 2, y).Type == type;
                    bool upTwoMatch = y + 2 < board.Height &&
                                      board.GetToken(x, y + 1).Type == type &&
                                      board.GetToken(x, y + 2).Type == type;
                    Assert.IsFalse(rightTwoMatch, $"Three in a row left on screen starting at ({x}, {y}).");
                    Assert.IsFalse(upTwoMatch, $"Three in a column left on screen starting at ({x}, {y}).");
                }
            }
        }
    }
}
