using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The flickering ring of light drawn over a Flame gem. It sits on a child GameObject of
    /// the <see cref="Token"/>, with its own SpriteRenderer, so it is an overlay: the token's
    /// own sprite is untouched underneath. That's what lets one effect work with every token
    /// style (Gems, Flat, Glass) without any of them needing a "flame" version of their art.
    ///
    /// The token creates this the first time it's given a Flame (see <c>Token.SetPiece</c>).
    /// Being a child, it follows the token for free: it falls with it, grows with it when the
    /// token is picked up, and shrinks away with it when the token pops.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FlameGlow : MonoBehaviour
    {
        /// <summary>How wide the glow's sprite is, in cells. Wider than a cell so the ring sits around the gem's edge.</summary>
        public const float SizeInCells = 1.25f;

        // The glow's texture is this many pixels square. It's a soft blur, so it doesn't need many.
        private const int TextureSize = 64;

        // The shape of the glow, as distances from the middle of the sprite (0) to its edge (1).
        // The ring is brightest at RingRadius and fades over about RingWidth either side.
        private const float RingRadius = 0.72f;
        private const float RingWidth = 0.12f;
        // Inside the ring the glow is only faint, so the gem's own colour still reads clearly:
        // the player has to be able to tell what a Flame matches with.
        private const float CoreAlpha = 0.18f;

        // A warm yellow-orange. The sprite itself is white; a SpriteRenderer's color multiplies
        // it, which is how one white texture can be tinted any colour.
        private static readonly Color GlowColor = new Color(1f, 0.72f, 0.25f);
        private const float MinAlpha = 0.45f;
        private const float MaxAlpha = 0.95f;
        // The ring also swells a little as it brightens.
        private const float MinScale = 0.96f;
        private const float MaxScale = 1.08f;
        // How fast the flicker runs through its noise, in "noise units" per second.
        private const float FlickerSpeed = 3f;

        // `static` means one copy shared by every FlameGlow, rather than one each: every Flame
        // on the board draws the same sprite, so it only has to be built once.
        private static Sprite sharedSprite;

        private SpriteRenderer spriteRenderer;
        // Where along the noise this glow reads from, so two Flames don't flicker in step.
        private float noiseOffset;

        /// <summary>The glow's sprite: a soft white ring, built in code the first time it's asked for.</summary>
        public static Sprite GlowSprite
        {
            get
            {
                // Unity's == treats a destroyed object as null, so this also rebuilds the sprite
                // if something destroyed it (the field would otherwise still point at the corpse).
                if (sharedSprite == null)
                {
                    sharedSprite = BuildSprite();
                }
                return sharedSprite;
            }
        }

        /// <summary>The colour and opacity the glow has at its dimmest, before it starts flickering.</summary>
        public static Color RestColor => new Color(GlowColor.r, GlowColor.g, GlowColor.b, MinAlpha);

        /// <summary>
        /// How opaque the glow's sprite is at <paramref name="distance"/> from its middle, where
        /// 0 is the middle and 1 is the edge: faint in the middle, brightest on the ring, and
        /// fully transparent from the edge outward.
        /// </summary>
        // `public static` so the Edit Mode tests can check the shape without drawing anything.
        public static float AlphaAt(float distance)
        {
            if (distance >= 1f)
            {
                return 0f;
            }

            // A "bell curve" centred on the ring: 1 exactly on it, falling away smoothly on both
            // sides. Dividing by RingWidth sets how quickly it falls.
            float offset = (distance - RingRadius) / RingWidth;
            float ring = Mathf.Exp(-offset * offset);
            float core = distance < RingRadius ? CoreAlpha : 0f;
            return Mathf.Max(ring, core);
        }

        /// <summary>Paints the ring into a new texture and wraps it in a Sprite.</summary>
        // Making the image in code means there's no art file to draw, import or keep in step
        // with the token styles. A Texture2D is just a grid of pixels we can fill in ourselves.
        private static Sprite BuildSprite()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.name = "Flame Glow";
            // Clamp: don't repeat the image at its edges. Bilinear: blend between pixels, so a
            // 64-pixel blur still looks smooth when drawn bigger.
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            // It lives only in memory: never save it into a scene, and don't unload it when a
            // new scene loads.
            texture.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color[TextureSize * TextureSize];
            float middle = (TextureSize - 1) / 2f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    // How far this pixel is from the middle, as a fraction of the way to the edge.
                    float distance = new Vector2(x - middle, y - middle).magnitude / (TextureSize / 2f);
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, AlphaAt(distance));
                }
            }
            texture.SetPixels(pixels);
            // SetPixels only changes our copy; Apply sends it to the graphics card.
            texture.Apply();

            // Pixels per unit decides how big the sprite is in the world: 64 pixels at 51.2 per
            // unit is 1.25 units. The token is scaled to the cell size, so that's 1.25 cells.
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize / SizeInCells);
            sprite.name = "Flame Glow";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            // UnityEngine.Random.value is a random float from 0 to 1.
            noiseOffset = Random.value * 100f;
        }

        private void Update()
        {
            // Perlin noise is "smooth randomness": nearby inputs give nearby outputs, so walking
            // along it over time gives a value that wanders between 0 and 1 without jumping.
            // That's a flicker, where a sine wave would be an even, mechanical pulse.
            // Time.time is scaled time, so the flicker freezes while the game is paused.
            float flicker = Mathf.PerlinNoise(Time.time * FlickerSpeed, noiseOffset);

            Color color = GlowColor;
            color.a = Mathf.Lerp(MinAlpha, MaxAlpha, flicker);
            spriteRenderer.color = color;
            // localScale is relative to the parent (the token), so 1 means "the token's size".
            transform.localScale = Vector3.one * Mathf.Lerp(MinScale, MaxScale, flicker);
        }
    }
}
