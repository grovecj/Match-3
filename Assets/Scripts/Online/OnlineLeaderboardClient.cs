using System;
using System.Collections;
using System.Globalization;
using Match3.Accounts;
using UnityEngine;

namespace Match3.Online
{
    /// <summary>
    /// The game's side of the global leaderboard. It does two jobs:
    ///
    /// <b>Posting rounds</b> (signed-in players only). When a round starts,
    /// <see cref="RoundStarted"/> asks the server for a round token; when it ends,
    /// <see cref="RoundEnded"/> posts the score with that token. The server only accepts a
    /// score for a round it saw start about a minute earlier, which is its basic protection
    /// against made-up scores. How it went is in <see cref="LastPost"/> and
    /// <see cref="PostMessage"/>.
    ///
    /// <b>Showing the board.</b> <see cref="RefreshBoard"/> loads the top players
    /// (<see cref="Top"/>, public, so guests see it too) and, when signed in, the player's own
    /// standing (<see cref="Me"/>).
    ///
    /// Everything happens in coroutines, in the background, and nothing here ever stops play:
    /// if the server or the accounts server can't be reached, it says so in a message and the
    /// game carries on. Every change raises <see cref="Changed"/>, which is how the Leaderboard
    /// panel keeps up.
    /// </summary>
    public class OnlineLeaderboardClient : MonoBehaviour
    {
        private const string NoAccessTokenMessage = "Couldn't reach the account server, so this round wasn't posted.";

        public enum PostState
        {
            /// <summary>No round has ended since the last one started.</summary>
            None,
            /// <summary>The round ended while signed out, so it's local only.</summary>
            NotSignedIn,
            Posting,
            Posted,
            Failed,
        }

        [Tooltip("Who's signed in; the source of the access token for posting.")]
        [SerializeField] private AccountManager accountManager;
        [Tooltip("The leaderboard server. Only change it to test against a local server, and don't commit that.")]
        [SerializeField] private string baseUrl = HttpLeaderboardApi.DefaultBaseUrl;

        /// <summary>Raised whenever anything below changes.</summary>
        public event Action Changed;

        public PostState LastPost { get; private set; }

        /// <summary>After a successful post, the player's best and rank; otherwise null.</summary>
        public Standing LastStanding { get; private set; }

        /// <summary>A line about the last round's post for the player (e.g. "Posting your score..."), or null.</summary>
        public string PostMessage { get; private set; }

        /// <summary>The top players from the last <see cref="RefreshBoard"/>, or null if they couldn't be loaded.</summary>
        public GlobalEntry[] Top { get; private set; }

        /// <summary>The signed-in player's standing from the last <see cref="RefreshBoard"/>, or null.</summary>
        public PlayerProfile Me { get; private set; }

        /// <summary>True while <see cref="RefreshBoard"/> is waiting for the server.</summary>
        public bool Loading { get; private set; }

        /// <summary>Why the board couldn't be loaded, or null.</summary>
        public string BoardMessage { get; private set; }

        public bool IsSignedIn => isSignedIn != null && isSignedIn();

        private ILeaderboardApi api;
        // How we ask about the account. Normally the AccountManager's; tests swap in their own
        // (see ConfigureForTests), so they don't need a real sign-in.
        private Func<bool> isSignedIn;
        private Func<Action<string>, IEnumerator> getAccessToken;

        // Counts rounds. Each request remembers which round it was for, and an answer that
        // arrives after the next round has started is ignored instead of mixing the two up.
        private int round;
        private bool roundStarting;
        private string roundToken;
        // Why this round couldn't be registered, to tell the player when it ends; or null.
        private string startFailure;
        // The same idea for board loads: only the latest one's answer is shown.
        private int boardLoad;

        private void Awake()
        {
            if (api != null)
            {
                return; // Already set up by ConfigureForTests.
            }
            if (accountManager == null)
            {
                Debug.LogError("OnlineLeaderboardClient: assign the AccountManager in the Inspector.", this);
                enabled = false;
                return;
            }
            api = new HttpLeaderboardApi(baseUrl);
            isSignedIn = () => accountManager.State == AccountManager.AccountState.SignedIn;
            // A method name without "()" is a delegate: a reference to the method itself, to
            // call later. GetAccessToken's shape (takes a callback, returns a coroutine) matches.
            getAccessToken = accountManager.GetAccessToken;
        }

