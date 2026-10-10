using System;
using UnityEngine;

namespace Match3.Achievements
{
    /// <summary>
    /// This game's pictures for achievements. The server only names an icon ("rounds",
    /// "score", ...); this asset says which sprite that is here. A name it doesn't know (say,
    /// from an achievement added to the server after this build) gets the fallback.
    ///
    /// Match3 > Build Achievements Menu creates the asset from the sprites in
    /// Assets/Sprites/Achievements, named after their files.
    /// </summary>
    [CreateAssetMenu(fileName = "AchievementIcons", menuName = "Match3/Achievement Icons")]
    public class AchievementIcons : ScriptableObject
    {
        // A small struct the Inspector can show as a list of name/sprite pairs. (Unity can't
        // serialize a Dictionary, so a list we search is the usual stand-in.)
        [Serializable]
        public struct Entry
        {
            public string name;
            public Sprite sprite;
        }

        [SerializeField] private Entry[] entries = new Entry[0];
        [Tooltip("Used for an icon name that isn't in the list.")]
        [SerializeField] private Sprite fallback;

        /// <summary>The sprite for the icon called <paramref name="iconName"/>, or the fallback.</summary>
        public Sprite Get(string iconName)
        {
            foreach (Entry entry in entries)
            {
                if (entry.name == iconName && entry.sprite != null)
                {
                    return entry.sprite;
                }
            }
            return fallback;
        }
    }
}
