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

        // A third starting board, with no matches and no valid moves either: each row is the
        // one below it shifted two places, so no single swap can line three of a type up.
        private static readonly string[] DeadLayout =
        {
            "DEABC", // y = 4
            "BCDEA",
            "EABCD",
            "CDEAB",
            "ABCDE", // y = 0
        };

        private readonly Dictionary<char, TokenType> types = new Dictionary<char, TokenType>();
        private GameObject themeObject;
        private GameObject cellTemplate;
        private GameObject tokenTemplate;
        private GameObject explosionTemplates;
        private GameObject boardObject;
        private Board board;

        // Every run length reported through Board.MatchCleared, in order.
        private List<int> clearedRuns;

        // Every (longest run, cascade index) reported through Board.ClearStepResolved, in order.
        private List<(int longestRun, int cascadeIndex)> clearSteps;

        // The run length of every burst on screen when the swap's own clear step (index 0) was
        // reported, which is just after the Board spawned that step's bursts.
        private List<int> firstStepBursts;

        // Board.SwapStarted and Board.SwapRejected, recorded as "started"/"rejected" in the
        // order they fired, so a test can check both which ones fired and their order.
        private List<string> swapEvents;

        // How many times Board.NoMovesLeft has fired.
        private int noMovesCount;

        // Board.IsReshuffling, as it was each time Board.NoMovesLeft fired.
        private List<bool> reshufflingWhenAnnounced;

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
            Explosion explosionTemplate = CreateExplosionTemplate();

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
                fallSpeed: 1000f,
                // Long enough to outlast a slow frame, so the pause test can catch tokens mid-pop.
                popDuration: 0.25f,
                explosionPrefab: explosionTemplate,
                // Long enough to outlast a slow frame, so the pause test can catch it mid-spring.
                springBackDuration: 0.25f);
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
            firstStepBursts = null;
            board.ClearStepResolved += (_, cascadeIndex) =>
            {
                if (cascadeIndex == 0)
                {
                    firstStepBursts = new List<int>();
                    foreach (Explosion explosion in Bursts())
                    {
                        firstStepBursts.Add(explosion.RunLength);
                    }
                }
            };
            swapEvents = new List<string>();
            board.SwapStarted += () => swapEvents.Add("started");
            board.SwapRejected += () => swapEvents.Add("rejected");
            noMovesCount = 0;
            board.NoMovesLeft += () => noMovesCount++;
            reshufflingWhenAnnounced = new List<bool>();
            board.NoMovesLeft += () => reshufflingWhenAnnounced.Add(board.IsReshuffling);
        }

        // Runs after every test, even a failing one. In Play Mode, Destroy is the normal way to
        // remove objects: they go at the end of the frame, before the next test starts.
        [TearDown]
        public void TearDown()
        {
            // A test that pauses might fail before unpausing, and timeScale outlives the test.
            Time.timeScale = 1f;
            Object.Destroy(boardObject);
            Object.Destroy(explosionTemplates);
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
        public IEnumerator RoundStatsTracker_CountsWhatTheBoardReports()
        {
            // The tracker is what the server's achievements are worked out from, so it has to
            // agree with the Board's events (which are also what the score is added up from).
            var trackerObject = new GameObject("Round Stats Tracker");
            trackerObject.SetActive(false);
            RoundStatsTracker tracker = trackerObject.AddComponent<RoundStatsTracker>();
            tracker.ConfigureForTests(board);
            trackerObject.SetActive(true);
            board.LoadLayoutForTests(BuildLayout(TShapeLayout));

            // One swap that makes no match (it still counts as a swap), then the T.
            Assert.IsTrue(board.TrySwap(NoMatchA, NoMatchB));
            yield return WaitUntilIdle();
            Assert.IsTrue(board.TrySwap(TShapeA, TShapeB));
            yield return WaitUntilIdle();

            RoundStats stats = tracker.Stats;
            Object.Destroy(trackerObject);

            Assert.AreEqual(2, stats.swaps);
            Assert.AreEqual(clearedRuns.FindAll(run => run == 3).Count, stats.runs3);
            Assert.AreEqual(clearedRuns.FindAll(run => run == 4).Count, stats.runs4);
            Assert.AreEqual(clearedRuns.FindAll(run => run >= 5).Count, stats.runs5);
            Assert.That(stats.runs3, Is.GreaterThanOrEqualTo(1), "The T's row of 3.");
            Assert.That(stats.runs4, Is.GreaterThanOrEqualTo(1), "The T's column of 4.");
            Assert.That(stats.longestRun, Is.GreaterThanOrEqualTo(4));
            // Only the T cleared anything, so the chain is however many steps it set off.
            Assert.AreEqual(clearSteps.Count, stats.bestChain);

            tracker.ResetStats();
            Assert.AreEqual(0, tracker.Stats.swaps, "ResetStats should start the count again.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator TShapedMatch_BurstsOncePerCellSizedByItsLongestRun_ThenCleansUp()
        {
            board.LoadLayoutForTests(BuildLayout(TShapeLayout));

            Assert.IsTrue(board.TrySwap(TShapeA, TShapeB));
            yield return WaitUntilIdle();

            // A row of 3 and a column of 4 sharing one cell: 6 cells, so 6 bursts, not 7. The
            // shared cell bursts at the column's size, so 4 of them are 4s.
            Assert.IsNotNull(firstStepBursts, "The swap's clear step was never reported.");
            CollectionAssert.AreEquivalent(new[] { 3, 3, 4, 4, 4, 4 }, firstStepBursts);

            // Every burst destroys itself once its particles have faded, so none pile up.
            yield return WaitUntilNoBursts();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator Pausing_FreezesThePopAndTheBursts()
        {
            // One of the tokens this swap matches, to watch it pop.
            Token popping = board.GetToken(0, 0);
            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            // Let the swap finish and the clear begin: the bursts appear as the tokens start popping.
            yield return new WaitUntil(() => Bursts().Length > 0);

            // Pause the way the GameManager does. The new timeScale only applies from the next
            // frame (this one's deltaTime is already worked out), so let one frame go by before
            // taking a snapshot of where everything is.
            Time.timeScale = 0f;
            yield return null;
            Explosion[] bursts = Bursts();
            var burstTimes = new List<float>();
            foreach (Explosion burst in bursts)
            {
                // ParticleSystem.time: how far into its playback the system is, in (scaled) seconds.
                burstTimes.Add(burst.GetComponent<ParticleSystem>().time);
            }
            Assert.IsTrue(board.IsResolving, "The turn should still be resolving when the pause lands.");
            Assert.IsTrue(popping != null, "The matched token should still be mid-pop when the pause lands.");
            Vector3 popScale = popping.transform.localScale;

            // Real (unscaled) seconds keep passing while paused: plenty of time for things to
            // move on, if they weren't frozen.
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.IsTrue(popping != null, "The popping token was destroyed while paused.");
            Assert.AreEqual(popScale, popping.transform.localScale, "The pop moved on while paused.");

            Assert.AreEqual(bursts.Length, Bursts().Length, "Bursts shouldn't finish (and destroy themselves) while paused.");
            for (int i = 0; i < bursts.Length; i++)
            {
                Assert.AreEqual(burstTimes[i], bursts[i].GetComponent<ParticleSystem>().time,
                    "A burst's particles moved on while paused.");
            }
            Assert.IsTrue(board.IsResolving, "The turn shouldn't move on while paused.");

            Time.timeScale = 1f;
            yield return WaitUntilIdle();
            yield return WaitUntilNoBursts();
            AssertBoardIsFullAndSettled();
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
            Assert.AreEqual(0, noMovesCount, "The board still has a move, so it shouldn't reshuffle.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator BoardWithNoValidMoves_ReshufflesItsOwnTokens()
        {
            board.LoadLayoutForTests(BuildLayout(DeadLayout));
            var tokensBefore = new List<Token>();
            var typesBefore = new List<TokenType>();
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    tokensBefore.Add(board.GetToken(x, y));
                    typesBefore.Add(board.GetToken(x, y).Type);
                }
            }

            // The board checks for moves whenever it settles, and a swap that's put back counts.
            // On this board every swap is put back, so any one will do.
            Assert.IsTrue(board.TrySwap(NoMatchA, NoMatchB));
            yield return WaitUntilIdle();

            Assert.AreEqual(1, noMovesCount, "NoMovesLeft should fire once for the one reshuffle.");
            // The GameManager stops the clock while IsReshuffling is true, so it has to cover
            // the whole reshuffle (from the announcement on) and nothing after it.
            Assert.AreEqual(new[] { true }, reshufflingWhenAnnounced, "IsReshuffling should already be true when NoMovesLeft fires.");
            Assert.IsFalse(board.IsReshuffling, "IsReshuffling should be false again once the board is idle.");
            Assert.That(clearedRuns, Is.Empty, "A reshuffle must not hand out free matches.");
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();

            // The same Token objects, just in different cells: nothing destroyed, nothing spawned.
            var tokensAfter = new List<Token>();
            var typesAfter = new List<TokenType>();
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    tokensAfter.Add(board.GetToken(x, y));
                    typesAfter.Add(board.GetToken(x, y).Type);
                }
            }
            Assert.That(tokensAfter, Is.EquivalentTo(tokensBefore), "A reshuffle should move the existing tokens.");
            Assert.That(typesAfter, Is.Not.EqualTo(typesBefore), "The layout should have changed.");
        }

        // ---------------------------------------------------------------- Hints

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator ShowHint_PulsesTheTwoTokensOfAMoveThatReallyMatches()
        {
            Assert.IsFalse(board.IsHintShowing);
            Assert.IsTrue(board.ShowHint(), "This layout has a valid move, so there's a hint to show.");
            Assert.IsTrue(board.IsHintShowing);

            // Find the hinted tokens by looking at the board, the way the player would.
            var hinted = new List<Vector2Int>();
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    if (board.GetToken(x, y).CurrentHighlight == Token.Highlight.Hint)
                    {
                        hinted.Add(new Vector2Int(x, y));
                    }
                }
            }
            Assert.AreEqual(2, hinted.Count, "A hint is exactly the two tokens to swap.");

            // A hinted token pulses bigger than its resting size, without being picked up.
            Token hintedToken = board.GetToken(hinted[0].x, hinted[0].y);
            Vector3 restScale = hintedToken.transform.localScale;
            float biggest = restScale.x;
            for (float waited = 0f; waited < 0.5f; waited += Time.deltaTime)
            {
                biggest = Mathf.Max(biggest, hintedToken.transform.localScale.x);
                yield return null;
            }
            Assert.That(biggest, Is.GreaterThan(restScale.x), "A hinted token should pulse.");
            Assert.IsFalse(board.IsHolding, "Showing a hint mustn't pick anything up.");

            // Taking the hint ends it, puts both tokens back to normal, and makes a match.
            Assert.IsTrue(board.TrySwap(hinted[0], hinted[1]), "The hinted tokens should be neighbors.");
            Assert.IsFalse(board.IsHintShowing, "Swapping should clear the hint.");
            Assert.AreEqual(Token.Highlight.None, hintedToken.CurrentHighlight);
            yield return WaitUntilIdle();
            Assert.That(clearedRuns, Is.Not.Empty, "The hinted swap should have made a match.");
            AssertBoardIsFullAndSettled();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator PickingUpAHintedToken_EndsTheHint_AndItStillGoesBackToItsOwnSize()
        {
            // On this layout the hint is (1,1) and (2,1): the lowest, leftmost valid move.
            Token hintedToken = board.GetToken(1, 1);
            Token otherHinted = board.GetToken(2, 1);
            Vector3 restScale = hintedToken.transform.localScale;
            Assert.IsTrue(board.ShowHint());
            Assert.AreEqual(Token.Highlight.Hint, hintedToken.CurrentHighlight);
            Assert.AreEqual(Token.Highlight.Hint, otherHinted.CurrentHighlight);

            // Wait until it's visibly mid-pulse, the awkward moment to pick it up.
            yield return new WaitUntil(() => hintedToken.transform.localScale.x > restScale.x * 1.02f);
            Assert.IsTrue(board.TryPickUp(new Vector2Int(1, 1)));

            Assert.IsFalse(board.IsHintShowing, "Picking a token up should clear the hint.");
            Assert.AreEqual(Token.Highlight.Held, hintedToken.CurrentHighlight);
            Assert.AreEqual(Token.Highlight.None, otherHinted.CurrentHighlight);

            board.DropHeld();
            yield return new WaitForSeconds(LiftSettleSeconds);
            Assert.That(hintedToken.transform.localScale, Is.EqualTo(restScale).Using(Vector3Comparer),
                "A token picked up mid-pulse should still settle at exactly its old size.");
            Assert.That(otherHinted.transform.localScale, Is.EqualTo(restScale).Using(Vector3Comparer));
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator ShowHint_IsRefusedWhileResolvingOrHolding()
        {
            Assert.IsTrue(board.TryPickUp(NoMatchA));
            Assert.IsFalse(board.ShowHint(), "No hints while the player is holding a token.");
            board.DropHeld();

            Assert.IsTrue(board.TrySwap(NoMatchA, NoMatchB));
            Assert.IsFalse(board.ShowHint(), "No hints while a swap resolves.");
            Assert.IsFalse(board.IsHintShowing);
            yield return WaitUntilIdle();

            Assert.IsTrue(board.ShowHint(), "Hints work again once the board is idle.");
            board.ClearHint();
            Assert.IsFalse(board.IsHintShowing);
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

        // ---------------------------------------------------------------- Holding and dragging
        //
        // These drive the Board's hold/drag API directly, the way BoardInput does. The decisions
        // BoardInput makes (which axis, how far, swap or spring back) are pure math, covered by
        // the Edit Mode BoardInputDragTests.

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator PickingUp_GrowsTheToken_AndPuttingItDown_ShrinksItBack()
        {
            Token token = board.GetToken(NoMatchA.x, NoMatchA.y);
            Vector3 restScale = token.transform.localScale;

            Assert.IsTrue(board.TryPickUp(NoMatchA));
            Assert.IsTrue(board.IsHolding);
            Assert.AreEqual(Token.Highlight.Held, token.CurrentHighlight);
            yield return new WaitForSeconds(LiftSettleSeconds);
            Assert.That(token.transform.localScale.x, Is.GreaterThan(restScale.x), "A held token should be bigger.");

            board.DropHeld();
            Assert.IsFalse(board.IsHolding);
            Assert.IsFalse(board.IsResolving, "A token that was never dragged has nothing to spring back.");
            yield return new WaitForSeconds(LiftSettleSeconds);
            Assert.That(token.transform.localScale, Is.EqualTo(restScale).Using(Vector3Comparer),
                "A token put down should go back to exactly its old size.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator DraggedSwap_PushesTheNeighbor_ThenSwapsFromThere()
        {
            // Drag the A at (2,1) halfway down toward (2,0).
            Assert.IsTrue(board.TryPickUp(MatchB));
            board.DragHeld(Vector2Int.down, 0.5f);

            Token held = board.GetToken(MatchB.x, MatchB.y);
            Token neighbor = board.GetToken(MatchA.x, MatchA.y);
            Vector3 halfCellDown = Vector3.down * (0.5f * board.CellSize);
            Assert.That(held.transform.position, Is.EqualTo(board.GridToWorld(MatchB.x, MatchB.y) + halfCellDown).Using(Vector3Comparer),
                "The held token should follow the drag.");
            Assert.That(neighbor.transform.position, Is.EqualTo(board.GridToWorld(MatchA.x, MatchA.y) - halfCellDown).Using(Vector3Comparer),
                "The neighbor should slide the other way to make room.");
            Assert.That(swapEvents, Is.Empty, "Dragging alone isn't a swap.");

            Assert.IsTrue(board.TrySwap(MatchB, MatchA));
            Assert.IsFalse(board.IsHolding, "Swapping should put the held token down.");
            // One frame in, the tokens should carry on from the drag, not jump back to their cells.
            yield return null;
            Assert.That(held.transform.position.y, Is.LessThan(board.GridToWorld(MatchB.x, MatchB.y).y - 0.25f),
                "The swap should pick up where the drag left off.");

            yield return WaitUntilIdle();
            AssertBoardIsFullAndSettled();
            Assert.That(clearedRuns, Is.Not.Empty);
            Assert.AreEqual(3, clearedRuns[0], "The dragged swap should make the same run of 3 as a plain swap.");
            Assert.AreEqual(new[] { "started" }, swapEvents);
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator DroppedDrag_SpringsBack_WithoutSwapping()
        {
            Token tokenA = board.GetToken(NoMatchA.x, NoMatchA.y);
            Token tokenB = board.GetToken(NoMatchB.x, NoMatchB.y);

            Assert.IsTrue(board.TryPickUp(NoMatchA));
            board.DragHeld(Vector2Int.right, 0.25f);
            board.DropHeld();
            Assert.IsTrue(board.IsResolving, "The board should stay locked while the tokens spring back.");
            Assert.IsFalse(board.TryPickUp(NoMatchB), "Nothing can be picked up mid-spring.");

            yield return WaitUntilIdle();
            Assert.AreSame(tokenA, board.GetToken(NoMatchA.x, NoMatchA.y), "Token A should still be in its own cell.");
            Assert.AreSame(tokenB, board.GetToken(NoMatchB.x, NoMatchB.y), "Token B should still be in its own cell.");
            AssertBoardIsFullAndSettled();
            Assert.That(swapEvents, Is.Empty, "Springing back isn't a swap, so no swap sounds.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator DragTowardTheEdge_MovesOnlyTheHeldToken()
        {
            // (0,4) is the top-left corner: nothing above it.
            Assert.IsTrue(board.TryPickUp(NoMatchA));
            board.DragHeld(Vector2Int.up, 0.125f);

            Token held = board.GetToken(NoMatchA.x, NoMatchA.y);
            Assert.That(held.transform.position,
                Is.EqualTo(board.GridToWorld(NoMatchA.x, NoMatchA.y) + Vector3.up * (0.125f * board.CellSize)).Using(Vector3Comparer));
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    if (new Vector2Int(x, y) != NoMatchA)
                    {
                        Assert.That(board.GetToken(x, y).transform.position, Is.EqualTo(board.GridToWorld(x, y)).Using(Vector3Comparer),
                            $"The token at ({x}, {y}) shouldn't move.");
                    }
                }
            }

            board.DropHeld();
            yield return WaitUntilIdle();
            AssertBoardIsFullAndSettled();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator ChangingDirection_PutsTheOldNeighborBack()
        {
            // (2,1): drag right toward (3,1), then switch to up toward (2,2).
            Assert.IsTrue(board.TryPickUp(MatchB));
            board.DragHeld(Vector2Int.right, 0.5f);
            board.DragHeld(Vector2Int.up, 0.5f);

            Assert.That(board.GetToken(3, 1).transform.position, Is.EqualTo(board.GridToWorld(3, 1)).Using(Vector3Comparer),
                "The token the drag stopped pushing should be back on its cell.");
            Assert.That(board.GetToken(2, 2).transform.position.y, Is.LessThan(board.GridToWorld(2, 2).y),
                "The new neighbor should be pushed down.");

            board.DropHeld();
            yield return WaitUntilIdle();
            AssertBoardIsFullAndSettled();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator SelectingAnotherToken_PutsTheFirstDown()
        {
            Token first = board.GetToken(NoMatchA.x, NoMatchA.y);
            Token second = board.GetToken(MatchA.x, MatchA.y);

            Assert.IsTrue(board.TryPickUp(NoMatchA));
            board.SetHeldSelected(true);
            Assert.AreEqual(Token.Highlight.Selected, first.CurrentHighlight);

            Assert.IsTrue(board.TryPickUp(MatchA), "Picking up a different token should be allowed.");
            Assert.AreEqual(Token.Highlight.None, first.CurrentHighlight, "The first token should be put down.");
            Assert.AreEqual(Token.Highlight.Held, second.CurrentHighlight);

            board.DropHeld();
            yield return new WaitForSeconds(LiftSettleSeconds);
            Assert.AreEqual(Token.Highlight.None, second.CurrentHighlight);
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator PickingUp_IsRefusedWhileResolving()
        {
            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            Assert.IsFalse(board.TryPickUp(NoMatchA), "Nothing can be picked up while a swap resolves.");
            Assert.IsFalse(board.IsHolding);
            yield return WaitUntilIdle();
            Assert.IsTrue(board.TryPickUp(NoMatchA), "Picking up works again once the board is idle.");
            board.DropHeld();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator Pausing_FreezesTheSpringBack()
        {
            Token held = board.GetToken(NoMatchA.x, NoMatchA.y);
            Assert.IsTrue(board.TryPickUp(NoMatchA));
            board.DragHeld(Vector2Int.right, 0.5f);
            board.DropHeld();

            // As in the pop test: the new timeScale takes effect from the next frame.
            Time.timeScale = 0f;
            yield return null;
            Vector3 frozenAt = held.transform.position;
            Assert.That(frozenAt, Is.Not.EqualTo(board.GridToWorld(NoMatchA.x, NoMatchA.y)).Using(Vector3Comparer),
                "The token should still be on its way back when the pause lands.");

            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(held.transform.position, Is.EqualTo(frozenAt).Using(Vector3Comparer), "The spring-back moved while paused.");
            Assert.IsTrue(board.IsResolving, "The board should stay locked while paused mid-spring.");

            Time.timeScale = 1f;
            yield return WaitUntilIdle();
            AssertBoardIsFullAndSettled();
        }

        // ---------------------------------------------------------------- Helpers

        // Comfortably longer than a token's default lift duration (0.08 s), so it has finished
        // growing or shrinking.
        private const float LiftSettleSeconds = 0.25f;

        /// <summary>
        /// A stand-in for the Explosion prefab: a bare ParticleSystem with a short life that
        /// destroys itself when done, like the real one (but with no textures or flash).
        /// </summary>
        private Explosion CreateExplosionTemplate()
        {
            // The template sits under an inactive parent, so it never wakes up or plays itself
            // (and so never destroys itself). Clones are made under the Board, which is active,
            // so they do.
            explosionTemplates = new GameObject("Explosion Templates");
            explosionTemplates.SetActive(false);
            // Parent it first and add the components after: a new GameObject starts out active,
            // so a ParticleSystem added straight away would already be playing.
            var template = new GameObject("Explosion Template");
            template.transform.SetParent(explosionTemplates.transform);
            // Explosion's [RequireComponent] adds the ParticleSystem too.
            template.AddComponent<Explosion>();

            ParticleSystem.MainModule main = template.GetComponent<ParticleSystem>().main;
            // Long enough to outlast a slow frame (so the pause test finds them still going),
            // short enough that waiting for them to clean up is quick.
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = 0.5f;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            return template.GetComponent<Explosion>();
        }

        /// <summary>Every burst under the board right now.</summary>
        private Explosion[] Bursts()
        {
            return boardObject.GetComponentsInChildren<Explosion>();
        }

        /// <summary>Waits until every burst has destroyed itself, failing the test if some never do.</summary>
        private IEnumerator WaitUntilNoBursts()
        {
            bool timedOut = false;
            yield return new WaitUntil(
                () => Bursts().Length == 0,
                System.TimeSpan.FromSeconds(ResolveTimeoutSeconds),
                () => timedOut = true);

            Assert.IsFalse(timedOut, $"{Bursts().Length} bursts were still around after {ResolveTimeoutSeconds} seconds.");
        }

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
