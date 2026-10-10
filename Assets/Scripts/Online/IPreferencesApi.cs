using System;
using System.Collections;

namespace Match3.Online
{
    /// <summary>
    /// The server's copy of the player's settings (<c>/me/preferences</c> on
    /// grovecj/match-3-api). <see cref="HttpPreferencesApi"/> is the real one; tests use a fake.
    ///
    /// Like <see cref="ILeaderboardApi"/>, every method is a coroutine that calls <c>done</c>
    /// exactly once and never throws.
    /// </summary>
    public interface IPreferencesApi
    {
        /// <summary>
        /// <c>GET /me/preferences</c>: what's saved on the account. A player who has never saved
        /// anything gets empty settings at version 0.
        /// </summary>
        IEnumerator Load(string accessToken, Action<ApiResult<SavedPreferences>> done);

        /// <summary>
        /// <c>PUT /me/preferences</c>: saves <paramref name="change"/>'s settings over the
        /// version it names, and answers with what's now stored (one version higher). If the
        /// stored version isn't that one any more (another device saved in between), nothing is
        /// saved and the result's <see cref="ApiResult{T}.Code"/> is
        /// <see cref="VersionConflict"/>.
        /// </summary>
        IEnumerator Save(string accessToken, SavedPreferences change, Action<ApiResult<SavedPreferences>> done);
    }

    /// <summary>
    /// The settings as the server stores them: the values, and a version number that goes up by
    /// one on every save. The version is how the server notices two devices saving at once
    /// ("optimistic concurrency": nobody locks anything, a save just says which version it
    /// thinks it's replacing, and is turned down if it's wrong).
    /// </summary>
    [Serializable]
    public class SavedPreferences
    {
        /// <summary>The error code of a save that named an out-of-date version.</summary>
        public const string VersionConflict = "version_conflict";

        // JsonUtility reads and writes a nested [Serializable] class as a nested JSON object.
        public PreferenceValues data = new PreferenceValues();
        public long version;
    }

    /// <summary>
    /// The settings that follow the player's account. Styles and themes are named by their
    /// asset's <c>Id</c>, the same strings PlayerPrefs keeps.
    /// </summary>
    [Serializable]
    public class PreferenceValues
    {
        /// <summary>What <see cref="soundVolume"/> is when the server has no volume saved.</summary>
        public const float NoVolume = -1f;

        public string tokenStyle = "";
        public string boardTheme = "";
        /// <summary>0 (muted) to 1 (full), or <see cref="NoVolume"/>.</summary>
        public float soundVolume = NoVolume;

        public bool HasSoundVolume => soundVolume >= 0f;

        /// <summary>True if <paramref name="other"/> holds the same settings.</summary>
        public bool Matches(PreferenceValues other)
        {
            // Floats that went to the server and back as text may differ in the last digit, so
            // compare the volume with a little slack rather than with ==.
            return other != null
                && tokenStyle == other.tokenStyle
                && boardTheme == other.boardTheme
                && Math.Abs(soundVolume - other.soundVolume) < 0.001f;
        }
    }
}
