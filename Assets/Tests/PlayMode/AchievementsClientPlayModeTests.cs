using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Match3.Achievements;
using Match3.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Tests
{
    /// <summary>
    /// Play Mode tests for <see cref="AchievementsClient"/>: loading the list for a guest and
    /// for a signed-in player, announcing unlocks (once each), and carrying on when the server
    /// can't be reached. A fake <see cref="IAchievementsApi"/> stands in for the server and a
    /// pair of lambdas for the account, as in the leaderboard client's tests.
    /// </summary>
    public class AchievementsClientPlayModeTests
    {
        private const int MaxFrames = 100;
        private const string AccessToken = "access-token";

        private GameObject gameObject;
        private AchievementsClient client;
        private FakeAchievementsApi api;
        private bool signedIn;
        private string accessToken;
        // The key of every achievement announced through Unlocked, in order.
        private List<string> announced;

        [SetUp]
        public void SetUp()
        {
            api = new FakeAchievementsApi();
            signedIn = true;
            accessToken = AccessToken;
            announced = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        /// <summary>Creates the client and waits for the list it loads when it starts.</summary>
        private IEnumerator CreateClient()
        {
            gameObject = new GameObject("AchievementsClient (test)");
            // Inactive, so Awake waits until the fakes are plugged in.
            gameObject.SetActive(false);
            client = gameObject.AddComponent<AchievementsClient>();
            client.ConfigureForTests(api, () => signedIn, GetAccessToken);
            client.Unlocked += achievement => announced.Add(achievement.key);
            gameObject.SetActive(true);
            yield return null;
            yield return WaitFor(() => !client.Loading);
        }

        // Stands in for AccountManager.GetAccessToken: null means "couldn't get one".
        private IEnumerator GetAccessToken(Action<string> done)
        {
            yield return null;
            done(accessToken);
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            for (int frame = 0; frame < MaxFrames && !condition(); frame++)
            {
                yield return null;
            }
            Assert.That(condition(), Is.True, "Timed out waiting.");
        }

        private IEnumerator WaitForLoads(int count)
        {
            yield return WaitFor(() => api.Calls.Count >= count && !client.Loading);
        }

        private static Achievement Locked(string key) => new Achievement { key = key, name = key, goal = 1 };

        private static Achievement Unlocked(string key) =>
            new Achievement { key = key, name = key, goal = 1, progress = 1, unlockedAt = "2026-10-10T04:15:00Z" };

        [UnityTest]
        public IEnumerator SignedIn_LoadsThePlayersList()
        {
            api.Mine = new[] { Unlocked("a"), Locked("b") };

            yield return CreateClient();

            Assert.That(api.Calls, Is.EqualTo(new[] { "GetMine" }));
            Assert.That(api.AccessTokens, Has.All.EqualTo(AccessToken));
            Assert.That(client.Items, Has.Length.EqualTo(2));
            Assert.That(client.Items[0].IsUnlocked, Is.True);
            Assert.That(client.Message, Is.Null);
            Assert.That(announced, Is.Empty, "Loading the list isn't unlocking anything.");
        }

        [UnityTest]
        public IEnumerator Guest_LoadsThePublicCatalog()
        {
            signedIn = false;
            api.Catalog = new[] { Locked("a"), Locked("b") };

            yield return CreateClient();

            Assert.That(api.Calls, Is.EqualTo(new[] { "GetCatalog" }));
            Assert.That(client.Items, Has.Length.EqualTo(2));
            Assert.That(client.IsSignedIn, Is.False);
        }

        [UnityTest]
        public IEnumerator ScorePosted_AnnouncesWhatTheServerSaysWasUnlocked_AndReloads()
        {
            api.Mine = new[] { Locked("a"), Locked("b") };
            yield return CreateClient();

            api.Mine = new[] { Unlocked("a"), Locked("b") };
            client.OnScorePosted(new[] { Unlocked("a") });
            yield return WaitForLoads(2);

            Assert.That(announced, Is.EqualTo(new[] { "a" }));
            Assert.That(client.Items[0].IsUnlocked, Is.True, "The list should have been loaded again.");
        }

        [UnityTest]
        public IEnumerator ScorePosted_NothingUnlocked_AnnouncesNothing()
        {
            api.Mine = new[] { Locked("a") };
            yield return CreateClient();

            client.OnScorePosted(new Achievement[0]);
            yield return WaitForLoads(2);

            Assert.That(announced, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ScorePosted_ReplyLost_AnnouncesTheDifferenceFromTheOldList()
        {
            api.Mine = new[] { Unlocked("a"), Locked("b"), Locked("c") };
            yield return CreateClient();

            // null: the score was posted, but we never heard what it unlocked.
            api.Mine = new[] { Unlocked("a"), Unlocked("b"), Locked("c") };
            client.OnScorePosted(null);
            yield return WaitForLoads(2);

            Assert.That(announced, Is.EqualTo(new[] { "b" }));
        }

        [UnityTest]
        public IEnumerator AnAchievementIsNeverAnnouncedTwice()
        {
            api.Mine = new[] { Locked("a") };
            yield return CreateClient();

            api.Mine = new[] { Unlocked("a") };
            client.OnScorePosted(new[] { Unlocked("a") });
            yield return WaitForLoads(2);
            client.OnScorePosted(new[] { Unlocked("a") });
            yield return WaitForLoads(3);

            Assert.That(announced, Is.EqualTo(new[] { "a" }));
        }

        [UnityTest]
        public IEnumerator SigningIn_SwapsTheCatalogForThePlayersList_WithoutAnnouncing()
        {
            signedIn = false;
            api.Catalog = new[] { Locked("a") };
            yield return CreateClient();

            signedIn = true;
            api.Mine = new[] { Unlocked("a") };
            client.OnAccountChanged();
            yield return WaitForLoads(2);

            Assert.That(api.Calls, Is.EqualTo(new[] { "GetCatalog", "GetMine" }));
            Assert.That(client.Items[0].IsUnlocked, Is.True);
            Assert.That(announced, Is.Empty, "Earned before now, so it's not news.");
        }

        [UnityTest]
        public IEnumerator AccountChanged_WithoutSigningInOrOut_DoesNothing()
        {
            yield return CreateClient();

            client.OnAccountChanged();
            yield return null;
            yield return null;

            Assert.That(api.Calls, Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ServerDown_SaysSoAndKeepsGoing()
        {
            api.Error = ApiResult<Achievement[]>.Failure("Cannot connect to destination host");
            // The client is supposed to log why, as a warning (an error would fail the test by
            // itself). Expecting it also checks that it does.
            LogAssert.Expect(LogType.Warning, new Regex("couldn't load the list"));

            yield return CreateClient();

            Assert.That(client.Items, Is.Null);
            Assert.That(client.Message, Does.Contain("Couldn't reach"));
        }

        [UnityTest]
        public IEnumerator NoAccessToken_BlamesTheAccountServer()
        {
            accessToken = null;

            yield return CreateClient();

            Assert.That(api.Calls, Is.Empty, "Without a token there's nothing to ask the server with.");
            Assert.That(client.Message, Does.Contain("account server"));
        }

        /// <summary>
        /// A pretend achievements server. Each request takes a frame and is recorded in
        /// <see cref="Calls"/>. Set the public fields to choose the answers.
        /// </summary>
        private sealed class FakeAchievementsApi : IAchievementsApi
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> AccessTokens = new List<string>();

            public Achievement[] Catalog = new Achievement[0];
            public Achievement[] Mine = new Achievement[0];
            // If set, every request fails with this.
            public ApiResult<Achievement[]> Error;

            public IEnumerator GetCatalog(Action<ApiResult<Achievement[]>> done)
            {
                Calls.Add("GetCatalog");
                yield return null;
                done(Error ?? ApiResult<Achievement[]>.Success(Catalog));
            }

            public IEnumerator GetMine(string accessToken, Action<ApiResult<Achievement[]>> done)
            {
                Calls.Add("GetMine");
                AccessTokens.Add(accessToken);
                yield return null;
                done(Error ?? ApiResult<Achievement[]>.Success(Mine));
            }
        }
    }
}
