using System;
using System.Collections;
using Match3.Accounts;
using UnityEngine;

namespace Match3.Online
{
    /// <summary>
    /// Keeps the player's settings (token style, board theme, sound volume) the same on every
    /// device they sign in on, by keeping a copy on their account.
    ///
    /// PlayerPrefs stays the settings' home: the ThemeManager and AudioManager read and write
    /// it exactly as before, and it's all a signed-out or offline player ever uses. This class
    /// only copies between PlayerPrefs and the server, in two directions:
    ///
    /// <b>Down, when the player signs in</b> (or the game starts already signed in): load the
    /// account's settings and apply them here. The server wins.
    ///
    /// <b>Up, when the Settings menu closes</b> with something changed: save this device's
    /// settings to the account. The last save wins, even if another device saved in between.
    ///
    /// A save that can't be sent (offline, server down) isn't lost. It's remembered in
    /// PlayerPrefs as "pending" and sent at the next chance: the next time Settings closes, or
    /// the next time the game starts. While a save is pending this device's settings are the
    /// newer ones, so they go up instead of the server's coming down.
    ///
    /// Like the leaderboard client, nothing here ever stops play or shows an error: settings
    /// always work locally, and syncing is a bonus on top.
    /// </summary>
    public class PreferencesSync : MonoBehaviour
    {
        /// <summary>
        /// PlayerPrefs key holding the id of the account that has a save waiting to be sent
        /// (empty or missing: none). It's the account's id rather than just "yes", so one
        /// player's unsent settings are never saved to another account that signs in here.
        /// </summary>
        public const string PendingForKey = "Settings.SyncPendingFor";

        private const long UnknownVersion = -1;

        [Tooltip("Who's signed in; the source of the access token.")]
        [SerializeField] private AccountManager accountManager;
        [SerializeField] private ThemeManager themeManager;
        [SerializeField] private AudioManager audioManager;
        [Tooltip("Settings are sent to the account when this menu closes.")]
        [SerializeField] private SettingsMenu settingsMenu;
        [Tooltip("The server (the leaderboard's). Only change it to test against a local server, and don't commit that.")]
        [SerializeField] private string baseUrl = HttpLeaderboardApi.DefaultBaseUrl;

        private IPreferencesApi api;
        // How we ask about the account. Normally the AccountManager's; tests swap in their own
        // (see ConfigureForTests).
        private Func<string> getUserId;
        private Func<Action<string>, IEnumerator> getAccessToken;

        // The account we're syncing for, or null when signed out. Every request remembers which
        // account it was for, and its answer is dropped if that's no longer who's signed in.
        private string userId;
        // The version of the settings on the server, as far as we know. Unknown until the first
        // load for this account.
        private long version = UnknownVersion;
        // The local settings when we last looked, to tell whether Settings closed with a change.
        private PreferenceValues seen;
        // One sync runs at a time. If another is asked for meanwhile, it runs straight after.
        private bool syncing;
        private bool syncAgain;

        /// <summary>True while a request to the server is out.</summary>
        public bool IsSyncing => syncing;

        /// <summary>True if this account has changes here that haven't reached the server yet.</summary>
        public bool HasPendingSave =>
            userId != null && PlayerPrefs.GetString(PendingForKey, "") == userId;

        private void Awake()
        {
            if (themeManager == null || audioManager == null)
            {
                Debug.LogError("PreferencesSync: assign the ThemeManager and AudioManager in the Inspector.", this);
                enabled = false;
                return;
            }
            if (api != null)
            {
                return; // Already set up by ConfigureForTests.
            }
            if (accountManager == null || settingsMenu == null)
            {
                Debug.LogError("PreferencesSync: assign the AccountManager and SettingsMenu in the Inspector.", this);
                enabled = false;
                return;
            }
            api = new HttpPreferencesApi(baseUrl);
            getUserId = () => accountManager.State == AccountManager.AccountState.SignedIn ? accountManager.UserId : null;
            getAccessToken = accountManager.GetAccessToken;
        }

        private void OnEnable()
        {
            if (accountManager != null)
            {
                accountManager.Changed += OnAccountChanged;
            }
            if (settingsMenu != null)
            {
                settingsMenu.Closed += OnSettingsClosed;
            }
            // Enabled again after Start has run? Catch up on who's signed in. (The first time,
            // Start does this, once the other managers are ready.)
            if (seen != null)
            {
                OnAccountChanged();
            }
        }

        private void OnDisable()
        {
            if (accountManager != null)
            {
                accountManager.Changed -= OnAccountChanged;
            }
            if (settingsMenu != null)
            {
                settingsMenu.Closed -= OnSettingsClosed;
            }
            // Disabling a component stops its coroutines where they are, so the one that would
            // have cleared this never gets to. Forget the account too, so the sync starts over
            // if we're enabled again.
            syncing = false;
            userId = null;
        }

        private void Start()
        {
            // Start (not Awake) so the ThemeManager and AudioManager have loaded what's saved.
            seen = ReadLocal();
            // In case the account was already signed in before we started listening.
            OnAccountChanged();
        }

        /// <summary>
        /// Called on every change to the account (signing in is only one of them: its message
        /// changing is another). Starts a sync when a different player is now signed in.
        /// </summary>
        internal void OnAccountChanged()
        {
            string now = getUserId();
            if (now == userId)
            {
                return;
            }
            userId = now;
            version = UnknownVersion;
            if (userId != null)
            {
                RequestSync();
            }
        }

