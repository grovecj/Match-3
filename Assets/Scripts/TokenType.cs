using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Data describing one kind of token (gem): its identity and how it looks.
    ///
    /// A ScriptableObject is a data container that lives as an asset file in the project
    /// rather than on a GameObject in a scene. Each .asset file is one shared instance, so
    /// every Token of the same type points at the same TokenType. That makes "are these two
    /// tokens the same kind?" a simple reference comparison (tokenA.Type == tokenB.Type).
    ///
    /// [CreateAssetMenu] adds an entry to the Project window's right-click menu:
    /// Create > Match3 > Token Type.
    /// </summary>
    [CreateAssetMenu(fileName = "TokenType", menuName = "Match3/Token Type")]
    public class TokenType : ScriptableObject
    {
        [Tooltip("Stable identifier for code and save data, e.g. \"circle\". Should not change once set.")]
        [SerializeField] private string id;
        [Tooltip("Human-readable name for UI.")]
        [SerializeField] private string displayName;
        [Tooltip("Sprite drawn for this token. The gem sprites (Art/Gems) are already colored, so leave Color white.")]
        [SerializeField] private Sprite sprite;
        [Tooltip("Tint multiplied onto the sprite. White leaves the sprite's own colors unchanged.")]
        [SerializeField] private Color color = Color.white;

        // Read-only properties: other code can read the data but only the Inspector edits it.
        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Sprite => sprite;
        public Color Color => color;
    }
}
