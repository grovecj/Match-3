using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Whether the screen shakes when power gems go off: on by default, and switched in the
    /// Settings menu for players who find motion uncomfortable. Saved in PlayerPrefs, and
    /// synced to the player's account along with their other settings (see
    /// <c>Docs/SettingsSync.md</c>).
    ///
    /// A static class rather than a component: it's one yes/no that the Settings menu, the
    /// sync and the <see cref="ScreenShake"/> all read, and none of them owns it.
    /// </summary>
    public static class ScreenShakeSetting
    {
        /// <summary>The PlayerPrefs key it's saved under.</summary>
        public const string PrefsKey = "Settings.ScreenShake";

        /// <summary>Fired whenever <see cref="Enabled"/> changes.</summary>
        public static event System.Action Changed;

        /// <summary>True (the default) to shake, false to keep the screen still.</summary>
        public static bool Enabled
        {
            // PlayerPrefs has no bool, so it's stored as 1 or 0. GetInt's second argument is
            // what to return when nothing's been saved yet: here, "on".
            get => PlayerPrefs.GetInt(PrefsKey, 1) != 0;
            set
            {
                if (value == Enabled)
                {
                    return;
                }
                PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
                // Written to disk straight away, like the theme, so a closed browser tab keeps it.
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }
    }
}