        /// <summary>Called when the Settings menu closes. Sends the settings if they changed.</summary>
        internal void OnSettingsClosed()
        {
            PreferenceValues local = ReadLocal();
            bool changed = !local.Matches(seen);
            seen = local;

            if (userId == null)
            {
                return; // Signed out: PlayerPrefs is all there is.
            }
            if (changed)
            {
                PlayerPrefs.SetString(PendingForKey, userId);
                PlayerPrefs.Save();
            }
            // Also the retry for a save that couldn't be sent earlier, or for a first load
            // that failed (the game started offline, say).
            if (HasPendingSave || version == UnknownVersion)
            {
                RequestSync();
            }
        }

        private void RequestSync()
        {
            if (syncing)
            {
                syncAgain = true;
            }
            else
            {
                StartCoroutine(Sync());
            }
        }

        private IEnumerator Sync()
        {
            syncing = true;
            do
            {
                syncAgain = false;
                yield return SyncOnce(userId);
            }
            while (syncAgain && userId != null);
            syncing = false;
        }

        /// <summary>
        /// One pass: learn what the server has if we don't know yet (applying it here unless
        /// this device has newer changes), then send this device's changes if it has any.
        /// </summary>
        private IEnumerator SyncOnce(string forUser)
        {
            string token = null;
            yield return getAccessToken(t => token = t);
            if (forUser != userId)
            {
                yield break; // Signed out (or someone else signed in) while we waited.
            }
            if (token == null)
            {
                Debug.LogWarning("Settings sync: no access token, so settings weren't synced.");
                yield break;
            }

            if (version == UnknownVersion)
            {
                ApiResult<SavedPreferences> loaded = null;
                yield return api.Load(token, r => loaded = r);
                if (forUser != userId)
                {
                    yield break;
                }
                if (!loaded.Succeeded)
                {
                    Debug.LogWarning($"Settings sync: couldn't load the account's settings ({loaded.Error}).");
                    yield break;
                }
                version = loaded.Value.version;

                if (version == 0)
                {
                    // Nothing has ever been saved for this account, so there's nothing to
                    // apply. Make this device's settings the account's first ones instead.
                    PlayerPrefs.SetString(PendingForKey, forUser);
                    PlayerPrefs.Save();
                }
                else if (!HasPendingSave)
                {
                    Apply(loaded.Value.data);
                }
            }

            if (!HasPendingSave)
            {
                yield break;
            }

            // Send this device's settings. If the server says our version is out of date,
            // another device has saved since we looked. "Last write wins": find out the new
            // version and save over it. Twice is enough; a third device saving in that very
            // moment can wait for the next sync.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                PreferenceValues sent = ReadLocal();
                var change = new SavedPreferences { data = sent, version = version };
                ApiResult<SavedPreferences> saved = null;
                yield return api.Save(token, change, r => saved = r);
                if (forUser != userId)
                {
                    yield break;
                }
                if (saved.Succeeded)
                {
                    version = saved.Value.version;
                    if (sent.Matches(ReadLocal()))
                    {
                        PlayerPrefs.DeleteKey(PendingForKey);
                        PlayerPrefs.Save();
                    }
                    else
                    {
                        // The player changed something again while this save was on its way.
                        syncAgain = true;
                    }
                    yield break;
                }
                if (saved.Code != SavedPreferences.VersionConflict || attempt == 2)
                {
                    // Still pending, so it's tried again later.
                    Debug.LogWarning($"Settings sync: couldn't save the settings to the account ({saved.Error}).");
                    yield break;
                }

                ApiResult<SavedPreferences> current = null;
                yield return api.Load(token, r => current = r);
                if (forUser != userId)
                {
                    yield break;
                }
                if (!current.Succeeded)
                {
                    Debug.LogWarning($"Settings sync: couldn't reload the account's settings ({current.Error}).");
                    yield break;
                }
                version = current.Value.version;
            }
        }

        /// <summary>This device's settings, in the shape the server stores them.</summary>
        private PreferenceValues ReadLocal()
        {
            return new PreferenceValues
            {
                tokenStyle = themeManager.TokenStyle != null ? themeManager.TokenStyle.Id : "",
                boardTheme = themeManager.BoardTheme != null ? themeManager.BoardTheme.Id : "",
                soundVolume = audioManager.Volume,
            };
        }

        /// <summary>
        /// Makes this device's settings match <paramref name="values"/>, through the same
        /// managers the Settings menu uses, so the board redraws and PlayerPrefs is updated.
        /// A setting that's missing, or names a style or theme this build doesn't have (one
        /// added in a newer version, say), is left as it is here.
        /// </summary>
        private void Apply(PreferenceValues values)
        {
            TokenStyle style = ThemeManager.FindById(themeManager.TokenStyles, values.tokenStyle, s => s.Id);
            if (style != null)
            {
                themeManager.SetTokenStyle(style);
            }
            BoardTheme theme = ThemeManager.FindById(themeManager.BoardThemes, values.boardTheme, t => t.Id);
            if (theme != null)
            {
                themeManager.SetBoardTheme(theme);
            }
            if (values.HasSoundVolume)
            {
                audioManager.SetVolume(values.soundVolume);
                audioManager.SaveVolume();
            }
            // These came from the server, so they're not a change to send back.
            seen = ReadLocal();
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>
        /// Swaps in a fake server and a stand-in for the account, and the managers to sync.
        /// Call it while the GameObject is still inactive, before Awake runs. With no
        /// AccountManager or SettingsMenu to listen to, tests call <see cref="OnAccountChanged"/>
        /// and <see cref="OnSettingsClosed"/> themselves.
        /// </summary>
        internal void ConfigureForTests(IPreferencesApi api, Func<string> getUserId,
            Func<Action<string>, IEnumerator> getAccessToken, ThemeManager themeManager, AudioManager audioManager)
        {
            this.api = api;
            this.getUserId = getUserId;
            this.getAccessToken = getAccessToken;
            this.themeManager = themeManager;
            this.audioManager = audioManager;
        }
    }
}
