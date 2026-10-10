using UnityEngine;

namespace Match3
{
    /// <summary>
    /// One bolt from a Hypercube to a gem it's destroying: a jagged arc of light between the
    /// two that crackles (its kinks jump about a few times while it's lit) and then fades.
    /// <see cref="PowerGemEffects"/> makes one per target gem and gives each the delay the
    /// <see cref="Board"/> pops that gem at (<see cref="Board.ZapDelay"/>), so the gems are
    /// picked off one after another, each the moment its bolt lands. It runs itself, and when
    /// it has faded it says so through <see cref="Finished"/> and stays alive (like a
    /// <see cref="ParticleEffect"/>), so it can go back in a pool and be played again: a
    /// Hypercube fires a dozen of these at once, each made of several GameObjects.
    ///
    /// It only draws. Which gems go is the model's business
    /// (<see cref="BoardModel.ActivateHypercube"/>).
    /// </summary>
    public class HypercubeZap : MonoBehaviour
    {
        /// <summary>How thick the bolt is, in cells. Thinner than a Star's beam: there can be a dozen at once.</summary>
        public const float Thickness = 0.12f;

        /// <summary>How long, in seconds, a bolt takes to fade out after it appears.</summary>
        public const float FadeDuration = 0.22f;

        /// <summary>How many straight pieces the bolt is drawn with. More pieces, more kinks.</summary>
        public const int SegmentCount = 6;

        /// <summary>How far, at most, a kink sits off the straight line, in cells.</summary>
        public const float Jitter = 0.22f;

        // How often, in seconds, the kinks move. A few changes during the fade make it flicker
        // like electricity; changing every frame would just look like noise.
        private const float FlickerInterval = 0.05f;

        // The bolt is drawn as a chain of short bars, each the same 1-by-1 bar the Star's beam
        // is made of, stretched from one kink to the next.
        private readonly Transform[] segments = new Transform[SegmentCount];
        private readonly SpriteRenderer[] segmentRenderers = new SpriteRenderer[SegmentCount];
        // The kinks' positions, kept and overwritten on every flicker rather than made anew.
        private readonly Vector3[] points = new Vector3[SegmentCount + 1];
        private Vector3 from;
        private Vector3 to;
        private float cellSize = 1f;
        private Color color = Color.white;
        // How long to stay invisible before appearing.
        private float delay;
        private float elapsed;
        private float nextFlicker;
        private System.Random random;

        /// <summary>Fired once each time the bolt has finished fading.</summary>
        public event System.Action<HypercubeZap> Finished;

        /// <summary>
        /// Sets up a bolt from <paramref name="from"/> (the Hypercube) to <paramref name="to"/>
        /// (the gem), both world positions. It stays invisible for <paramref name="delay"/>
        /// seconds, then flashes and fades.
        /// </summary>
        /// <param name="color">The bolt's colour.</param>
        /// <param name="cellSize">The size of a board cell, in world units.</param>
        /// <param name="drawLike">A renderer to copy the material and sorting layer from, so the bolt is drawn the way tokens are.</param>
        /// <param name="sortingOrder">Where in that layer to draw. Higher is further forward.</param>
        /// <param name="seed">Picks the bolt's kinks. Bolts with different seeds take different paths.</param>
        public void Play(Vector3 from, Vector3 to, Color color, float delay, float cellSize,
            SpriteRenderer drawLike, int sortingOrder, int seed)
        {
            this.from = from;
            this.to = to;
            this.color = color;
            this.delay = delay;
            this.cellSize = cellSize;
            elapsed = 0f;
            nextFlicker = delay;
            random = new System.Random(seed);

            for (int i = 0; i < SegmentCount; i++)
            {
                if (segments[i] == null)
                {
                    var segmentObject = new GameObject($"Segment {i}");
                    segments[i] = segmentObject.transform;
                    segments[i].SetParent(transform, false);
                    segmentRenderers[i] = segmentObject.AddComponent<SpriteRenderer>();
                    segmentRenderers[i].sprite = StarBeam.BeamSprite;
                }
                if (drawLike != null)
                {
                    segmentRenderers[i].sharedMaterial = drawLike.sharedMaterial;
                    segmentRenderers[i].sortingLayerID = drawLike.sortingLayerID;
                }
                segmentRenderers[i].sortingOrder = sortingOrder;
            }

            Reshape();
            Redraw();
        }

