using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The colors of everything around the tokens: the checkerboard cells and the background
    /// behind the board. Create one with Create > Match3 > Board Theme.
    /// </summary>
    [CreateAssetMenu(fileName = "BoardTheme", menuName = "Match3/Board Theme")]
    public class BoardTheme : ScriptableObject
    {
        [Tooltip("Stable identifier for code and save data, e.g. \"midnight\". Should not change once set.")]
        [SerializeField] private string id;
        [Tooltip("Human-readable name for UI.")]
        [SerializeField] private string displayName;
        [SerializeField] private Color lightCellColor = new Color(0.28f, 0.28f, 0.36f);
        [SerializeField] private Color darkCellColor = new Color(0.20f, 0.20f, 0.27f);
        [Tooltip("Color the camera clears the screen to, i.e. everything behind the board.")]
        [SerializeField] private Color backgroundColor = new Color(0.19f, 0.30f, 0.47f);

        public string Id => id;
        public string DisplayName => displayName;
        public Color LightCellColor => lightCellColor;
        public Color DarkCellColor => darkCellColor;
        public Color BackgroundColor => backgroundColor;
    }
}
