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

        // A fourth starting board (also match-free) for a line of 4: swapping (2,0) with (2,1)
        // moves an A down into the bottom row, making AAAA along it. Nothing else lines up.
        private static readonly string[] LineOfFourLayout =
        {
            "ABCDE", // y = 4
            "CDEAB",
            "EABCD",
            "BCAEB",
            "AABAD", // y = 0
        };

        private static readonly Vector2Int LineOfFourA = new Vector2Int(2, 0);
        private static readonly Vector2Int LineOfFourB = new Vector2Int(2, 1);

        // A fifth starting board (also match-free) for a line of 5: the same swap, (2,0) with
        // (2,1), now fills the one gap in a whole bottom row of A's.
        private static readonly string[] LineOfFiveLayout =
        {
            "ABCDE", // y = 4
            "CDEAB",
            "EABCD",
            "BCAEB",
            "AABAA", // y = 0
        };

        private static readonly Vector2Int LineOfFiveA = new Vector2Int(2, 0);
        private static readonly Vector2Int LineOfFiveB = new Vector2Int(2, 1);

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
        public IEnumerator TShapedMatch_BurstsOncePerClearedCellSizedByItsRun_ThenCleansUp()
        {
            board.LoadLayoutForTests(BuildLayout(TShapeLayout));

            Assert.IsTrue(board.TrySwap(TShapeA, TShapeB));
            yield return WaitUntilIdle();

            // A row of 3 and a column of 4 sharing one cell: 6 cells. The shared cell is where
            // the T leaves its Star, so it isn't cleared and doesn't burst. That leaves 5
            // bursts: 2 from the row and 3 from the column.
            Assert.IsNotNull(firstStepBursts, "The swap's clear step was never reported.");
            CollectionAssert.AreEquivalent(new[] { 3, 3, 4, 4, 4 }, firstStepBursts);

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

        // ---------------------------------------------------------------- Flame gems
        //
        // The rules (which match makes a Flame, what its blast clears) are covered by the Edit
        // Mode BoardModelFlameTests. These check what only a running Board can show: that the
        // token on screen changes, and that the blast happens in its own phase, in order.

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator LineOfFour_TurnsTheSwappedTokenIntoAFlame()
        {
            board.LoadLayoutForTests(BuildLayout(LineOfFourLayout));
            // The A the player moves: it ends up at (2,0), the swapped cell inside the match.
            Token moved = board.GetToken(LineOfFourB.x, LineOfFourB.y);
            TokenType colour = moved.Type;

            var created = new List<(PowerKind kind, Vector2Int cell)>();
            bool tokenWasAFlame = false;
            bool tokenShowedTheGlow = false;
            int detonations = 0;
            board.PowerGemCreated += (kind, cell) =>
            {
                // Only look at the first one: a cascade could, with luck, make another.
                if (created.Count == 0)
                {
                    Token token = board.GetToken(cell.x, cell.y);
                    tokenWasAFlame = token == moved && token.Power == PowerKind.Flame && token.Type == colour;
                    tokenShowedTheGlow = token.ShowsPowerOverlay;
                }
                created.Add((kind, cell));
            };
            board.PowerGemDetonated += (kind, cell, wave) => detonations++;

            Assert.IsTrue(board.TrySwap(LineOfFourA, LineOfFourB));
            // Stop as soon as the swap's own step has been reported, before any cascade.
            yield return new WaitUntil(() => clearSteps.Count > 0);

            Assert.AreEqual((4, 0), clearSteps[0], "The swap should make a run of 4.");
            Assert.That(created, Is.Not.Empty, "PowerGemCreated never fired.");
            Assert.AreEqual((PowerKind.Flame, LineOfFourA), created[0], "The Flame belongs at the swapped cell.");
            Assert.IsTrue(tokenWasAFlame, "The token the player moved should itself become the Flame, in its own colour.");
            Assert.IsTrue(tokenShowedTheGlow, "The new Flame should show its glow straight away.");
            Assert.AreEqual(0, detonations, "A Flame mustn't go off in the step that made it.");
            // The Flame is a survivor of the match: it's still on the board while the others pop.
            Assert.IsTrue(moved != null && moved.enabled, "The Flame's token was cleared along with the rest of the run.");

            yield return WaitUntilIdle();
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator MatchedFlame_BlastsItsNeighborsAfterTheMatchAndBeforeAnythingFalls()
        {
            // The A at (0,0) is in the run of 3 that MatchA/MatchB make. As a Flame in the
            // corner, its blast reaches (0,1) and (1,1); the rest of its 3x3 is the match itself
            // or off the board.
            var flameCell = new Vector2Int(0, 0);
            board.SetPowerForTests(flameCell, PowerKind.Flame);
            Assert.IsTrue(board.GetToken(0, 0).ShowsPowerOverlay);
            Token blastedA = board.GetToken(0, 1);
            Token blastedB = board.GetToken(1, 1);
            // Sits right above the blast. It will fall, but not until the blast is over.
            Token above = board.GetToken(0, 2);

            bool blastedStillThereWhenMatchReported = false;
            board.ClearStepResolved += (_, cascadeIndex) =>
            {
                if (cascadeIndex == 0)
                {
                    blastedStillThereWhenMatchReported = board.GetToken(0, 1) == blastedA && board.GetToken(1, 1) == blastedB;
                }
            };

            var detonated = new List<(PowerKind kind, Vector2Int cell)>();
            int stepsWhenDetonated = -1;
            int runsWhenDetonated = -1;
            bool blastedGoneWhenDetonated = false;
            bool aboveHadNotMoved = false;
            board.PowerGemDetonated += (kind, cell, wave) =>
            {
                if (detonated.Count == 0)
                {
                    stepsWhenDetonated = clearSteps.Count;
                    runsWhenDetonated = clearedRuns.Count;
                    blastedGoneWhenDetonated = board.GetToken(0, 1) == null && board.GetToken(1, 1) == null;
                    aboveHadNotMoved = board.GetToken(0, 2) == above &&
                                       above.transform.position == board.GridToWorld(0, 2);
                }
                detonated.Add((kind, cell));
            };

            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            yield return WaitUntilIdle();

            Assert.That(detonated, Is.Not.Empty, "PowerGemDetonated never fired.");
            Assert.AreEqual((PowerKind.Flame, flameCell), detonated[0]);
            Assert.IsTrue(blastedStillThereWhenMatchReported,
                "The blast is a later phase: its tokens should still be on the board while the match pops.");
            Assert.AreEqual(1, stepsWhenDetonated, "The blast belongs to the swap's own step, after the match was reported.");
            Assert.AreEqual(1, runsWhenDetonated, "Blasted gems aren't runs: only the run of 3 should have been reported.");
            Assert.IsTrue(blastedGoneWhenDetonated, "The blasted tokens should be off the board once the Flame goes off.");
            Assert.IsTrue(aboveHadNotMoved, "Nothing should fall between a match and its blast.");

            Assert.IsTrue(blastedA == null, "The token at (0, 1) was blasted but not destroyed.");
            Assert.IsTrue(blastedB == null, "The token at (1, 1) was blasted but not destroyed.");
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();
            yield return WaitUntilNoBursts();
        }

        // ---------------------------------------------------------------- Star gems
        //
        // As with the Flame, the rules are covered in Edit Mode (BoardModelStarTests). These
        // check the running Board: the token changes, and the beam's blast is its own phase.

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator TShape_TurnsTheSwappedTokenIntoAStar()
        {
            board.LoadLayoutForTests(BuildLayout(TShapeLayout));
            // The A the player moves: it ends up at (2,1), where the T's two runs cross.
            Token moved = board.GetToken(TShapeB.x, TShapeB.y);
            TokenType colour = moved.Type;

            var created = new List<(PowerKind kind, Vector2Int cell)>();
            bool tokenWasAStar = false;
            bool tokenShowedTheSparkle = false;
            int detonations = 0;
            board.PowerGemCreated += (kind, cell) =>
            {
                // Only look at the first one: a cascade could, with luck, make another.
                if (created.Count == 0)
                {
                    Token token = board.GetToken(cell.x, cell.y);
                    tokenWasAStar = token == moved && token.Power == PowerKind.Star && token.Type == colour;
                    tokenShowedTheSparkle = token.ShowsPowerOverlay &&
                                            token.GetComponentInChildren<StarSparkle>().enabled;
                }
                created.Add((kind, cell));
            };
            board.PowerGemDetonated += (kind, cell, wave) => detonations++;

            Assert.IsTrue(board.TrySwap(TShapeA, TShapeB));
            // Stop as soon as the swap's own step has been reported, before any cascade.
            yield return new WaitUntil(() => clearSteps.Count > 0);

            Assert.That(created, Is.Not.Empty, "PowerGemCreated never fired.");
            Assert.AreEqual((PowerKind.Star, TShapeA), created[0], "The Star belongs at the swapped cell.");
            Assert.IsTrue(tokenWasAStar, "The token the player moved should itself become the Star, in its own colour.");
            Assert.IsTrue(tokenShowedTheSparkle, "The new Star should show its sparkle straight away.");
            Assert.AreEqual(0, detonations, "A Star mustn't go off in the step that made it.");
            // The Star is a survivor of the match: it's still on the board while the others pop.
            Assert.IsTrue(moved != null && moved.enabled, "The Star's token was cleared along with the rest of the match.");

            yield return WaitUntilIdle();
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator MatchedStar_ClearsItsRowAndColumnAfterTheMatchAndBeforeAnythingFalls()
        {
            // The A at (0,0) is in the run of 3 that MatchA/MatchB make along the bottom row.
            // As a Star in the corner, its beam runs along the rest of row 0 and up column 0.
            var starCell = new Vector2Int(0, 0);
            board.SetPowerForTests(starCell, PowerKind.Star);
            Assert.IsTrue(board.GetToken(0, 0).ShowsPowerOverlay);

            // Which token was in which cell once the match had been taken off the board. The
            // matched cells are empty (null) by then; everything else hasn't moved yet.
            Token[,] afterMatch = null;
            board.ClearStepResolved += (_, cascadeIndex) =>
            {
                if (cascadeIndex == 0)
                {
                    afterMatch = new Token[board.Width, board.Height];
                    for (int x = 0; x < board.Width; x++)
                    {
                        for (int y = 0; y < board.Height; y++)
                        {
                            afterMatch[x, y] = board.GetToken(x, y);
                        }
                    }
                }
            };

            var detonated = new List<(PowerKind kind, Vector2Int cell)>();
            int stepsWhenDetonated = -1;
            int runsWhenDetonated = -1;
            int beamsWhenDetonated = -1;
            // Anything wrong with the board at the moment the Star went off, as a message.
            string problem = null;
            var blasted = new List<Token>();
            board.PowerGemDetonated += (kind, cell, wave) =>
            {
                if (detonated.Count == 0)
                {
                    stepsWhenDetonated = clearSteps.Count;
                    runsWhenDetonated = clearedRuns.Count;
                    beamsWhenDetonated = Beams().Length;
                    for (int x = 0; x < board.Width; x++)
                    {
                        for (int y = 0; y < board.Height; y++)
                        {
                            Token before = afterMatch[x, y];
                            Token now = board.GetToken(x, y);
                            bool inTheBeam = x == starCell.x || y == starCell.y;
                            if (inTheBeam)
                            {
                                if (before != null)
                                {
                                    blasted.Add(before);
                                }
                                if (now != null)
                                {
                                    problem = $"({x}, {y}) is in the Star's row or column but still has a token.";
                                }
                            }
                            else if (now != before || now.transform.position != board.GridToWorld(x, y))
                            {
                                problem = $"({x}, {y}) is outside the Star's row and column but its token changed or moved.";
                            }
                        }
                    }
                }
                detonated.Add((kind, cell));
            };

            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            yield return WaitUntilIdle();

            Assert.That(detonated, Is.Not.Empty, "PowerGemDetonated never fired.");
            Assert.AreEqual((PowerKind.Star, starCell), detonated[0]);
            Assert.AreEqual(1, stepsWhenDetonated, "The blast belongs to the swap's own step, after the match was reported.");
            Assert.AreEqual(1, runsWhenDetonated, "Blasted gems aren't runs: only the run of 3 should have been reported.");
            Assert.AreEqual(1, beamsWhenDetonated, "The Star should fire one beam as it goes off.");
            Assert.IsNull(problem, "The beam should clear exactly the Star's row and column, before anything falls.");

            // Row 0 has 5 cells and column 0 has 4 more; 3 of those 9 were the match itself.
            Assert.AreEqual(6, blasted.Count);
            foreach (Token token in blasted)
            {
                Assert.IsTrue(token == null, "A token in the beam was taken off the board but not destroyed.");
            }
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();
            yield return WaitUntilNoBursts();

            // The beam cleans up after itself, like the bursts do.
            bool timedOut = false;
            yield return new WaitUntil(
                () => Beams().Length == 0,
                System.TimeSpan.FromSeconds(ResolveTimeoutSeconds),
                () => timedOut = true);
            Assert.IsFalse(timedOut, $"{Beams().Length} beams were still around after {ResolveTimeoutSeconds} seconds.");
        }

        // ---------------------------------------------------------------- Hypercubes
        //
        // The rules are covered in Edit Mode (BoardModelHypercubeTests). These check the running
        // Board: the token changes, a cube swap is accepted without any run, and the whole
        // colour is cleared before anything falls.

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator LineOfFive_TurnsTheSwappedTokenIntoAHypercube()
        {
            board.LoadLayoutForTests(BuildLayout(LineOfFiveLayout));
            // The A the player moves: it ends up at (2,0), the swapped cell inside the match.
            Token moved = board.GetToken(LineOfFiveB.x, LineOfFiveB.y);

            var created = new List<(PowerKind kind, Vector2Int cell)>();
            bool tokenWasTheCube = false;
            board.PowerGemCreated += (kind, cell) =>
            {
                // Only look at the first one: a cascade could, with luck, make another.
                if (created.Count == 0)
                {
                    Token token = board.GetToken(cell.x, cell.y);
                    // No colour any more, and no overlay: the cube's look is its own sprite.
                    tokenWasTheCube = token == moved && token.Power == PowerKind.Hypercube &&
                                      token.Type == null && !token.ShowsPowerOverlay;
                }
                created.Add((kind, cell));
            };

            Assert.IsTrue(board.TrySwap(LineOfFiveA, LineOfFiveB));
            // Stop as soon as the swap's own step has been reported, before any cascade.
            yield return new WaitUntil(() => clearSteps.Count > 0);

            Assert.AreEqual((5, 0), clearSteps[0], "The swap should make a run of 5.");
            Assert.That(created, Is.Not.Empty, "PowerGemCreated never fired.");
            Assert.AreEqual((PowerKind.Hypercube, LineOfFiveA), created[0], "The Hypercube belongs at the swapped cell.");
            Assert.IsTrue(tokenWasTheCube, "The token the player moved should itself become the Hypercube, with no colour.");
            // The cube is a survivor of the match: it's still on the board while the others pop.
            Assert.IsTrue(moved != null && moved.enabled, "The Hypercube's token was cleared along with the rest of the run.");

            yield return WaitUntilIdle();
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator HypercubeSwap_ClearsEveryTokenOfThatColourBeforeAnythingFalls()
        {
            // The top-left pair, which makes no match when swapped (see Layout). With the A at
            // (0,4) turned into a Hypercube, swapping it with the B beside it is a valid move
            // all the same, and should clear all six B's on the board.
            Vector2Int cubeStart = NoMatchA;
            Vector2Int gemStart = NoMatchB;
            board.SetPowerForTests(cubeStart, PowerKind.Hypercube);
            Token cube = board.GetToken(cubeStart.x, cubeStart.y);
            TokenType colour = board.GetToken(gemStart.x, gemStart.y).Type;

            // Which token is in which cell before the swap, and which of them are B's.
            var before = new Token[board.Width, board.Height];
            var targets = new List<Token>();
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    before[x, y] = board.GetToken(x, y);
                    if (before[x, y].Type == colour)
                    {
                        targets.Add(before[x, y]);
                    }
                }
            }
            Assert.AreEqual(6, targets.Count, "The layout should have six B's.");

            var detonated = new List<(PowerKind kind, Vector2Int cell)>();
            int stepsWhenDetonated = -1;
            int runsWhenDetonated = -1;
            int zapsWhenDetonated = -1;
            // Anything wrong with the board at the moment the cube went off, as a message.
            string problem = null;
            board.PowerGemDetonated += (kind, cell, wave) =>
            {
                if (detonated.Count == 0)
                {
                    stepsWhenDetonated = clearSteps.Count;
                    runsWhenDetonated = clearedRuns.Count;
                    zapsWhenDetonated = Zaps().Length;
                    for (int x = 0; x < board.Width; x++)
                    {
                        for (int y = 0; y < board.Height; y++)
                        {
                            var here = new Vector2Int(x, y);
                            Token now = board.GetToken(x, y);
                            // The two swapped cells held the cube and a B, so both are cleared
                            // whichever of the two ended up where.
                            bool cleared = here == cubeStart || here == gemStart || before[x, y].Type == colour;
                            if (cleared)
                            {
                                if (now != null)
                                {
                                    problem = $"({x}, {y}) held the cube or a B but still has a token.";
                                }
                            }
                            else if (now != before[x, y] || now.transform.position != board.GridToWorld(x, y))
                            {
                                problem = $"({x}, {y}) held another colour but its token changed or moved.";
                            }
                        }
                    }
                }
                detonated.Add((kind, cell));
            };

            Assert.IsTrue(board.TrySwap(cubeStart, gemStart));
            yield return WaitUntilIdle();

            Assert.AreEqual(new[] { "started" }, swapEvents, "A Hypercube swap is a valid move: it mustn't be rejected.");
            Assert.That(detonated, Is.Not.Empty, "PowerGemDetonated never fired.");
            // The swap is made first, so the cube goes off from the cell it was swapped into.
            Assert.AreEqual((PowerKind.Hypercube, gemStart), detonated[0]);
            Assert.AreEqual(0, stepsWhenDetonated, "The cube's own step isn't a match, so no clear step is reported for it.");
            Assert.AreEqual(0, runsWhenDetonated, "Zapped gems aren't runs: nothing should have been reported as one.");
            Assert.AreEqual(6, zapsWhenDetonated, "The cube should fire one bolt at each B.");
            Assert.IsNull(problem, "The cube should clear itself and exactly the B's, before anything falls.");

            // Whatever the refill lined up afterwards is a cascade, never "the swap's own match".
            foreach ((int longestRun, int cascadeIndex) step in clearSteps)
            {
                Assert.That(step.cascadeIndex, Is.GreaterThan(0), "Matches after a Hypercube goes off are cascades.");
            }

            Assert.IsTrue(cube == null, "The Hypercube was taken off the board but not destroyed.");
            foreach (Token token in targets)
            {
                Assert.IsTrue(token == null, "A zapped token was taken off the board but not destroyed.");
            }
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();
            yield return WaitUntilNoBursts();

            // The bolts clean up after themselves, like the bursts and the Star's beam do.
            bool timedOut = false;
            yield return new WaitUntil(
                () => Zaps().Length == 0,
                System.TimeSpan.FromSeconds(ResolveTimeoutSeconds),
                () => timedOut = true);
            Assert.IsFalse(timedOut, $"{Zaps().Length} bolts were still around after {ResolveTimeoutSeconds} seconds.");
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator SwappingTwoHypercubes_PutsBothBack()
        {
            board.SetPowerForTests(NoMatchA, PowerKind.Hypercube);
            board.SetPowerForTests(NoMatchB, PowerKind.Hypercube);
            Token cubeA = board.GetToken(NoMatchA.x, NoMatchA.y);
            Token cubeB = board.GetToken(NoMatchB.x, NoMatchB.y);
            int detonations = 0;
            board.PowerGemDetonated += (kind, cell, wave) => detonations++;

            Assert.IsTrue(board.TrySwap(NoMatchA, NoMatchB));
            yield return WaitUntilIdle();

            Assert.AreEqual(new[] { "started", "rejected" }, swapEvents, "Two Hypercubes can't be swapped (yet).");
            Assert.AreEqual(0, detonations);
            Assert.AreSame(cubeA, board.GetToken(NoMatchA.x, NoMatchA.y));
            Assert.AreSame(cubeB, board.GetToken(NoMatchB.x, NoMatchB.y));
            Assert.AreEqual(PowerKind.Hypercube, cubeA.Power);
            Assert.AreEqual(PowerKind.Hypercube, cubeB.Power);
            AssertBoardIsFullAndSettled();
        }

        // ---------------------------------------------------------------- Chain reactions
        //
        // The rules are covered in Edit Mode (BoardModelChainTests). This checks the running
        // Board: the waves are played one after another, a caught gem waits on the board for
        // its turn, and nothing falls until the last wave is over.

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator FlameCaughtInABlast_GoesOffInItsOwnWave_BeforeAnythingFalls()
        {
            // The A at (0,0) is in the run of 3 that MatchA/MatchB make. As a Flame in the
            // corner, its blast reaches (0,1) and (1,1). With (1,1) a Flame too, that one goes
            // off next, and its 3x3 takes (0,2), (1,2), (2,1) and (2,2).
            var firstCell = new Vector2Int(0, 0);
            var secondCell = new Vector2Int(1, 1);
            board.SetPowerForTests(firstCell, PowerKind.Flame);
            board.SetPowerForTests(secondCell, PowerKind.Flame);
            Token second = board.GetToken(1, 1);
            Token blastedByFirst = board.GetToken(0, 1);
            // In the second Flame's blast but not the first's.
            Token blastedBySecond = board.GetToken(1, 2);
            // Sits above both blasts. It will fall, but not until the whole chain is over.
            Token above = board.GetToken(0, 3);

            var detonated = new List<(PowerKind kind, Vector2Int cell, int wave)>();
            int stepsWhenSecondWentOff = -1;
            // Anything wrong with the board at the moment either Flame went off, as a message.
            string problem = null;
            board.PowerGemDetonated += (kind, cell, wave) =>
            {
                // Only the chain itself is checked: whatever a later cascade happens to set off
                // is still recorded, but it's a different board by then.
                if (detonated.Count == 0)
                {
                    if (board.GetToken(0, 1) != null)
                    {
                        problem = "The first blast's token at (0, 1) should be off the board once the Flame goes off.";
                    }
                    if (board.GetToken(1, 1) != second || !second.ShowsPowerOverlay)
                    {
                        problem = "The caught Flame should stay on the board, still a Flame, until its own wave.";
                    }
                    if (board.GetToken(1, 2) != blastedBySecond)
                    {
                        problem = "The second wave's tokens shouldn't be cleared during the first.";
                    }
                }
                else if (detonated.Count == 1)
                {
                    stepsWhenSecondWentOff = clearSteps.Count;
                    if (board.GetToken(1, 1) != null)
                    {
                        problem = "The second Flame's own token should go as it goes off.";
                    }
                    if (board.GetToken(0, 2) != null || board.GetToken(1, 2) != null ||
                        board.GetToken(2, 1) != null || board.GetToken(2, 2) != null)
                    {
                        problem = "Everything in the second Flame's 3x3 should be off the board once it goes off.";
                    }
                }
                if (detonated.Count < 2 &&
                    (board.GetToken(0, 3) != above || above.transform.position != board.GridToWorld(0, 3)))
                {
                    problem = $"Something fell before wave {wave} went off.";
                }
                detonated.Add((kind, cell, wave));
            };

            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            yield return WaitUntilIdle();

            Assert.That(detonated.Count, Is.GreaterThanOrEqualTo(2), "Both Flames should have gone off.");
            Assert.AreEqual((PowerKind.Flame, firstCell, 0), detonated[0], "The matched Flame is wave 0.");
            Assert.AreEqual((PowerKind.Flame, secondCell, 1), detonated[1], "The Flame it caught is wave 1.");
            Assert.IsNull(problem);
            Assert.AreEqual(1, stepsWhenSecondWentOff, "The whole chain belongs to the swap's own clear step.");

            Assert.IsTrue(second == null, "The second Flame went off but its token wasn't destroyed.");
            Assert.IsTrue(blastedByFirst == null, "The token at (0, 1) was blasted but not destroyed.");
            Assert.IsTrue(blastedBySecond == null, "The token at (1, 2) was blasted but not destroyed.");
            AssertBoardIsFullAndSettled();
            AssertNoMatchesLeft();
            yield return WaitUntilNoBursts();
        }

        // ---------------------------------------------------------------- Detonation hold
        //
        // The GameManager holds the round clock while Board.IsDetonating is true, so these
        // check that it covers exactly the detonations: every wave, and nothing falling.

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator OrdinaryMatch_NeverSetsIsDetonating()
        {
            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            bool everDetonating = false;
            // Checked every frame, the way the GameManager asks.
            while (board.IsResolving)
            {
                everDetonating |= board.IsDetonating;
                yield return null;
            }

            Assert.IsFalse(everDetonating, "A match with no power gem shouldn't hold the clock.");
            yield return WaitUntilNoBursts();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator Chain_IsDetonatingForEveryWave_AndNothingFallsMeanwhile()
        {
            // The same two-Flame chain as FlameCaughtInABlast_GoesOffInItsOwnWave_BeforeAnythingFalls.
            board.SetPowerForTests(new Vector2Int(0, 0), PowerKind.Flame);
            board.SetPowerForTests(new Vector2Int(1, 1), PowerKind.Flame);
            // Sits above both blasts. It falls once the chain is over.
            Token above = board.GetToken(0, 3);
            Vector3 aboveHome = board.GridToWorld(0, 3);

            var detonatingWhenWentOff = new List<bool>();
            board.PowerGemDetonated += (kind, cell, wave) => detonatingWhenWentOff.Add(board.IsDetonating);

            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            bool sawDetonating = false;
            bool fellWhileDetonating = false;
            while (board.IsResolving)
            {
                if (board.IsDetonating)
                {
                    sawDetonating = true;
                    fellWhileDetonating |= above != null && above.transform.position != aboveHome;
                }
                yield return null;
            }

            Assert.IsTrue(sawDetonating, "IsDetonating should be true while the Flames go off.");
            Assert.That(detonatingWhenWentOff.Count, Is.GreaterThanOrEqualTo(2), "Both Flames should have gone off.");
            Assert.That(detonatingWhenWentOff, Is.All.True, "IsDetonating should already be true as each gem goes off.");
            Assert.IsFalse(fellWhileDetonating, "Nothing should fall while IsDetonating is true: the clock runs again as tokens fall.");
            Assert.IsFalse(board.IsDetonating, "IsDetonating should be false again once the board is idle.");
            yield return WaitUntilNoBursts();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator HypercubeSwap_IsDetonatingWhileItGoesOff()
        {
            board.SetPowerForTests(NoMatchA, PowerKind.Hypercube);
            var detonatingWhenWentOff = new List<bool>();
            board.PowerGemDetonated += (kind, cell, wave) => detonatingWhenWentOff.Add(board.IsDetonating);

            Assert.IsTrue(board.TrySwap(NoMatchA, NoMatchB));
            yield return WaitUntilIdle();

            Assert.That(detonatingWhenWentOff, Is.Not.Empty, "The Hypercube should have gone off.");
            Assert.That(detonatingWhenWentOff, Is.All.True);
            Assert.IsFalse(board.IsDetonating);
            yield return WaitUntilNoBursts();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator PausingDuringADetonation_KeepsItHeld_UntilResumedAndFinished()
        {
            board.SetPowerForTests(new Vector2Int(0, 0), PowerKind.Flame);
            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            bool timedOut = false;
            yield return new WaitUntil(() => board.IsDetonating,
                System.TimeSpan.FromSeconds(ResolveTimeoutSeconds), () => timedOut = true);
            Assert.IsFalse(timedOut, "The Flame never started going off.");

            // Pause the way the GameManager does (see Pausing_FreezesThePopAndTheBursts).
            Time.timeScale = 0f;
            yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(board.IsDetonating, "A pause shouldn't end the detonation hold.");

            Time.timeScale = 1f;
            yield return WaitUntilIdle();
            Assert.IsFalse(board.IsDetonating, "The hold should end once the detonation finishes after the resume.");
            AssertBoardIsFullAndSettled();
            yield return WaitUntilNoBursts();
        }

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator Regenerate_DuringADetonation_ClearsIsDetonating()
        {
            board.SetPowerForTests(new Vector2Int(0, 0), PowerKind.Flame);
            Assert.IsTrue(board.TrySwap(MatchA, MatchB));
            yield return new WaitUntil(() => board.IsDetonating);

            // A new round deals a fresh board, cancelling the turn in progress.
            board.Regenerate();

            Assert.IsFalse(board.IsDetonating, "A cancelled detonation would otherwise hold the next round's clock forever.");
            yield return WaitUntilNoBursts();
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
            // On this layout the hint is (1,1) and (2,1): the valid move in the leftmost column.
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

        /// <summary>Every Star beam under the board right now.</summary>
        private StarBeam[] Beams()
        {
            return boardObject.GetComponentsInChildren<StarBeam>();
        }

        /// <summary>Every Hypercube bolt under the board right now.</summary>
        private HypercubeZap[] Zaps()
        {
            return boardObject.GetComponentsInChildren<HypercubeZap>();
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
