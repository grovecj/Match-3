using UnityEngine;

namespace Match3
{
    /// <summary>
    /// One bolt from a Hypercube to a gem it's destroying: a bar of light between the two that
    /// appears all at once and then fades. The <see cref="Board"/> makes one per target gem and
    /// gives each a later <c>delay</c> than the one before, so the gems are picked off one after
    /// another instead of all vanishing together. Like <see cref="StarBeam"/>, it runs itself
    /// and destroys its own GameObject when done.
    ///
    /// It only draws. Which gems go is the model's business
    /// (<see cref="BoardModel.ActivateHypercube"/>), and the Board pops each one at the same
    /// delay its bolt is given here (<see cref="Board.ZapDelay"/>). That shared delay is what
    /// keeps the bolts and the pops in step.
    ///
    /// A placeholder, like the other blasts: proper effects are a later issue.
    /// </summary>
    // RequireComponent adds the SpriteRenderer along with this script, so AddComponent on a
    // bare GameObject gives a bolt that's ready to draw.
    [RequireComponent(typeof(SpriteRenderer))]
    public class HypercubeZap : MonoBehaviour
    {
        /// <summary>How thick the bolt is, in cells. Thinner than a Star's beam: there can be a dozen at once.</summary>
        public const float Thickness = 0.14f;

        /// <summary>How long, in seconds, a bolt takes to fade out after it appears.</summary>
        public const float FadeDuration = 0.18f;

        private SpriteRenderer spriteRenderer;
        private Color color = Color.white;
        // How long to stay invisible before appearing.
        private float delay;
        private float elapsed;

        /// <summary>
        /// Sets up a bolt from <paramref name="from"/> (the Hypercube) to <paramref name="to"/>
        /// (the gem), both world positions. It stays invisible for <paramref name="delay"/>
        /// seconds, then flashes and fades.
        /// </summary>
        /// <param name="color">The bolt's colour.</param>
        /// <param name="cellSize">The size of a board cell, in world units.</param>
        /// <param name="drawLike">A renderer to copy the material and sorting layer from, so the bolt is drawn the way tokens are.</param>
        /// <param name="sortingOrder">Where in that layer to draw. Higher is further forward.</param>
        public void Play(Vector3 from, Vector3 to, Color color, float delay, float cellSize,
            SpriteRenderer drawLike, int sortingOrder)
        {
            this.color = color;
            this.delay = delay;
            elapsed = 0f;

            spriteRenderer = GetComponent<SpriteRenderer>();
            // The same bar the Star's beam is made of: 1 unit long and 1 unit thick, with its
            // pivot at one end. So putting it on the cube, turning it to face the gem and
            // stretching it to the gem's distance draws a line between the two.
            spriteRenderer.sprite = StarBeam.BeamSprite;
            if (drawLike != null)
            {
                spriteRenderer.sharedMaterial = drawLike.sharedMaterial;
                spriteRenderer.sortingLayerID = drawLike.sortingLayerID;
            }
            spriteRenderer.sortingOrder = sortingOrder;

            Vector3 toTarget = to - from;
            transform.position = from;
            // Atan2 turns "this far across, this far up" into the angle of that direction.
            // It answers in radians; Rad2Deg converts to the degrees that Euler angles want.
            float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = new Vector3(toTarget.magnitude, Thickness * cellSize, 1f);

            Redraw();
        }

        /// <summary>
        /// How opaque a bolt is <paramref name="elapsed"/> seconds after <see cref="Play"/>, when
        /// it was told to wait <paramref name="delay"/>: invisible until its turn comes, fully
        /// lit at that moment, then fading to nothing over <see cref="FadeDuration"/>.
        /// </summary>
        // `public static` so the Edit Mode tests can check it without a scene.
        public static float Opacity(float elapsed, float delay)
        {
            if (elapsed < delay)
            {
                return 0f;
            }
            return 1f - Mathf.Clamp01((elapsed - delay) / FadeDuration);
        }

        private void Update()
        {
            // Time.deltaTime is scaled time, so the bolt freezes while the game is paused.
            elapsed += Time.deltaTime;
            Redraw();

            if (elapsed >= delay + FadeDuration)
            {
                Destroy(gameObject);
            }
        }

        private void Redraw()
        {
            // Play hasn't run yet: there's nothing to draw with.
            if (spriteRenderer == null)
            {
                return;
            }
            Color faded = color;
            faded.a *= Opacity(elapsed, delay);
            spriteRenderer.color = faded;
        }
    }
}
