using System;
using System.Collections;
using System.Collections.Generic;
using Match3.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Tests
{
    /// <summary>
    /// Play Mode tests for <see cref="OnlineLeaderboardClient"/>: registering a round when it
    /// starts, posting its score when it ends, loading the board, and carrying on calmly when
    /// the server can't be reached. A fake <see cref="ILeaderboardApi"/> stands in for the
    /// server and a pair of lambdas for the signed-in account, so there's no network and no
    /// real sign-in. Play Mode, because the client works in coroutines, which need real frames.
    /// </summary>
    public class OnlineLeaderboardClientPlayModeTests
    {
        private const int MaxFrames = 100;
        private const string AccessToken = "access-token";

        private GameObject gameObject;
        private OnlineLeaderboardClient client;
        private FakeLeaderboardApi api;
        private bool signedIn;
        private string accessToken;

        [SetUp]
        public void SetUp()
        {
            api = new FakeLeaderboardApi();
            signedIn = true;
            accessToken = AccessToken;
        }

        [TearDown]
        public void TearDown()
        {
            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        private IEnumerator CreateClient()
        {
            gameObject = new GameObject("OnlineLeaderboardClient (test)");
            // Inactive, so Awake waits until the fakes are plugged in.
            gameObject.SetActive(false);
            client = gameObject.AddComponent<OnlineLeaderboardClient>();
            client.ConfigureForTests(api, () => signedIn, GetAccessToken);
            gameObject.SetActive(true);
            yield return null;
        }

        // Stands in for AccountManager.GetAccessToken: null means "couldn't get one".
        private IEnumerator GetAccessToken(Action<string> done)
        {
            yield return null;
            done(signedIn ? accessToken : null);
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            for (int frame = 0; frame < MaxFrames && !condition(); frame++)
            {
                yield return null;
            }
            Assert.That(condition(), Is.True, "Timed out waiting.");
        }

        private IEnumerator PlayRound(int score)
        {
            client.RoundStarted();
            yield return WaitFor(() => api.Calls.Contains("StartRound"));
            // A few frames for the reply to arrive, as if the round were being played.
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }
            client.RoundEnded(score);
            yield return WaitFor(() => client.LastPost != OnlineLeaderboardClient.PostState.Posting);
        }

        [UnityTest]
        public IEnumerator SignedIn_PostsTheRoundWithItsToken()
        {
            yield return CreateClient();
            api.Standing = new Standing { best = 90000, rank = 4 };

            yield return PlayRound(90000);

            Assert.That(api.SubmittedToken, Is.EqualTo("round-1"));
            Assert.That(api.SubmittedScore, Is.EqualTo(90000));
            Assert.That(api.AccessTokens, Has.All.EqualTo(AccessToken));
            Assert.That(client.LastPost, Is.EqualTo(OnlineLeaderboardClient.PostState.Posted));
            Assert.That(client.LastStanding.rank, Is.EqualTo(4));
            Assert.That(client.PostMessage, Does.Contain("90,000").And.Contain("#4"));
        }

        [UnityTest]
        public IEnumerator SignedOut_NeverCallsTheServer()
        {
            signedIn = false;
            yield return CreateClient();

            client.RoundStarted();
            yield return null;
            client.RoundEnded(5000);
            yield return null;

            Assert.That(api.Calls, Is.Empty);
            Assert.That(client.LastPost, Is.EqualTo(OnlineLeaderboardClient.PostState.NotSignedIn));
        }

        [UnityTest]
        public IEnumerator ServerDownWhenTheRoundStarts_FailsWithAMessage()
        {
            yield return CreateClient();
            api.RoundResult = ApiResult<string>.Failure("Cannot connect to destination host");

            yield return PlayRound(5000);

            Assert.That(api.Calls, Does.Not.Contain("SubmitScore"));
            Assert.That(client.LastPost, Is.EqualTo(OnlineLeaderboardClient.PostState.Failed));
            Assert.That(client.PostMessage, Does.Contain("wasn't posted"));
        }

        [UnityTest]
        public IEnumerator RateLimitedWhenTheRoundStarts_SaysSo()
        {
            yield return CreateClient();
            api.RoundResult = ApiResult<string>.Failure("rate_limited", "rate_limited", reached: true);

            yield return PlayRound(5000);

            Assert.That(api.Calls, Does.Not.Contain("SubmitScore"));
            Assert.That(client.LastPost, Is.EqualTo(OnlineLeaderboardClient.PostState.Failed));
            Assert.That(client.PostMessage, Does.Contain("Too many rounds"));
        }

        [UnityTest]
        public IEnumerator NoAccessTokenWhenTheRoundStarts_BlamesTheAccountServer()
        {
            accessToken = null;
            yield return CreateClient();

            client.RoundStarted();
            yield return null;
            yield return null;
            client.RoundEnded(5000);
            yield return WaitFor(() => client.LastPost != OnlineLeaderboardClient.PostState.Posting);

            Assert.That(api.Calls, Is.Empty);
            Assert.That(client.PostMessage, Does.Contain("account server"));
        }

        [UnityTest]
        public IEnumerator NoAccessToken_FailsWithAMessage()
        {
            yield return CreateClient();
            client.RoundStarted();
            yield return WaitFor(() => api.Calls.Contains("StartRound"));
            yield return null;

            // The account server became unreachable mid-round: no token for the post.
            accessToken = null;
            client.RoundEnded(5000);
            yield return WaitFor(() => client.LastPost != OnlineLeaderboardClient.PostState.Posting);

            Assert.That(api.Calls, Does.Not.Contain("SubmitScore"));
            Assert.That(client.LastPost, Is.EqualTo(OnlineLeaderboardClient.PostState.Failed));
            Assert.That(client.PostMessage, Does.Contain("account server"));
        }

        [UnityTest]
        public IEnumerator ScoreTurnedDown_FailsWithAMessage()
        {
            yield return CreateClient();
            api.ScoreError = ApiResult<Standing>.Failure("rate_limited", "rate_limited", reached: true);

            yield return PlayRound(5000);

            Assert.That(client.LastPost, Is.EqualTo(OnlineLeaderboardClient.PostState.Failed));
            Assert.That(client.PostMessage, Does.Contain("Too many rounds"));
        }

        [UnityTest]
        public IEnumerator EachRoundUsesItsOwnToken()
        {
            yield return CreateClient();

            yield return PlayRound(1000);
            yield return PlayRound(2000);

            Assert.That(api.SubmittedToken, Is.EqualTo("round-2"));
            Assert.That(api.SubmittedScore, Is.EqualTo(2000));
        }

        [UnityTest]
        public IEnumerator RoundStarted_ClearsTheLastRoundsResult()
        {
            yield return CreateClient();
            yield return PlayRound(1000);

            client.RoundStarted();

            Assert.That(client.LastPost, Is.EqualTo(OnlineLeaderboardClient.PostState.None));
            Assert.That(client.PostMessage, Is.Null);
            Assert.That(client.LastStanding, Is.Null);
        }

        [UnityTest]
        public IEnumerator RefreshBoard_LoadsTheTopAndThePlayer()
        {
            yield return CreateClient();
            api.Top = new[] { new GlobalEntry { rank = 1, tag = "ADA", score = 150000 } };
            api.Me = new PlayerProfile { tag = "ADA", best = 150000, rank = 1 };

            client.RefreshBoard();
            yield return WaitFor(() => !client.Loading);

            Assert.That(client.Top, Has.Length.EqualTo(1));
            Assert.That(client.Me.tag, Is.EqualTo("ADA"));
            Assert.That(client.BoardMessage, Is.Null);
        }

        [UnityTest]
        public IEnumerator RefreshBoard_SignedOut_LoadsOnlyTheTop()
        {
            signedIn = false;
            yield return CreateClient();

            client.RefreshBoard();
            yield return WaitFor(() => !client.Loading);

            Assert.That(client.Top, Is.Not.Null);
            Assert.That(client.Me, Is.Null);
            Assert.That(api.Calls, Does.Not.Contain("GetMe"));
        }

        [UnityTest]
        public IEnumerator RefreshBoard_ServerDown_SaysSo()
        {
            yield return CreateClient();
            api.TopError = ApiResult<GlobalEntry[]>.Failure("HTTP 403");

            client.RefreshBoard();
            yield return WaitFor(() => !client.Loading);

            Assert.That(client.Top, Is.Null);
            Assert.That(client.BoardMessage, Does.Contain("Couldn't reach"));
        }

        /// <summary>
        /// A pretend leaderboard server. Each request takes a frame, like a (very fast) network,
        /// and is recorded in <see cref="Calls"/>. Set the public fields to choose the answers.
        /// </summary>
        private sealed class FakeLeaderboardApi : ILeaderboardApi
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> AccessTokens = new List<string>();

            // null = succeed (with a new round token each time).
            public ApiResult<string> RoundResult;
            public Standing Standing = new Standing { best = 1, rank = 1 };
            public ApiResult<Standing> ScoreError;
            public GlobalEntry[] Top = new GlobalEntry[0];
            public ApiResult<GlobalEntry[]> TopError;
            public PlayerProfile Me = new PlayerProfile { tag = "ADA" };

            public string SubmittedToken;
            public int SubmittedScore;

            private int rounds;

            public IEnumerator StartRound(string accessToken, Action<ApiResult<string>> done)
            {
                Calls.Add("StartRound");
                AccessTokens.Add(accessToken);
                yield return null;
                rounds++;
                done(RoundResult ?? ApiResult<string>.Success("round-" + rounds));
            }

            public IEnumerator SubmitScore(string accessToken, string roundToken, int score, Action<ApiResult<Standing>> done)
            {
                Calls.Add("SubmitScore");
                AccessTokens.Add(accessToken);
                SubmittedToken = roundToken;
                SubmittedScore = score;
                yield return null;
                done(ScoreError ?? ApiResult<Standing>.Success(Standing));
            }

            public IEnumerator GetTop(int limit, Action<ApiResult<GlobalEntry[]>> done)
            {
                Calls.Add("GetTop");
                yield return null;
                done(TopError ?? ApiResult<GlobalEntry[]>.Success(Top));
            }

            public IEnumerator GetMe(string accessToken, Action<ApiResult<PlayerProfile>> done)
            {
                Calls.Add("GetMe");
                AccessTokens.Add(accessToken);
                yield return null;
                done(ApiResult<PlayerProfile>.Success(Me));
            }
        }
    }

    /// <summary>
    /// Which line of the global board is the player's (<see cref="LeaderboardMenu.MyRow"/>).
    /// Plain tests, no frames needed; they live here because only the Play Mode tests can see
    /// the game's internal members.
    /// </summary>
    public class LeaderboardMenuMyRowTests
    {
        private static GlobalEntry Entry(int rank, string tag, int score) =>
            new GlobalEntry { rank = rank, tag = tag, score = score };

        private static readonly PlayerProfile Me = new PlayerProfile { tag = "CAR", best = 80000, rank = 2 };

        [Test]
        public void FindsThePlayersLine()
        {
            GlobalEntry[] board = { Entry(1, "ADA", 90000), Entry(2, "CAR", 80000), Entry(3, "CAR", 70000) };

            Assert.That(LeaderboardMenu.MyRow(board, Me), Is.EqualTo(1));
        }

        [Test]
        public void TwoMatchingLines_HighlightsNeither()
        {
            // Another "CAR" with the very same best: same rank too, so we can't tell them apart.
            GlobalEntry[] board = { Entry(1, "ADA", 90000), Entry(2, "CAR", 80000), Entry(2, "CAR", 80000) };

            Assert.That(LeaderboardMenu.MyRow(board, Me), Is.EqualTo(-1));
        }

        [Test]
        public void NotOnTheBoardOrNoScore_IsNone()
        {
            GlobalEntry[] board = { Entry(1, "ADA", 90000) };

            Assert.That(LeaderboardMenu.MyRow(board, Me), Is.EqualTo(-1));
            Assert.That(LeaderboardMenu.MyRow(board, new PlayerProfile { tag = "ADA" }), Is.EqualTo(-1));
            Assert.That(LeaderboardMenu.MyRow(null, Me), Is.EqualTo(-1));
            Assert.That(LeaderboardMenu.MyRow(board, null), Is.EqualTo(-1));
        }
    }
}
