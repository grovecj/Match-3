using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The picture a Hypercube is drawn with. The Flame and the Star are overlays on top of a
    /// gem's ordinary sprite, because they still have a colour and the player needs to see it.
    /// A Hypercube has no colour, so no <see cref="TokenStyle"/> has a sprite for it: this is
    /// its whole look, and the same one is used whichever style is selected.
    ///
    /// It's drawn as a cube inside a cube (the usual way to draw a "hypercube"): two square
    /// outlines joined corner to corner, on a dark tile. The lines run through every hue
    /// around the middle, which is the hint at what it does: it isn't any one colour, it can
    /// take any of them.
    /// </summary>
    // A `static` class can't be created with `new` and holds only static members. That fits
    // here: there's nothing per-token to remember, just one shared sprite and how to paint it.
    // (FlameGlow and StarSparkle are MonoBehaviours because they also animate; this doesn't.)
    public static class HypercubeSprite
    {
        /// <summary>How wide the sprite is, in cells: exactly one, like an ordinary gem's.</summary>
        public const float SizeInCells = 1f;

        // The texture is this many pixels square. More than the glow's 64: these are thin,
        // sharp lines rather than a soft blur, and would look jagged with fewer.
        private const int TextureSize = 128;

        // The shape, as distances from the middle of the sprite (0) to its edge (1). Both
        // squares are measured to the middle of their outline.
        private const float OuterSize = 0.8f;
        private const float InnerSize = 0.38f;
        // How far either side of a line's centre it fades out over.
        private const float LineWidth = 0.07f;

        // The tile behind the lines: nearly black with a little blue, and not quite solid.
        private static readonly Color FillColor = new Color(0.08f, 0.08f, 0.16f);
        private const float FillAlpha = 0.9f;
        // How strong the lines' colours are (0 = white, 1 = as vivid as possible). Pale, so the
        // cube reads as "white with a rainbow sheen" rather than as one of the gem colours.
        private const float LineSaturation = 0.55f;

        // One sprite shared by every Hypercube, built the first time it's needed.
        private static Sprite sharedSprite;

        /// <summary>The Hypercube's sprite, built in code the first time it's asked for.</summary>
        public static Sprite Sprite
        {
            get
            {
                // Unity's == treats a destroyed object as null, so this also rebuilds the sprite
                // if something destroyed it.
                if (sharedSprite == null)
                {
                    sharedSprite = BuildSprite();
                }
                return sharedSprite;
            }
        }

        /// <summary>
        /// How strongly one of the cube's lines covers the point at <paramref name="offset"/>
        /// from the middle of the sprite, where (0, 0) is the middle and 1 in x or y is the
        /// sprite's edge: 1 on a line's centre, fading to 0 just either side of it.
        /// </summary>
        // `public static` so the Edit Mode tests can check the shape without drawing anything.
        public static float LineAlphaAt(Vector2 offset)
        {
            float x = Mathf.Abs(offset.x);
            float y = Mathf.Abs(offset.y);
            // "How far out is this point?", measured the way a square measures it: by whichever
            // of x and y is bigger. Every point with the same answer lies on the same square
            // outline, just as every point at the same ordinary distance lies on the same circle.
            float reach = Mathf.Max(x, y);

            // The distance to the nearest line. Start with the two square outlines...
            float nearest = Mathf.Min(Mathf.Abs(reach - OuterSize), Mathf.Abs(reach - InnerSize));
            // ...then, between the two squares, the lines joining their corners. Those run along
            // the diagonals, where x and y are equal, so (x - y) says how far off one a point is.
            // Dividing by the square root of 2 turns that into a true distance.
            if (reach > InnerSize && reach < OuterSize)
            {
                nearest = Mathf.Min(nearest, Mathf.Abs(x - y) / Mathf.Sqrt(2f));
            }

            return Mathf.Clamp01(1f - nearest / LineWidth);
        }

        /// <summary>
        /// The sprite's colour and opacity at <paramref name="offset"/> from its middle (see
        /// <see cref="LineAlphaAt"/> for the units): the dark tile inside the outer square, the
        /// coloured lines on top of it, and nothing at all outside.
        /// </summary>
        public static Color ColorAt(Vector2 offset)
        {
            float line = LineAlphaAt(offset);
            bool onTile = Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) <= OuterSize;

            // Atan2 gives the angle of a point around the middle, from -PI to PI. Dividing by a
            // full turn (2 * PI) and wrapping with Repeat makes that 0 to 1: once round the cube.
            float turn = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) / (2f * Mathf.PI), 1f);
            // HSV describes a colour as Hue (which colour, 0 to 1 round the colour wheel),
            // Saturation (how vivid) and Value (how bright). Using the angle as the hue paints
            // the whole colour wheel around the cube.
            Color lineColor = Color.HSVToRGB(turn, LineSaturation, 1f);

            // Lerp blends from the tile to the line by how much line is here.
            Color color = Color.Lerp(FillColor, lineColor, line);
            color.a = Mathf.Max(onTile ? FillAlpha : 0f, line);
            return color;
        }

        // The same recipe as FlameGlow.BuildSprite: see the comments there. The difference is
        // that these pixels have their own colours, where the glow is white and tinted later.
        private static Sprite BuildSprite()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.name = "Hypercube";
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
                    pixels[y * TextureSize + x] = ColorAt(offset);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize / SizeInCells);
            sprite.name = "Hypercube";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
