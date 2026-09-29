using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Identity of one kind of token (gem), e.g. "circle". It deliberately says nothing about
    /// how the token looks: that's a <see cref="TokenStyle"/>'s job, so the same game can be
    /// drawn with different art without touching any game rules.
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

        // Read-only properties: other code can read the data but only the Inspector edits it.
        public string Id => id;
        public string DisplayName => displayName;
    }
}
