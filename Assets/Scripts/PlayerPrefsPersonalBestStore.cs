using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Keeps the personal best in PlayerPrefs: Unity's small key-value store for settings, kept
    /// per user (the registry on Windows; IndexedDB in a browser, so it survives a reload of
    /// the page).
    /// </summary>
    public class PlayerPrefsPersonalBestStore : IPersonalBestStore
    {
        /// <summary>
        /// The PlayerPrefs key. It's new with issue #107: the best starts fresh with six token
        /// types, so scores from the five-type game (on the local leaderboard) never count.
        /// A future change that makes old scores unfair to compare would bump it again.
        /// </summary>
        public const string Key = "personalBest.v2.6types";

        public bool TryLoad(out int best)
        {
            // HasKey tells "never saved" apart from a saved 0, which GetInt alone can't.
            if (!PlayerPrefs.HasKey(Key))
            {
                best = 0;
                return false;
            }
            best = PlayerPrefs.GetInt(Key);
            return true;
        }

        public void Save(int best)
        {
            PlayerPrefs.SetInt(Key, best);
            // PlayerPrefs are only written to disk on Save() (or when the game quits normally).
            // A browser tab can be closed without the game quitting, so save straight away.
            PlayerPrefs.Save();
        }
    }
}
