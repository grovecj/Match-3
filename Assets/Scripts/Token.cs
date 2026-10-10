using UnityEngine;

namespace Match3
{
    /// <summary>
    /// A single token (gem) on the board. It knows its <see cref="TokenType"/> and
    /// <see cref="PowerKind"/> (what it is) and the <see cref="TokenStyle"/> to draw it with
    /// (how it looks); game rules (matching, swapping) live elsewhere.
    /// </summary>
    // RequireComponent makes Unity add a SpriteRenderer automatically (and prevents removing it)
    // wherever this script is attached, so GetComponent below can't come back empty.
    [RequireComponent(typeof(SpriteRenderer))]
    public class Token : MonoBehaviour
    {
        [Tooltip("Type shown when placed in a scene. At runtime the Board assigns this via SetPiece.")]
        [SerializeField] private TokenType type;
        [Tooltip("Style used to draw the type. At runtime the Board assigns this via SetStyle.")]
        [SerializeField] private TokenStyle style;

        [Header("Pick-up")]
        [Tooltip("How big the token gets while held or selected, relative to its normal size.")]
        [SerializeField, Min(1f)] private float liftScale = 1.1f;
        [Tooltip("How long, in seconds, the token takes to grow when picked up (and shrink when put down).")]
        [SerializeField, Min(0.01f)] private float liftDuration = 0.08f;
        [Tooltip("How much a selected token pulses, as a fraction of its size.")]
        [SerializeField, Range(0f, 0.2f)] private float pulseAmount = 0.05f;
        [Tooltip("How many times per second a selected token pulses.")]
        [SerializeField, Min(0.1f)] private float pulseRate = 1.5f;
        [Tooltip("How much a hinted token pulses, as a fraction of its size. Bigger than the selected pulse, so it catches the eye.")]
        [SerializeField, Range(0f, 0.3f)] private float hintPulseAmount = 0.12f;

        /// <summary>How a token is being singled out, by the player or (for a hint) for them.</summary>
        public enum Highlight
        {
            /// <summary>Resting on the board at its normal size.</summary>
            None,
            /// <summary>Picked up: the pointer is pressed on it (and maybe dragging it).</summary>
            Held,
            /// <summary>Clicked, waiting for a click on a neighbor: picked up and gently pulsing.</summary>
            Selected,
            /// <summary>Part of a move the board is suggesting: pulsing in place, not picked up.</summary>
            Hint,
        }

        // Not a [SerializeField]: nothing is drawn for a power yet, so there's nothing to set up
        // in the Inspector. The Board assigns it, with the type, via SetPiece.
        private PowerKind power = PowerKind.None;
        private SpriteRenderer spriteRenderer;
        private Highlight highlight = Highlight.None;
        // 0 = resting, 1 = fully picked up. Eased toward the highlight's target every frame.
        private float lift;
        // How long this token has been selected, which drives the pulse.
        private float pulseTime;
        // The token's size when resting, remembered at pick-up so it can go back to it exactly.
        private Vector3 restScale;
        private int restSortingOrder;
        // Set when the highlight changes, so Update redraws at least once even if lift is
        // already at its target (e.g. selected -> held has to drop the pulse).
        private bool highlightChanged;

        public TokenType Type => type;

        /// <summary>The power this token carries. It doesn't change how the token looks yet.</summary>
        public PowerKind Power => power;

        public Highlight CurrentHighlight => highlight;

        private void Awake()
        {
            ApplyVisuals();
            restSortingOrder = spriteRenderer.sortingOrder;
        }

        /// <summary>
        /// Picks the token up (<see cref="Highlight.Held"/> or <see cref="Highlight.Selected"/>),
        /// makes it pulse where it is (<see cref="Highlight.Hint"/>), or puts it back to normal
        /// (<see cref="Highlight.None"/>). Being picked up animates over
        /// <see cref="liftDuration"/> rather than jumping.
        /// </summary>
        public void SetHighlight(Highlight newHighlight)
        {
            if (newHighlight == highlight)
            {
                return;
            }

            // A hint pulses around the resting size, so the token is probably mid-pulse right
            // now. Snap it back first, or the check below would remember the swollen size as
            // the resting one.
            if (highlight == Highlight.Hint)
            {
                transform.localScale = restScale;
            }

            // Only remember the resting size when starting from rest. A token picked up again
            // while it's still shrinking back is part-way lifted, so its scale isn't the rest scale.
            if (highlight == Highlight.None && lift == 0f)
            {
                restScale = transform.localScale;
            }
            if (newHighlight == Highlight.Selected || newHighlight == Highlight.Hint)
            {
                pulseTime = 0f;
            }
            if (newHighlight != Highlight.None)
            {
                // Draw above the neighbors, so a dragged token slides over the one it's swapping
                // with instead of under it. Higher sorting orders are drawn on top.
                spriteRenderer.sortingOrder = restSortingOrder + 1;
            }
            highlight = newHighlight;
            highlightChanged = true;
        }

        private void Update()
        {
            // Only a token the player has picked up is lifted; a hinted one stays on the board.
            float target = highlight == Highlight.Held || highlight == Highlight.Selected ? 1f : 0f;
            bool pulsing = highlight == Highlight.Selected || highlight == Highlight.Hint;
            // Nothing to animate: fully at rest, or fully lifted and not pulsing. Returning early
            // matters because other code (the Board's pop) sets the scale of resting tokens, and
            // we mustn't overwrite it.
            if (lift == target && !pulsing && !highlightChanged)
            {
                return;
            }
            highlightChanged = false;

            // Time.deltaTime is scaled time, so the lift and pulse freeze while the game is paused.
            // MoveTowards changes lift by at most the given step, and never past the target.
            lift = Mathf.MoveTowards(lift, target, Time.deltaTime / liftDuration);
            float size = Mathf.Lerp(1f, liftScale, Easing.OutCubic(lift));

            if (pulsing)
            {
                pulseTime += Time.deltaTime;
                // (1 - cos) / 2 swings smoothly between 0 and 1, starting at 0, so the pulse
                // begins without a jump. 2 * PI radians is one full swing.
                float wave = (1f - Mathf.Cos(pulseTime * pulseRate * 2f * Mathf.PI)) / 2f;
                size += (highlight == Highlight.Hint ? hintPulseAmount : pulseAmount) * wave;
            }

            transform.localScale = restScale * size;

            // Back down: stop drawing on top only now, so the token doesn't dip under its
            // neighbor while it's still bigger than them.
            if (lift == 0f && highlight == Highlight.None)
            {
                spriteRenderer.sortingOrder = restSortingOrder;
            }
        }

        // Runs in the Editor when a value changes in the Inspector, so the token updates
        // immediately when you pick a different Type or Style, without pressing Play.
        private void OnValidate()
        {
            ApplyVisuals();
        }

        /// <summary>
        /// Makes this token show <paramref name="piece"/>: takes its colour as the token's type
        /// and its power, and updates the visuals to match.
        /// </summary>
        public void SetPiece(Piece piece)
        {
            type = piece.Colour;
            power = piece.Power;
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
