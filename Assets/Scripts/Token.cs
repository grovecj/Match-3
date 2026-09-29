using UnityEngine;

namespace Match3
{
    /// <summary>
    /// A single token (gem) on the board. It knows its <see cref="TokenType"/> (what it is) and
    /// the <see cref="TokenStyle"/> to draw it with (how it looks); game rules (matching,
    /// swapping) live elsewhere.
    /// </summary>
    // RequireComponent makes Unity add a SpriteRenderer automatically (and prevents removing it)
    // wherever this script is attached, so GetComponent below can't come back empty.
    [RequireComponent(typeof(SpriteRenderer))]
    public class Token : MonoBehaviour
    {
        [Tooltip("Type shown when placed in a scene. At runtime the Board assigns this via SetType.")]
        [SerializeField] private TokenType type;
        [Tooltip("Style used to draw the type. At runtime the Board assigns this via SetStyle.")]
        [SerializeField] private TokenStyle style;

        private SpriteRenderer spriteRenderer;

        public TokenType Type => type;

        private void Awake()
        {
            ApplyVisuals();
        }

        // Runs in the Editor when a value changes in the Inspector, so the token updates
        // immediately when you pick a different Type or Style, without pressing Play.
        private void OnValidate()
        {
            ApplyVisuals();
        }

        /// <summary>Changes this token's type and updates its visuals to match.</summary>
        public void SetType(TokenType newType)
        {
            type = newType;
            ApplyVisuals();
        }

        /// <summary>Redraws this token with <paramref name="newStyle"/>. Its type is unchanged.</summary>
        public void SetStyle(TokenStyle newStyle)
        {
            style = newStyle;
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            // Cache the component the first time we need it; GetComponent is cheap but not free.
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            // Without both a type and a style there's nothing sensible to draw.
            if (type == null || style == null)
            {
                spriteRenderer.sprite = null;
                return;
            }

            // If the style has no entry for this type the sprite comes back null, so the token is
            // invisible rather than wrong. (The Board logs an error naming the missing types.)
            style.TryGetVisual(type, out Sprite sprite, out Color tint);
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = tint;
        }
    }
}
