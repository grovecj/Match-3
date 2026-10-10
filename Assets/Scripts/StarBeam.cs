using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The beam a Star gem fires when it goes off: four bars of light that grow outward from
    /// the gem's cell (left, right, down and up) until they reach the edges of the board, then
    /// fade away. The <see cref="Board"/> makes one per detonating Star and calls
    /// <see cref="Play"/>; the beam then runs itself and destroys its own GameObject when done.
    ///
    /// It only draws. Which tokens the beam removes is the model's business
    /// (<see cref="BoardModel.StarBlastCells"/>), and the Board pops each of them as the beam
    /// gets to it (<see cref="Board.BlastDelay"/>), using the same speed as the beam is given
    /// here. That shared speed is what keeps the picture and the pops in step.
    ///
    /// A placeholder, like the Flame's blast: proper effects are a later issue.
    /// </summary>
    public class StarBeam : MonoBehaviour
    {
        /// <summary>How thick the beam is, in cells.</summary>
        public const float Thickness = 0.35f;

        /// <summary>How long, in seconds, the beam takes to fade out once every arm has reached its edge.</summary>
        public const float FadeDuration = 0.15f;

        // The beam's texture is this many pixels square. Every column of it is the same (the
        // beam looks the same all the way along), so it can be stretched to any length.
        private const int TextureSize = 32;

        // The four ways the arms point. An arm's sprite lies along its own x axis, so turning
        // the arm by 0, 90, 180 or 270 degrees points it right, up, left or down.
        private const int ArmCount = 4;

        private static Sprite sharedSprite;

        private readonly Transform[] arms = new Transform[ArmCount];
        private readonly SpriteRenderer[] armRenderers = new SpriteRenderer[ArmCount];
        // How long each arm ends up, in world units: the distance from the gem to that edge.
        private readonly float[] fullLengths = new float[ArmCount];
        private float cellSize = 1f;
        // How fast the arms grow, in world units per second.
        private float speed = 1f;
        // How long the longest arm takes to arrive. The fade starts then.
        private float travelDuration;
        private float elapsed;
        private Color color = Color.white;

        /// <summary>
        /// Starts the beam where this GameObject is, which should be the middle of the Star's
        /// cell. The four reaches are how far each arm has to go to hit the edge of the board,
        /// in cells, measured from the middle of that cell.
        /// </summary>
        /// <param name="color">The beam's colour.</param>
        /// <param name="cellSize">The size of a board cell, in world units.</param>
        /// <param name="cellsPerSecond">How fast the arms grow.</param>
        /// <param name="drawLike">A renderer to copy the material and sorting layer from, so the beam is drawn the way tokens are.</param>
        /// <param name="sortingOrder">Where in that layer to draw. Higher is further forward.</param>
        public void Play(Color color, float cellSize, float cellsPerSecond,
            float reachRight, float reachUp, float reachLeft, float reachDown,
            SpriteRenderer drawLike, int sortingOrder)
        {
            this.color = color;
            this.cellSize = cellSize;
            speed = cellsPerSecond * cellSize;
            elapsed = 0f;

            // In the order the arms are turned: 0, 90, 180 and 270 degrees.
            float[] reaches = { reachRight, reachUp, reachLeft, reachDown };
            travelDuration = 0f;
            for (int i = 0; i < ArmCount; i++)
            {
                fullLengths[i] = Mathf.Max(0f, reaches[i]) * cellSize;
                travelDuration = Mathf.Max(travelDuration, fullLengths[i] / speed);

                if (arms[i] == null)
                {
                    var armObject = new GameObject($"Arm {i}");
                    arms[i] = armObject.transform;
                    arms[i].SetParent(transform, false);
                    // Euler angles are "degrees of turn around each axis". Turning around z
                    // spins a 2D object in the plane of the screen.
                    arms[i].localRotation = Quaternion.Euler(0f, 0f, 90f * i);
                    armRenderers[i] = armObject.AddComponent<SpriteRenderer>();
                    armRenderers[i].sprite = BeamSprite;
                }

                if (drawLike != null)
                {
                    armRenderers[i].sharedMaterial = drawLike.sharedMaterial;
                    armRenderers[i].sortingLayerID = drawLike.sortingLayerID;
                }
                armRenderers[i].sortingOrder = sortingOrder;
            }

            Redraw();
        }

        /// <summary>
        /// How long an arm that will end up <paramref name="fullLength"/> long is after
        /// <paramref name="elapsed"/> seconds of growing at <paramref name="speed"/>: it grows
        /// steadily, then stops at its full length.
        /// </summary>
        // `public static` so the Edit Mode tests can check it without a scene.
        public static float ArmLength(float elapsed, float speed, float fullLength)
        {
            return Mathf.Clamp(elapsed * speed, 0f, fullLength);
        }

        /// <summary>
        /// How opaque the whole beam is <paramref name="elapsed"/> seconds in, when its longest
        /// arm takes <paramref name="travelDuration"/> to arrive: solid while anything is still
        /// travelling, then fading to nothing over <see cref="FadeDuration"/>.
        /// </summary>
        public static float Opacity(float elapsed, float travelDuration)
        {
            return 1f - Mathf.Clamp01((elapsed - travelDuration) / FadeDuration);
        }

        private void Update()
        {
            // Time.deltaTime is scaled time, so the beam freezes while the game is paused.
            elapsed += Time.deltaTime;
            Redraw();

            if (elapsed >= travelDuration + FadeDuration)
            {
                // Destroying a GameObject destroys its children too, so the arms go with it.
                Destroy(gameObject);
            }
        }

        /// <summary>Sizes and colours the four arms for the current <see cref="elapsed"/> time.</summary>
        private void Redraw()
        {
            Color faded = color;
            faded.a *= Opacity(elapsed, travelDuration);

            for (int i = 0; i < ArmCount; i++)
            {
                if (arms[i] == null)
                {
                    continue;
                }
                // The sprite is 1 unit long and 1 unit thick with its pivot at one end, so its
                // scale is its size: x stretches it away from the gem, y sets how thick it is.
                float length = ArmLength(elapsed, speed, fullLengths[i]);
                arms[i].localScale = new Vector3(length, Thickness * cellSize, 1f);
                armRenderers[i].color = faded;
            }
        }

        /// <summary>The sprite every arm draws: a white bar, soft along its long edges, built in code.</summary>
        private static Sprite BeamSprite
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

        // The same recipe as FlameGlow.BuildSprite: see the comments there.
        private static Sprite BuildSprite()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.name = "Star Beam";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color[TextureSize * TextureSize];
            float middle = (TextureSize - 1) / 2f;
            for (int y = 0; y < TextureSize; y++)
            {
                // 0 on the beam's centre line, 1 at its edge. Squaring it and taking that from 1
                // gives a bar that's solid down the middle and fades out toward both edges.
                float across = Mathf.Abs(y - middle) / (TextureSize / 2f);
                float alpha = Mathf.Clamp01(1f - across * across);
                for (int x = 0; x < TextureSize; x++)
                {
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();

            // TextureSize pixels at TextureSize pixels per unit is exactly 1 unit each way, so
            // scaling the sprite sets its size directly. The pivot is the point the sprite is
            // positioned, turned and scaled around: (0, 0.5) is the middle of its left end, so
            // that end stays on the gem while the other one stretches away.
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0f, 0.5f), TextureSize);
            sprite.name = "Star Beam";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
