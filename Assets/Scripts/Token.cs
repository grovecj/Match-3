using UnityEngine;

namespace Match3
{
    /// <summary>
    /// A single token (gem) on the board. It only knows its <see cref="TokenType"/> and how to
    /// display it; game rules (matching, swapping) live elsewhere.
    /// </summary>
    // RequireComponent makes Unity add a SpriteRenderer automatically (and prevents removing it)
    // wherever this script is attached, so GetComponent below can't come back empty.
    [RequireComponent(typeof(SpriteRenderer))]
    public class Token : MonoBehaviour
    {
        [Tooltip("Type shown when placed in a scene. At runtime the Board assigns this via SetType.")]
        [SerializeField] private TokenType type;

        private SpriteRenderer spriteRenderer;

        public TokenType Type => type;

        private void Awake()
        {
            ApplyType();
        }

        // Runs in the Editor when a value changes in the Inspector, so the token updates
        // immediately when you pick a different Type, without pressing Play.
        private void OnValidate()
        {
            ApplyType();
        }

        /// <summary>Changes this token's type and updates its visuals to match.</summary>
        public void SetType(TokenType newType)
        {
            type = newType;
            ApplyType();
        }

        private void ApplyType()
        {
            // Cache the component the first time we need it; GetComponent is cheap but not free.
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (type == null)
            {
                spriteRenderer.sprite = null;
                return;
            }

            spriteRenderer.sprite = type.Sprite;
            spriteRenderer.color = type.Color;
        }
    }
}
