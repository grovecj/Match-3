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

        // Sorting order decides what's drawn on top: higher numbers cover lower ones. A power
        // overlay is one above its own token, and a picked-up token is two above a resting one,
        // which puts it over its neighbors and their overlays too.
        private const int OverlayOrderOffset = 1;
        private const int LiftedOrderOffset = 2;

        // Not a [SerializeField]: the power's overlay is built in code, so there's nothing to
        // set up in the Inspector. The Board assigns it, with the type, via SetPiece.
        private PowerKind power = PowerKind.None;
        private SpriteRenderer spriteRenderer;
        // What's drawn over this token while it's a power gem. It's on a child GameObject that
        // is only created the first time it's needed, so ordinary tokens never have one.
        private SpriteRenderer powerOverlay;
        // The two scripts that animate the overlay, one per look. Both sit on the overlay's
        // GameObject, and only the one for the token's current power is switched on.
        private FlameGlow flameGlow;
        private StarSparkle starSparkle;
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

        /// <summary>
        /// The power this token carries. A Flame shows it with a glow and a Star with a sparkle
        /// (see <see cref="ShowsPowerOverlay"/>); a Hypercube has a sprite of its own instead
        /// (see <see cref="HypercubeSprite"/>), and its <see cref="Type"/> is null.
        /// </summary>
        public PowerKind Power => power;

        /// <summary>
        /// True while this token is drawing a power overlay (the Flame's glow or the Star's
        /// sparkle) on top of its ordinary sprite.
        /// </summary>
        public bool ShowsPowerOverlay => powerOverlay != null && powerOverlay.gameObject.activeSelf;

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
                SetSortingOrder(restSortingOrder + LiftedOrderOffset);
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
                SetSortingOrder(restSortingOrder);
            }
        }

        /// <summary>Sets how far forward this token is drawn, keeping its overlay just in front of it.</summary>
        private void SetSortingOrder(int order)
        {
            spriteRenderer.sortingOrder = order;
            if (powerOverlay != null)
            {
                powerOverlay.sortingOrder = order + OverlayOrderOffset;
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

            // Before the early returns below: the overlay doesn't depend on the type or style.
            ApplyPowerOverlay();

            // A Hypercube has no colour, so no style has a sprite for it. It brings its own, the
            // same in every style, and that's the whole of its look (no tint, no overlay).
            if (power == PowerKind.Hypercube)
            {
                spriteRenderer.sprite = HypercubeSprite.Sprite;
                spriteRenderer.color = Color.white;
                return;
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

        /// <summary>
        /// Shows the overlay for this token's power (the Flame's glow or the Star's sparkle) and
        /// hides it if the token has no power to show, creating the overlay's GameObject the
        /// first time it's needed.
        /// </summary>
        // An "overlay" is a second picture drawn on top of the first. The token's own sprite
        // (which the style picks) isn't changed at all, so the overlay never has to know which
        // style is in use, and switching style can't lose it.
        private void ApplyPowerOverlay()
        {
            // A Hypercube has no overlay: its own sprite is its look (see ApplyVisuals).
            bool show = power == PowerKind.Flame || power == PowerKind.Star;
            if (powerOverlay == null)
            {
                if (!show)
                {
                    return;
                }

                // A child GameObject moves, scales and is destroyed with its parent, so the
                // overlay stays on the token while it falls, swaps, is picked up, or pops.
                // SetParent's `false` means "keep my local position (0, 0, 0)" rather than
                // "stay where I am in the world", which puts it exactly on the token.
                var overlayObject = new GameObject("Power Overlay");
                overlayObject.transform.SetParent(transform, false);
                powerOverlay = overlayObject.AddComponent<SpriteRenderer>();
                // One object carries both looks. That's simpler than a child per power, and a
                // token that changes from one power to another has nothing to create or destroy.
                flameGlow = overlayObject.AddComponent<FlameGlow>();
                starSparkle = overlayObject.AddComponent<StarSparkle>();
                // Draw it the same way, and in the same layer, as the token itself.
                powerOverlay.sharedMaterial = spriteRenderer.sharedMaterial;
                powerOverlay.sortingLayerID = spriteRenderer.sortingLayerID;
                powerOverlay.sortingOrder = spriteRenderer.sortingOrder + OverlayOrderOffset;
            }

            if (show)
            {
                bool isStar = power == PowerKind.Star;
                // A disabled script's Update doesn't run, so only one of the two is ever
                // animating the overlay.
                flameGlow.enabled = !isStar;
                starSparkle.enabled = isStar;
                // Start from the new look's resting state, so nothing of the old look (its
                // size, say) is left over until the next Update.
                powerOverlay.sprite = isStar ? StarSparkle.SparkleSprite : FlameGlow.GlowSprite;
                powerOverlay.color = isStar ? StarSparkle.RestColor : FlameGlow.RestColor;
                powerOverlay.transform.localScale = Vector3.one;
            }

            // SetActive(false) hides a GameObject and stops its scripts, without destroying it,
            // so a token that stops being a power gem can become one again later for free.
            powerOverlay.gameObject.SetActive(show);
        }
    }
}
