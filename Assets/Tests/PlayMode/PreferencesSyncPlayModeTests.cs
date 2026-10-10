using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Match3.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Tests
{
    /// <summary>
    /// Play Mode tests for <see cref="PreferencesSync"/>: the account's settings coming down on
    /// sign-in, this device's going up when Settings closes, and what happens offline or when
    /// another device saved first. A fake <see cref="IPreferencesApi"/> stands in for the server
    /// (and behaves like it, versions and all); the ThemeManager and AudioManager are real.
    ///
    /// Those two save to PlayerPrefs, which the Editor shares with the real game, so the
    /// player's saved settings are backed up first and put back afterwards.
    /// </summary>
    public class PreferencesSyncPlayModeTests
    {
        private const int MaxFrames = 100;
        private const string UserId = "user-1";

        private static readonly string[] Keys =
        {
            ThemeManager.TokenStyleKey, ThemeManager.BoardThemeKey, PreferencesSync.PendingForKey,
        };

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private readonly Dictionary<string, string> savedStrings = new Dictionary<string, string>();
        private bool hadVolume;
        private float savedVolume;
        private float savedListenerVolume;

        private BoardTheme midnight;
        private BoardTheme ocean;
        private TokenStyle gems;
        private TokenStyle glass;

        private ThemeManager themes;
        private AudioManager audio;
        private PreferencesSync sync;
        private FakePreferencesApi api;
        private string signedInAs;

        [SetUp]
        public void SetUp()
        {
            foreach (string key in Keys)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    savedStrings[key] = PlayerPrefs.GetString(key);
                }
                PlayerPrefs.DeleteKey(key);
            }
            hadVolume = PlayerPrefs.HasKey(AudioManager.SoundVolumeKey);
            savedVolume = PlayerPrefs.GetFloat(AudioManager.SoundVolumeKey);
            PlayerPrefs.DeleteKey(AudioManager.SoundVolumeKey);
            savedListenerVolume = AudioListener.volume;

            midnight = Create<BoardTheme>("midnight");
            ocean = Create<BoardTheme>("ocean");
            gems = Create<TokenStyle>("gems");
            glass = Create<TokenStyle>("glass");

            api = new FakePreferencesApi();
            signedInAs = UserId;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object obj in created)
            {
                UnityEngine.Object.Destroy(obj);
            }
            created.Clear();

            foreach (string key in Keys)
            {
                if (savedStrings.TryGetValue(key, out string value))
                {
                    PlayerPrefs.SetString(key, value);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }
            savedStrings.Clear();
            if (hadVolume)
            {
                PlayerPrefs.SetFloat(AudioManager.SoundVolumeKey, savedVolume);
            }
            else
            {
                PlayerPrefs.DeleteKey(AudioManager.SoundVolumeKey);
            }
            PlayerPrefs.Save();
            AudioListener.volume = savedListenerVolume;
        }

        /// <summary>A style or theme asset that exists only for this test, with the given id.</summary>
        private T Create<T>(string id) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = id;
            created.Add(asset);
            SetPrivate(asset, "id", id);
            return asset;
        }

        // Private [SerializeField]s can't be set from here directly. Reflection can: it looks a
        // field up by name at run time, private or not. (The Edit Mode tests use SerializedObject
        // for this, but that's Editor-only, and reflection is all a test needs.)
        private static void SetPrivate(object target, string field, object value)
        {
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        /// <summary>
        /// Starts the game's settings (midnight, gems, the default volume, unless PlayerPrefs
        /// says otherwise) with a PreferencesSync, and waits for its first sync to finish.
        /// </summary>
        private IEnumerator CreateSync()
        {
            var go = new GameObject("PreferencesSync (test)");
            created.Add(go);
            // Inactive, so no Awake runs until everything is plugged in.
            go.SetActive(false);

            themes = go.AddComponent<ThemeManager>();
            SetPrivate(themes, "boardThemes", new[] { midnight, ocean });
            SetPrivate(themes, "tokenStyles", new[] { gems, glass });
            SetPrivate(themes, "boardTheme", midnight);
            SetPrivate(themes, "tokenStyle", gems);
            audio = go.AddComponent<AudioManager>();

            sync = go.AddComponent<PreferencesSync>();
            sync.ConfigureForTests(api, () => signedInAs, GetAccessToken, themes, audio);
            go.SetActive(true);

            // A frame for Start, where the first sync begins.
            yield return null;
            yield return WaitUntilIdle();
        }

        // Stands in for AccountManager.GetAccessToken.
        private IEnumerator GetAccessToken(Action<string> done)
        {
            yield return null;
            done(signedInAs != null ? "access-token" : null);
        }

        private IEnumerator WaitUntilIdle()
        {
            for (int frame = 0; frame < MaxFrames && sync.IsSyncing; frame++)
            {
                yield return null;
            }
            Assert.That(sync.IsSyncing, Is.False, "Timed out waiting for the sync.");
        }

        private IEnumerator CloseSettings()
        {
            sync.OnSettingsClosed();
            yield return WaitUntilIdle();
        }

        private static PreferenceValues Values(string tokenStyle, string boardTheme, float soundVolume) =>
            new PreferenceValues { tokenStyle = tokenStyle, boardTheme = boardTheme, soundVolume = soundVolume };

        [UnityTest]
        public IEnumerator SigningIn_AppliesTheAccountsSettings()
        {
            api.Store(3, Values("glass", "ocean", 0.25f));

            yield return CreateSync();

            Assert.That(themes.TokenStyle, Is.SameAs(glass));
            Assert.That(themes.BoardTheme, Is.SameAs(ocean));
            Assert.That(audio.Volume, Is.EqualTo(0.25f).Within(0.001f));
            // ...and into PlayerPrefs, so they're still here next time, signed in or not.
            Assert.That(PlayerPrefs.GetString(ThemeManager.BoardThemeKey), Is.EqualTo("ocean"));
            Assert.That(PlayerPrefs.GetFloat(AudioManager.SoundVolumeKey), Is.EqualTo(0.25f).Within(0.001f));
            // Nothing changed here, so nothing is sent back.
            Assert.That(api.Calls, Is.EqualTo(new[] { "Load" }));
        }

        [UnityTest]
        public IEnumerator UnknownIdsAndMissingSettings_AreLeftAsTheyAre()
        {
            // As if saved by a newer version of the game, with a theme this one doesn't have.
            api.Store(1, Values("", "theme-from-the-future", PreferenceValues.NoVolume));

            yield return CreateSync();

            Assert.That(themes.TokenStyle, Is.SameAs(gems));
            Assert.That(themes.BoardTheme, Is.SameAs(midnight));
            Assert.That(audio.Volume, Is.EqualTo(AudioManager.DefaultVolume).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator NothingSavedYet_SavesThisDevicesSettings()
        {
            yield return CreateSync();

            Assert.That(api.Version, Is.EqualTo(1));
            Assert.That(api.Data.Matches(Values("gems", "midnight", AudioManager.DefaultVolume)), Is.True);
            Assert.That(sync.HasPendingSave, Is.False);
        }

        [UnityTest]
        public IEnumerator SignedOut_NeverCallsTheServer()
        {
            signedInAs = null;
            yield return CreateSync();

            themes.SetBoardTheme(ocean);
            yield return CloseSettings();

            Assert.That(api.Calls, Is.Empty);
            Assert.That(sync.HasPendingSave, Is.False);
        }

        [UnityTest]
        public IEnumerator ClosingSettingsWithAChange_SavesIt()
        {
            api.Store(3, Values("gems", "midnight", 0.8f));
            yield return CreateSync();

            themes.SetBoardTheme(ocean);
            audio.SetVolume(0.4f);
            yield return CloseSettings();

            Assert.That(api.Data.Matches(Values("gems", "ocean", 0.4f)), Is.True);
            // Saved over the version we loaded, which makes the next one.
            Assert.That(api.SavedOverVersions, Is.EqualTo(new long[] { 3 }));
            Assert.That(api.Version, Is.EqualTo(4));
            Assert.That(sync.HasPendingSave, Is.False);
        }

        [UnityTest]
        public IEnumerator ClosingSettingsUnchanged_SendsNothing()
        {
            api.Store(3, Values("gems", "midnight", 0.8f));
            yield return CreateSync();

            yield return CloseSettings();

            Assert.That(api.Calls, Is.EqualTo(new[] { "Load" }));
        }

        [UnityTest]
        public IEnumerator Offline_TheChangeAppliesHere_AndIsSentLater()
        {
            api.Store(3, Values("gems", "midnight", 0.8f));
            yield return CreateSync();

            api.Down = true;
            themes.SetBoardTheme(ocean);
            yield return CloseSettings();

            Assert.That(themes.BoardTheme, Is.SameAs(ocean));
            Assert.That(api.Data.boardTheme, Is.EqualTo("midnight"));
            Assert.That(sync.HasPendingSave, Is.True);

            // Back online. Nothing changes this time, but closing Settings is the next chance.
            api.Down = false;
            yield return CloseSettings();

            Assert.That(api.Data.boardTheme, Is.EqualTo("ocean"));
            Assert.That(sync.HasPendingSave, Is.False);
        }

        [UnityTest]
        public IEnumerator PendingSaveFromLastTime_GoesUpInsteadOfTheServersComingDown()
        {
            // Last session ended offline with a change (ocean) that never reached the server.
            PlayerPrefs.SetString(ThemeManager.BoardThemeKey, "ocean");
            PlayerPrefs.SetString(PreferencesSync.PendingForKey, UserId);
            api.Store(3, Values("glass", "midnight", 0.5f));

            yield return CreateSync();

            Assert.That(themes.BoardTheme, Is.SameAs(ocean));
            Assert.That(themes.TokenStyle, Is.SameAs(gems));
            Assert.That(api.Data.Matches(Values("gems", "ocean", AudioManager.DefaultVolume)), Is.True);
            Assert.That(sync.HasPendingSave, Is.False);
        }

        [UnityTest]
        public IEnumerator PendingSaveForAnotherAccount_IsNotSent()
        {
            PlayerPrefs.SetString(PreferencesSync.PendingForKey, "someone-else");
            api.Store(3, Values("glass", "ocean", 0.5f));

            yield return CreateSync();

            // The server wins, as on any sign-in.
            Assert.That(themes.BoardTheme, Is.SameAs(ocean));
            Assert.That(api.Calls, Is.EqualTo(new[] { "Load" }));
            // The other account's unsent change was to settings that are gone now. Its marker
            // goes too, or these settings would be saved to that account when it comes back.
            Assert.That(PlayerPrefs.HasKey(PreferencesSync.PendingForKey), Is.False);
        }

        [UnityTest]
        public IEnumerator AnotherDeviceSavedFirst_TheLastSaveWins()
        {
            api.Store(3, Values("gems", "midnight", 0.8f));
            yield return CreateSync();

            // Another device saves twice while this one sits at version 3.
            api.Store(5, Values("glass", "midnight", 0.1f));
            themes.SetBoardTheme(ocean);
            yield return CloseSettings();

            // Turned down at version 3, then saved over version 5.
            Assert.That(api.SavedOverVersions, Is.EqualTo(new long[] { 3, 5 }));
            Assert.That(api.Version, Is.EqualTo(6));
            Assert.That(api.Data.Matches(Values("gems", "ocean", 0.8f)), Is.True);
            Assert.That(sync.HasPendingSave, Is.False);
        }

        [UnityTest]
        public IEnumerator SigningInLater_AppliesTheAccountsSettings()
        {
            signedInAs = null;
            api.Store(3, Values("glass", "ocean", 0.25f));
            yield return CreateSync();
            Assert.That(themes.BoardTheme, Is.SameAs(midnight));

            signedInAs = UserId;
            sync.OnAccountChanged();
            yield return WaitUntilIdle();

            Assert.That(themes.BoardTheme, Is.SameAs(ocean));
            Assert.That(themes.TokenStyle, Is.SameAs(glass));
        }

        /// <summary>
        /// A pretend server holding one account's settings. It keeps versions the way the real
        /// one does: a save must name the stored version, and bumps it by one. Each request
        /// takes a frame and is recorded in <see cref="Calls"/>.
        /// </summary>
        private sealed class FakePreferencesApi : IPreferencesApi
        {
            public readonly List<string> Calls = new List<string>();
            /// <summary>The version each save said it was replacing.</summary>
            public readonly List<long> SavedOverVersions = new List<long>();

            public long Version;
            public PreferenceValues Data = new PreferenceValues();
            /// <summary>True = every request fails as if there were no connection.</summary>
            public bool Down;

            public void Store(long version, PreferenceValues data)
            {
                Version = version;
                Data = data;
            }

            public IEnumerator Load(string accessToken, Action<ApiResult<SavedPreferences>> done)
            {
                Calls.Add("Load");
                yield return null;
                done(Down ? Unreachable() : ApiResult<SavedPreferences>.Success(Current()));
            }

            public IEnumerator Save(string accessToken, SavedPreferences change, Action<ApiResult<SavedPreferences>> done)
            {
                Calls.Add("Save");
                yield return null;
                if (Down)
                {
                    done(Unreachable());
                    yield break;
                }
                SavedOverVersions.Add(change.version);
                if (change.version != Version)
                {
                    done(ApiResult<SavedPreferences>.Failure(
                        SavedPreferences.VersionConflict, SavedPreferences.VersionConflict, reached: true));
                    yield break;
                }
                Version++;
                Data = change.data;
                done(ApiResult<SavedPreferences>.Success(Current()));
            }

            private SavedPreferences Current() => new SavedPreferences { data = Data, version = Version };

            private static ApiResult<SavedPreferences> Unreachable() =>
                ApiResult<SavedPreferences>.Failure("Cannot connect to destination host");
        }
    }
}
