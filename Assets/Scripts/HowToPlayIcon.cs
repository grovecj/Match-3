using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// One little board cell in a How to play picture: a square in the board's colours, the
    /// gem on it in the player's token style, and (for a Flame or Star) the same overlay the
    /// board draws on top. An empty cell (no gem) is just the square, which is how the L shape
    /// shows the cells around it.
    ///
    /// It stores which gem to show, not what it looks like: <see cref="Show"/> looks that up in
    /// the current style and theme, so the pictures always match the board with no art of
    /// their own. Set up by Match3 > Build How To Play.
    /// </summary>
    public class HowToPlayIcon : MonoBehaviour
    {
        [Tooltip("The square behind the gem, coloured like a board cell. Optional.")]
        [SerializeField] private Image cell;
        [Tooltip("Use the board theme's light cell colour (otherwise the dark one), so neighbouring cells make a checkerboard.")]
        [SerializeField] private bool lightCell = true;
        [Tooltip("The gem. Leave empty for an empty cell.")]
        [SerializeField] private Image gem;
        [Tooltip("Drawn over the gem for a Flame (the glow) or a Star (the sparkle).")]
        [SerializeField] private Image overlay;
        [Tooltip("Which of the Board's token types to show, by position in its list, so a picture can use one colour throughout.")]
        [SerializeField, Min(0)] private int tokenIndex;
        [SerializeField] private PowerKind power;

        public PowerKind Power => power;
        public bool HasGem => gem != null;

        /// <summary>Draws this cell in <paramref name="style"/> and <paramref name="theme"/> (either may be null).</summary>
        public void Show(TokenStyle style, BoardTheme theme, IReadOnlyList<TokenType> types)
        {
            if (cell != null && theme != null)
            {
                cell.color = lightCell ? theme.LightCellColor : theme.DarkCellColor;
            }

            if (gem != null)
            {
                // The same rules as Token.ApplyVisuals: a Hypercube has its own sprite in every
                // style; anything else is drawn the way the style draws its colour.
                Sprite sprite = null;
                Color tint = Color.white;
                if (power == PowerKind.Hypercube)
                {
                    sprite = HypercubeSprite.Sprite;
                }
                else if (style != null && types != null && types.Count > 0)
                {
                    // % wraps the index round, so a picture still shows something if a board
                    // ever has fewer colours than the picture expects.
                    style.TryGetVisual(types[tokenIndex % types.Count], out sprite, out tint);
                }
                gem.sprite = sprite;
                gem.color = tint;
                // An Image with no sprite draws a plain white square; hide it instead.
                gem.enabled = sprite != null;
            }

            if (overlay != null)
            {
                // These sprites are built in code the first time they're asked for, so they
                // can't be saved in the prefab: they're filled in here, like the gems.
                bool isStar = power == PowerKind.Star;
                overlay.enabled = power == PowerKind.Flame || isStar;
                overlay.sprite = isStar ? StarSparkle.SparkleSprite : FlameGlow.GlowSprite;
                overlay.color = isStar ? StarSparkle.RestColor : FlameGlow.RestColor;
            }
        }
    }
}
