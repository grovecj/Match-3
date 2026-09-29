using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// A Settings option for one <see cref="Match3.TokenStyle"/>: its name plus a row showing
    /// how each token type looks in that style.
    /// </summary>
    public class TokenStyleOption : SettingsOption
    {
        [Tooltip("Parent for the preview icons. Give it a Horizontal Layout Group so they line up in a row.")]
        [SerializeField] private RectTransform previewRow;
        [Tooltip("Size of each preview icon, in canvas pixels.")]
        [SerializeField, Min(1f)] private float iconSize = 56f;

        public TokenStyle TokenStyle { get; private set; }

        /// <summary>Fills in this option for <paramref name="style"/>, previewing each of <paramref name="types"/>.</summary>
        public void Setup(TokenStyle style, IReadOnlyList<TokenType> types)
        {
            TokenStyle = style;
            SetLabel(style.DisplayName);

            if (previewRow == null || types == null)
            {
                return;
            }

            // Build one Image per token type from code. `new GameObject(name, components...)`
            // creates an object with those components already attached; UI objects need a
            // RectTransform instead of a plain Transform, so we list it explicitly.
            foreach (TokenType type in types)
            {
                if (!style.TryGetVisual(type, out Sprite sprite, out Color tint) || sprite == null)
                {
                    continue;
                }

                var icon = new GameObject(type.name, typeof(RectTransform), typeof(Image));
                // false = keep the local layout values instead of the world position, which is
                // what you want when parenting UI.
                icon.transform.SetParent(previewRow, false);

                Image image = icon.GetComponent<Image>();
                image.sprite = sprite;
                image.color = tint;
                image.preserveAspect = true;
                // Preview icons are decoration: let clicks pass through to the option's button.
                image.raycastTarget = false;

                // A LayoutElement tells the parent layout group how big this child wants to be.
                var layout = icon.AddComponent<LayoutElement>();
                layout.preferredWidth = iconSize;
                layout.preferredHeight = iconSize;
            }
        }
    }
}