        /// <summary>
        /// Call when a round starts. Forgets the last round's result and, if the player is
        /// signed in, asks the server for this round's token.
        /// </summary>
        public void RoundStarted()
        {
            round++;
            roundToken = null;
            roundStarting = false;
            startFailure = null;
            LastPost = PostState.None;
            LastStanding = null;
            PostMessage = null;

            if (enabled && IsSignedIn)
            {
                StartCoroutine(StartRound(round));
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Call when a round ends with its final score. Posts it in the background if the player
        /// is signed in.
        /// </summary>
        public void RoundEnded(int score)
        {
            if (!enabled)
            {
                return;
            }
            if (!IsSignedIn)
            {
                LastPost = PostState.NotSignedIn;
                PostMessage = null;
                Changed?.Invoke();
                return;
            }
            StartCoroutine(PostScore(round, score));
        }

        /// <summary>Loads the top players (and, when signed in, the player's own standing).</summary>
        public void RefreshBoard()
        {
            if (enabled)
            {
                StartCoroutine(LoadBoard(++boardLoad));
            }
        }

        private IEnumerator StartRound(int thisRound)
        {
            roundStarting = true;

            string token = null;
            yield return getAccessToken(t => token = t);
            if (thisRound != round)
            {
                yield break; // A newer round has taken over (and reset roundStarting).
            }

            ApiResult<string> result = null;
            if (token != null)
            {
                yield return api.StartRound(token, r => result = r);
                if (thisRound != round)
                {
                    yield break;
                }
            }

            roundStarting = false;
            if (result != null && result.Succeeded)
            {
                roundToken = result.Value;
            }
            else
            {
                // Not worth interrupting the round for. Keep the reason; PostScore tells the
                // player when the round ends.
                Debug.LogWarning($"Global leaderboard: couldn't start a round ({(token == null ? "no access token" : result?.Error)}).");
                startFailure = token == null ? NoAccessTokenMessage : MessageFor(result);
            }
        }

        private IEnumerator PostScore(int thisRound, int score)
        {
            LastPost = PostState.Posting;
            PostMessage = "Posting your score...";
            Changed?.Invoke();

            // If the round's token is still on its way (a very slow connection), wait for it.
            while (roundStarting && thisRound == round)
            {
                yield return null;
            }
            if (thisRound != round)
            {
                yield break;
            }
            if (roundToken == null)
            {
                Fail(startFailure ?? "Couldn't reach the global leaderboard when this round started, so it wasn't posted.");
                yield break;
            }

            string token = null;
            yield return getAccessToken(t => token = t);
            if (thisRound != round)
            {
                yield break;
            }
            if (token == null)
            {
                Fail(NoAccessTokenMessage);
                yield break;
            }

            ApiResult<Standing> result = null;
            yield return api.SubmitScore(token, roundToken, score, r => result = r);
            if (thisRound != round)
            {
                yield break;
            }
            roundToken = null; // Each token is good for one score.

            if (result.Succeeded)
            {
                LastPost = PostState.Posted;
                LastStanding = result.Value;
                PostMessage = $"Posted! Your best: {Format(result.Value.best)}, #{result.Value.rank}";
                Changed?.Invoke();
                // The board has (maybe) just changed, so load it again for when it's next shown.
                RefreshBoard();
            }
            else
            {
                Debug.LogWarning($"Global leaderboard: score not posted ({result.Error}).");
                Fail(MessageFor(result));
            }
        }

        private IEnumerator LoadBoard(int thisLoad)
        {
            Loading = true;
            BoardMessage = null;
            Changed?.Invoke();

            ApiResult<GlobalEntry[]> top = null;
            yield return api.GetTop(Leaderboard.Capacity, r => top = r);

            // The player's own standing, if signed in. Not having it isn't worth a message:
            // the board itself is what matters.
            PlayerProfile me = null;
            if (IsSignedIn)
            {
                string token = null;
                yield return getAccessToken(t => token = t);
                if (token != null)
                {
                    ApiResult<PlayerProfile> profile = null;
                    yield return api.GetMe(token, r => profile = r);
                    me = profile.Succeeded ? profile.Value : null;
                }
            }

            if (thisLoad != boardLoad)
            {
                yield break; // A newer load has started; let that one report.
            }
            Loading = false;
            Top = top.Succeeded ? top.Value : null;
            Me = me;
            if (!top.Succeeded)
            {
                Debug.LogWarning($"Global leaderboard: couldn't load the top scores ({top.Error}).");
                BoardMessage = "Couldn't reach the global leaderboard. Your local scores still work.";
            }
            Changed?.Invoke();
        }

        private void Fail(string message)
        {
            LastPost = PostState.Failed;
            PostMessage = message;
            Changed?.Invoke();
        }

        /// <summary>What to tell the player when the server turned a score down.</summary>
        private static string MessageFor<T>(ApiResult<T> result)
        {
            if (!result.Reached)
            {
                return "Couldn't reach the global leaderboard, so this round wasn't posted.";
            }
            switch (result.Code)
            {
                case "rate_limited":
                    return "Too many rounds too quickly, so this one wasn't posted.";
                case "round_expired":
                    return "This round took too long (over an hour), so it wasn't posted.";
                default:
                    // Unknown or reused round, too short, or an impossible score: none of which
                    // happen in normal play.
                    return "The global leaderboard didn't accept this round.";
            }
        }

        private static string Format(int score) =>
            score.ToString("N0", CultureInfo.InvariantCulture);

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>
        /// Swaps in a fake server and a stand-in for the account. Call it while the GameObject is
        /// still inactive, before Awake runs.
        /// </summary>
        internal void ConfigureForTests(ILeaderboardApi api, Func<bool> isSignedIn,
            Func<Action<string>, IEnumerator> getAccessToken)
        {
            this.api = api;
            this.isSignedIn = isSignedIn;
            this.getAccessToken = getAccessToken;
        }
    }
}