        /// <summary>
        /// The points a bolt from <paramref name="from"/> to <paramref name="to"/> passes
        /// through: <paramref name="segmentCount"/> + 1 of them, the first and last exactly on
        /// the two ends, the ones between spaced evenly along the line and pushed sideways by up
        /// to <paramref name="jitter"/> (in world units), each by its own random amount.
        /// </summary>
        // `public static` so the Edit Mode tests can check it without a scene.
        public static Vector3[] JaggedPoints(Vector3 from, Vector3 to, int segmentCount, float jitter, System.Random random)
        {
            var points = new Vector3[segmentCount + 1];
            FillJaggedPoints(points, from, to, jitter, random);
            return points;
        }

        /// <summary>
        /// The same as <see cref="JaggedPoints"/>, but writes the points into
        /// <paramref name="points"/> (one more than the number of pieces) instead of making a new
        /// array, so a bolt that flickers many times can keep reusing one.
        /// </summary>
        public static void FillJaggedPoints(Vector3[] points, Vector3 from, Vector3 to, float jitter, System.Random random)
        {
            int segmentCount = points.Length - 1;
            Vector3 along = to - from;
            // "Sideways" is the line turned a quarter turn: (x, y) becomes (-y, x). Normalized
            // makes it 1 unit long, so jitter is a distance.
            Vector3 sideways = new Vector3(-along.y, along.x, 0f).normalized;
            for (int i = 0; i <= segmentCount; i++)
            {
                float t = (float)i / segmentCount;
                points[i] = from + along * t;
                bool isEnd = i == 0 || i == segmentCount;
                if (!isEnd)
                {
                    // NextDouble is between 0 and 1, so this is between -jitter and +jitter.
                    float offset = (float)(random.NextDouble() * 2.0 - 1.0) * jitter;
                    points[i] += sideways * offset;
                }
            }
        }

        /// <summary>
        /// How opaque a bolt is <paramref name="elapsed"/> seconds after <see cref="Play"/>, when
        /// it was told to wait <paramref name="delay"/>: invisible until its turn comes, fully
        /// lit at that moment, then fading to nothing over <see cref="FadeDuration"/>.
        /// </summary>
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
            if (elapsed >= nextFlicker)
            {
                Reshape();
                nextFlicker += FlickerInterval;
            }
            Redraw();

            if (elapsed >= delay + FadeDuration)
            {
                // Not destroyed: whoever played it puts it away (see PowerGemEffects).
                Finished?.Invoke(this);
            }
        }

        /// <summary>Picks new kinks and lays the segments along them.</summary>
        private void Reshape()
        {
            FillJaggedPoints(points, from, to, Jitter * cellSize, random);
            for (int i = 0; i < SegmentCount; i++)
            {
                Vector3 start = points[i];
                Vector3 step = points[i + 1] - start;
                segments[i].position = start;
                // Atan2 turns "this far across, this far up" into the angle of that direction.
                // It answers in radians; Rad2Deg converts to the degrees that Euler angles want.
                float angle = Mathf.Atan2(step.y, step.x) * Mathf.Rad2Deg;
                segments[i].rotation = Quaternion.Euler(0f, 0f, angle);
                // The bar is 1 unit long with its pivot at one end, so its x scale is its length.
                segments[i].localScale = new Vector3(step.magnitude, Thickness * cellSize, 1f);
            }
        }

        private void Redraw()
        {
            Color faded = color;
            faded.a *= Opacity(elapsed, delay);
            foreach (SpriteRenderer segment in segmentRenderers)
            {
                if (segment != null)
                {
                    segment.color = faded;
                }
            }
        }
    }
}
