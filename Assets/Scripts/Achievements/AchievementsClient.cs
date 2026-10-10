using System;
using System.Collections;
using System.Collections.Generic;
using Match3.Accounts;
using Match3.Online;
using UnityEngine;

namespace Match3.Achievements
{
    /// <summary>
    /// The game's copy of the player's achievements, and the place that notices new ones.
    ///
    /// <b>The list.</b> <see cref="Refresh"/> loads it from the server: with the player's
    /// progress when signed in, or just what there is to earn for a guest. The Achievements
    /// panel shows <see cref="Items"/> and follows <see cref="Changed"/>.
    ///
    /// <b>Unlocks.</b> The game never unlocks anything itself. When the server accepts a
    /// round's score, it says which achievements that round earned, and this raises
    /// <see cref="Unlocked"/> once for each, which is what the toast listens to.
    ///
    /// Like the leaderboard client, it works in the background and never gets in the way of
    /// play: if the server can't be reached there's a <see cref="Message"/>, and that's all.
    /// </summary>
    public class AchievementsClient : MonoBehaviour
    {
        [Tooltip("Who's signed in; the source of the access token.")]
        [SerializeField] private AccountManager accountManager;
        [Tooltip("Tells us when a round's score has been posted, and what it unlocked.")]
        [SerializeField] private OnlineLeaderboardClient online;
        [Tooltip("The server. Only change it to test against a local server, and don't commit that.")]
        [SerializeField] private string baseUrl = HttpLeaderboardApi.DefaultBaseUrl;

        /// <summary>Raised whenever <see cref="Items"/>, <see cref="Loading"/> or <see cref="Message"/> changes.</summary>
        public event Action Changed;

        /// <summary>Raised once for each achievement the player has just unlocked.</summary>
        public event Action<Achievement> Unlocked;

        /// <summary>The achievements, in the order to show them, or null if they haven't been loaded.</summary>
        public Achievement[] Items { get; private set; }

        /// <summary>True while <see cref="Refresh"/> is waiting for the server.</summary>
        public bool Loading { get; private set; }

        /// <summary>Why the list couldn't be loaded, or null.</summary>
        public string Message { get; private set; }

        public bool IsSignedIn => isSignedIn != null && isSignedIn();

        private IAchievementsApi api;
        // How we ask about the account; tests swap in their own (see ConfigureForTests).
        private Func<bool> isSignedIn;
        private Func<Action<string>, IEnumerator> getAccessToken;

        // Only the latest load's answer is used (the same idea as OnlineLeaderboardClient's).
        private int load;
        // Whether Items has the signed-in player's progress, rather than just the catalog.
        private bool listIsMine;
        private bool wasSignedIn;
        // Keys already announced, so nothing is ever announced twice.
        private readonly HashSet<string> announced = new HashSet<string>();

        private void Awake()
        {
            if (api == null)
            {
                if (accountManager == null || online == null)
                {
                    Debug.LogError("AchievementsClient: assign the AccountManager and the OnlineLeaderboardClient in the Inspector.", this);
                    enabled = false;
                    return;
                }
                api = new HttpAchievementsApi(baseUrl);
                isSignedIn = () => accountManager.State == AccountManager.AccountState.SignedIn;
                getAccessToken = accountManager.GetAccessToken;
            }
            // With ConfigureForTests there's no AccountManager or leaderboard client: the tests
            // call OnAccountChanged and OnScorePosted themselves.
            if (accountManager != null)
            {
                accountManager.Changed += OnAccountChanged;
            }
            if (online != null)
            {
                online.ScorePosted += OnScorePosted;
            }
            wasSignedIn = IsSignedIn;
        }

        private void Start()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (accountManager != null)
            {
                accountManager.Changed -= OnAccountChanged;
            }
            if (online != null)
            {
                online.ScorePosted -= OnScorePosted;
            }
        }

        /// <summary>Loads the list again from the server.</summary>
        public void Refresh()
        {
            Load(announceNew: false);
        }

