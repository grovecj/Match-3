using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// One complete set of token art: which sprite (and tint) to draw for each
    /// <see cref="TokenType"/>. Swapping the style re-skins every token without changing what
    /// type any token is, so matching and the rest of the rules are unaffected.
    ///
    /// Create one with Create > Match3 > Token Style.
    /// </summary>
    [CreateAssetMenu(fileName = "TokenStyle", menuName = "Match3/Token Style")]
    public class TokenStyle : ScriptableObject
    {
        /// <summary>How one token type looks in this style.</summary>
        // [System.Serializable] lets Unity save a plain C# class (not a ScriptableObject or
        // MonoBehaviour) inside an asset and show it in the Inspector as a foldout.
        [System.Serializable]
        public class Entry
        {
            public TokenType tokenType;
            public Sprite sprite;
            [Tooltip("Multiplied onto the sprite. White leaves the sprite's own colors unchanged.")]
            public Color tint = Color.white;
            // The tint can't double as the particle color: most art is drawn in full color
            // with a white tint, so every burst would come out white.
            [Tooltip("Color of the particle burst when a token of this type is cleared. Pick the sprite's main color.")]
            public Color burstColor = Color.white;
        }

        [Tooltip("Stable identifier for code and save data, e.g. \"gems\". Should not change once set.")]
        [SerializeField] private string id;
        [Tooltip("Human-readable name for UI.")]
        [SerializeField] private string displayName;
        [Tooltip("One entry per token type. Every type the Board uses needs an entry.")]
        [SerializeField] private List<Entry> entries = new List<Entry>();

        public string Id => id;
        public string DisplayName => displayName;

        /// <summary>
        /// Looks up how <paramref name="type"/> should be drawn. Returns false (with a null
        /// sprite and white tint) if this style has no entry for it.
        /// </summary>
        public bool TryGetVisual(TokenType type, out Sprite sprite, out Color tint)
        {
            foreach (Entry entry in entries)
            {
                if (type != null && entry != null && entry.tokenType == type)
                {
                    sprite = entry.sprite;
                    tint = entry.tint;
                    return true;
                }
            }

            sprite = null;
            tint = Color.white;
            return false;
        }

        /// <summary>
        /// The particle color for clearing a token of <paramref name="type"/>, or white if this
        /// style has no entry for it.
        /// </summary>
        public Color BurstColorFor(TokenType type)
        {
            foreach (Entry entry in entries)
            {
                if (type != null && entry != null && entry.tokenType == type)
                {
                    return entry.burstColor;
                }
            }
            return Color.white;
        }

        /// <summary>
        /// The types in <paramref name="types"/> that this style has no sprite for. An empty
        /// list means the style covers them all.
        /// </summary>
        public List<TokenType> FindMissingTypes(IEnumerable<TokenType> types)
        {
            var missing = new List<TokenType>();
            foreach (TokenType type in types)
            {
                if (type == null || missing.Contains(type))
                {
                    continue;
                }
                if (!TryGetVisual(type, out Sprite sprite, out _) || sprite == null)
                {
                    missing.Add(type);
                }
            }
            return missing;
        }
    }
}
