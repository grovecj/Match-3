using UnityEngine;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// A Settings option for one <see cref="Match3.BoardTheme"/>: its name plus a small
    /// checkerboard swatch drawn in the theme's cell colors on its background color.
    /// </summary>
    public class BoardThemeOption : SettingsOption
    {
        [Tooltip("Image behind the checkerboard, painted in the theme's background color.")]
        [SerializeField] private Image swatchBackground;
        [Tooltip("The checkerboard squares, listed row by row from the top-left (the order a Grid Layout Group places them).")]
        [SerializeField] private Image[] swatchCells;
        [Tooltip("How many squares are in each row of the swatch.")]
        [SerializeField, Min(1)] private int swatchColumns = 3;

        public BoardTheme BoardTheme { get; private set; }

        /// <summary>Fills in this option for <paramref name="theme"/>.</summary>
        public void Setup(BoardTheme theme)
        {
            BoardTheme = theme;
            SetLabel(theme.DisplayName);

            if (swatchBackground != null)
            {
                swatchBackground.color = theme.BackgroundColor;
            }
            if (swatchCells == null)
            {
                return;
            }

            for (int i = 0; i < swatchCells.Length; i++)
            {
                // Turn the list index back into a column and row, then alternate colors the
                // same way the Board does, so the swatch looks like a corner of the real board.
                int column = i % swatchColumns;
                int row = i / swatchColumns;
                swatchCells[i].color = (column + row) % 2 == 0 ? theme.DarkCellColor : theme.LightCellColor;
            }
        }
    }
}
