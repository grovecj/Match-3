using System;
using UnityEngine;

namespace Match3.Achievements
{
    /// <summary>
    /// One achievement as the server describes it: what it is, and where the player stands
    /// with it. The server decides all of this, including whether it's unlocked; the game only
    /// shows it.
    ///
    /// This is the item of the "achievements contract" that every game on
    /// games.cartergrove.me shares (see Docs/Achievements.md), so nothing in this folder knows
    /// anything about Match-3 itself.
    /// </summary>
    // JsonUtility fills public fields whose names match the JSON keys exactly.
    [Serializable]
    public class Achievement
    {
        /// <summary>A short id that never changes, e.g. "rounds-50".</summary>
        public string key;
        public string name;
        public string description;
        /// <summary>The name of its picture; <see cref="AchievementIcons"/> has the sprite.</summary>
        public string icon;
        /// <summary>The number <see cref="progress"/> has to reach. 1 for something you either did or didn't.</summary>
        public int goal;
        /// <summary>How far the player is, never more than <see cref="goal"/>.</summary>
        public int progress;
        /// <summary>When it was unlocked (a UTC time as text), or empty while it's locked.</summary>
        public string unlockedAt;

        public bool IsUnlocked => !string.IsNullOrEmpty(unlockedAt);

        /// <summary>Progress from 0 (nothing yet) to 1 (done), for a progress bar.</summary>
        public float Fraction
        {
            get
            {
                if (IsUnlocked)
                {
                    return 1f;
                }
                return goal > 0 ? Mathf.Clamp01((float)progress / goal) : 0f;
            }
        }
    }
}
