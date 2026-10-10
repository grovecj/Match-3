using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The slow four-pointed sparkle drawn over a Star gem. Like <see cref="FlameGlow"/> it's an
    /// overlay on a child GameObject of the <see cref="Token"/>, so it works with every token
    /// style and follows the token around for free.
    ///
    /// It's made to look nothing like the Flame's glow, so the two can be told apart at a
    /// glance: the Flame is a warm ring around the gem that flickers, the Star is a cool white
    /// cross through the gem that swells and fades slowly and evenly. The cross is also a hint
    /// at what the gem does: it clears its row and its column.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class StarSparkle : MonoBehaviour
    {
        /// <summary>How wide the sparkle's sprite is, in cells. Wider than a cell so its points poke out past the gem.</summary>
        public const float SizeInCells = 1.5f;

        // The sparkle's texture is this many pixels square.
        private const int TextureSize = 64;

        // How thick each arm of the cross is where it leaves the middle, as a fraction of the
        // arm's length. The arm narrows to nothing at its tip, which is what makes it a point.
        // Thin, so the gem's own colour still shows on either side of it.
        private const float ArmWidth = 0.16f;

        // A cool blue-white, well away from the Flame's orange.
        private static readonly Color SparkleColor = new Color(0.85f, 0.95f, 1f);
        private const float MinAlpha = 0.5f;
        private const float MaxAlpha = 1f;
        private const float MinScale = 0.8f;
        private const float MaxScale = 1.05f;
        // How many times per second the sparkle swells and fades. Well under 1: it's slow.
        private const float PulseRate = 0.6f;

        // One sprite shared by every StarSparkle, built the first time it's needed.
        private static Sprite sharedSprite;

        private SpriteRenderer spriteRenderer;
        // Where in its pulse this sparkle starts, so two Stars don't pulse in step.
        private float phase;

        /// <summary>The sparkle's sprite: a soft white four-pointed star, built in code the first time it's asked for.</summary>
        public static Sprite SparkleSprite
        {
            get
            {
                if (sharedSprite == null)
                {
                    sharedSprite = BuildSprite();
                }
                return sharedSprite;
            }
        }

        /// <summary>The colour and opacity the sparkle has at its dimmest, before it starts pulsing.</summary>
        public static Color RestColor => new Color(SparkleColor.r, SparkleColor.g, SparkleColor.b, MinAlpha);

        /// <summary>
        /// How opaque the sparkle's sprite is at <paramref name="offset"/> from its middle, where
        /// (0, 0) is the middle and 1 in x or y is the sprite's edge: solid in the middle,
        /// fading along the two axes to nothing at the edge, and empty on the diagonals.
        /// </summary>
        // `public static` so the Edit Mode tests can check the shape without drawing anything.
        public static float AlphaAt(Vector2 offset)
        {
            float x = Mathf.Abs(offset.x);
            float y = Mathf.Abs(offset.y);
            // Nothing at all on or past the sprite's edge, or it would show as a faint square.
            if (x >= 1f || y >= 1f)
            {
                return 0f;
            }

            // The cross is two arms, one lying along each axis. A pixel takes whichever arm is
            // brighter there, so the two don't add up to a blob where they cross.
            return Mathf.Max(ArmAlpha(x, y), ArmAlpha(y, x));
        }

        /// <summary>
        /// One arm's opacity at a point <paramref name="along"/> the arm (0 = the middle of the
        /// sprite, 1 = the tip) and <paramref name="across"/> it (0 = on the arm's centre line).
        /// </summary>
        private static float ArmAlpha(float along, float across)
        {
            // (1 - along) is 1 in the middle and 0 at the tip. Using it for both the width and
            // the brightness makes the arm get thinner and fainter together as it goes out.
            float taper = 1f - along;
            // The same bell curve as the Flame's ring, but measured across the arm.
            float distance = across / (ArmWidth * taper);
            return Mathf.Exp(-distance * distance) * taper;
        }

        /// <summary>Paints the star into a new texture and wraps it in a Sprite.</summary>
        // The same recipe as FlameGlow.BuildSprite: see the comments there.
        private static Sprite BuildSprite()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.name = "Star Sparkle";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color[TextureSize * TextureSize];
            float middle = (TextureSize - 1) / 2f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    // Where this pixel is, as a fraction of the way from the middle to the edge.
                    var offset = new Vector2(x - middle, y - middle) / (TextureSize / 2f);
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, AlphaAt(offset));
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize / SizeInCells);
            sprite.name = "Star Sparkle";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            phase = Random.value;
        }

        private void Update()
        {
            // A cosine wave, unlike the Flame's noise, repeats exactly: an even, calm pulse.
            // (1 - cos) / 2 swings smoothly between 0 and 1, and 2 * PI radians is one full swing.
            // Time.time is scaled time, so the pulse freezes while the game is paused.
            float pulse = (1f - Mathf.Cos((Time.time * PulseRate + phase) * 2f * Mathf.PI)) / 2f;

            Color color = SparkleColor;
            color.a = Mathf.Lerp(MinAlpha, MaxAlpha, pulse);
            spriteRenderer.color = color;
            transform.localScale = Vector3.one * Mathf.Lerp(MinScale, MaxScale, pulse);
        }
    }
}
