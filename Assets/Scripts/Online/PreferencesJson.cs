using System.Text.RegularExpressions;
using UnityEngine;

namespace Match3.Online
{
    /// <summary>
    /// Reads and writes the <c>/me/preferences</c> JSON:
    /// <code>{"data":{"tokenStyle":"gems","boardTheme":"ocean","soundVolume":0.8,"screenShake":true},"version":3}</code>
    ///
    /// The server doesn't look inside <c>data</c>; it stores whatever object the game sends. So
    /// a reply can be missing any of the settings (nothing saved yet, or saved by an older
    /// version of the game) and can hold ones this version doesn't know, which are skipped.
    /// </summary>
    public static class PreferencesJson
    {
        /// <summary>The reply as an object, or null if it isn't a preferences reply at all.</summary>
        public static SavedPreferences Parse(string json)
        {
            // Every real reply has a version, so text without one (say "{}") isn't one.
            if (json == null || !Regex.IsMatch(json, "\"version\"\\s*:\\s*\\d"))
            {
                return null;
            }
            SavedPreferences saved = LeaderboardJson.Parse<SavedPreferences>(json);
            if (saved == null)
            {
                return null;
            }

            saved.data ??= new PreferenceValues();
            saved.data.tokenStyle ??= "";
            saved.data.boardTheme ??= "";
            // JsonUtility can't say "this key was missing": a missing number is just 0, which
            // here would mean "muted". So look for the key ourselves.
            if (!Regex.IsMatch(json, "\"soundVolume\"\\s*:\\s*[0-9]"))
            {
                saved.data.soundVolume = PreferenceValues.NoVolume;
            }
            // The same for the screen shake: a missing bool reads as false, which would turn
            // shaking off for everyone whose account was saved before the setting existed.
            // (Set either way: hasScreenShake isn't in the JSON, so the reader never sets it.)
            saved.data.hasScreenShake = Regex.IsMatch(json, "\"screenShake\"\\s*:\\s*(true|false)");
            if (!saved.data.hasScreenShake)
            {
                saved.data.screenShake = true;
            }
            return saved;
        }

        /// <summary>The body of a save: the settings, and the version they replace.</summary>
        public static string ToJson(SavedPreferences change) => JsonUtility.ToJson(change);
    }
}