        // The AccountManager raises Changed for all sorts of things (a new message, say); we
        // only care about signing in and out.
        internal void OnAccountChanged()
        {
            bool signedIn = IsSignedIn;
            if (signedIn == wasSignedIn)
            {
                return;
            }
            wasSignedIn = signedIn;
            // What we have belongs to whoever was here before.
            Items = null;
            listIsMine = false;
            announced.Clear();
            Refresh();
        }

        // Called when the server has accepted a round's score.
        internal void OnScorePosted(Achievement[] unlocked)
        {
            if (unlocked != null)
            {
                foreach (Achievement achievement in unlocked)
                {
                    Announce(achievement);
                }
            }
            // The round changed the progress of other achievements too, so load the list again.
            // And if the server's reply was lost (unlocked == null), comparing the new list with
            // the old one is how we find out what the round unlocked.
            Load(announceNew: unlocked == null);
        }

        private void Load(bool announceNew)
        {
            if (enabled)
            {
                StartCoroutine(LoadList(++load, announceNew));
            }
        }

        private IEnumerator LoadList(int thisLoad, bool announceNew)
        {
            Loading = true;
            Message = null;
            Changed?.Invoke();

            bool signedIn = IsSignedIn;
            ApiResult<Achievement[]> result = null;
            if (signedIn)
            {
                string token = null;
                yield return getAccessToken(t => token = t);
                if (token != null)
                {
                    yield return api.GetMine(token, r => result = r);
                }
            }
            else
            {
                yield return api.GetCatalog(r => result = r);
            }

            if (thisLoad != load)
            {
                yield break; // A newer load has started; let that one report.
            }
            Loading = false;
            if (result != null && result.Succeeded)
            {
                Achievement[] before = listIsMine ? Items : null;
                Items = result.Value;
                listIsMine = signedIn;
                if (announceNew && signedIn)
                {
                    foreach (Achievement achievement in NewlyUnlocked(before, Items))
                    {
                        Announce(achievement);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Achievements: couldn't load the list ({(result == null ? "no access token" : result.Error)}).");
                Message = result == null
                    ? "Couldn't reach the account server."
                    : "Couldn't reach the server for your achievements.";
            }
            Changed?.Invoke();
        }

        private void Announce(Achievement achievement)
        {
            // HashSet.Add returns false if the key was already in the set.
            if (achievement != null && announced.Add(achievement.key))
            {
                Unlocked?.Invoke(achievement);
            }
        }

        /// <summary>
        /// The achievements that are unlocked in <paramref name="after"/> but weren't in
        /// <paramref name="before"/>, in <paramref name="after"/>'s order. With no
        /// <paramref name="before"/> to compare with, there's no telling what's new, so: none.
        /// </summary>
        // Static and public so the Edit Mode tests can check it without a scene.
        public static List<Achievement> NewlyUnlocked(Achievement[] before, Achievement[] after)
        {
            var result = new List<Achievement>();
            if (before == null || after == null)
            {
                return result;
            }
            var alreadyUnlocked = new HashSet<string>();
            foreach (Achievement achievement in before)
            {
                if (achievement.IsUnlocked)
                {
                    alreadyUnlocked.Add(achievement.key);
                }
            }
            foreach (Achievement achievement in after)
            {
                if (achievement.IsUnlocked && !alreadyUnlocked.Contains(achievement.key))
                {
                    result.Add(achievement);
                }
            }
            return result;
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>
        /// Swaps in a fake server and a stand-in for the account. Call it while the GameObject is
        /// still inactive, before Awake runs.
        /// </summary>
        internal void ConfigureForTests(IAchievementsApi api, Func<bool> isSignedIn,
            Func<Action<string>, IEnumerator> getAccessToken)
        {
            this.api = api;
            this.isSignedIn = isSignedIn;
            this.getAccessToken = getAccessToken;
        }
    }
}
