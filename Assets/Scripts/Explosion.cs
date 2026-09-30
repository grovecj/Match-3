using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The particle burst played where a token was cleared: small shards in the token's color
    /// that fly out, fade and shrink. Bigger matches get a bigger, faster burst, and 5+ also get
    /// a white flash behind it.
    ///
    /// Lives on the Explosion prefab (built by Match3 > Build Explosion Prefab). The Board spawns
    /// one per cleared cell and calls <see cref="Play"/>. It's never a child of the token: the
    /// token is destroyed a moment later, and destroying a GameObject destroys its children
    /// too, which would cut the burst off halfway.
    ///
    /// Nothing here cleans up: the shards' ParticleSystem has Stop Action = Destroy, so Unity
    /// destroys this whole GameObject (flash included) once the last particle has faded.
    /// </summary>
    // A ParticleSystem is a component that spawns, moves and draws lots of small sprites
    // ("particles") for you. It's split into "modules" (Main, Emission, Shape, Color over
    // Lifetime...), each a group of settings you can see in the Inspector.
    [RequireComponent(typeof(ParticleSystem))]
    public class Explosion : MonoBehaviour
    {
        [Tooltip("A white flash behind the shards, only played for matches of 5 or more. Optional.")]
        [SerializeField] private ParticleSystem flash;

        [Header("Base burst (a match of 3)")]
        [Tooltip("How many shards a match of 3 throws out. Bigger matches multiply this.")]
        [SerializeField, Min(1)] private int shardCount = 12;
        [Tooltip("Slowest and fastest shard speed for a match of 3, in cells per second.")]
        [SerializeField] private Vector2 shardSpeed = new Vector2(1.5f, 3.5f);

        // The colored shards: the ParticleSystem on this GameObject (RequireComponent above
        // guarantees there is one).
        private ParticleSystem shards;

        /// <summary>The run length the last <see cref="Play"/> sized this burst for.</summary>
        public int RunLength { get; private set; }

        private void Awake()
        {
            shards = GetComponent<ParticleSystem>();
        }

        /// <summary>
        /// Colors the shards, sizes the burst for a run of <paramref name="runLength"/> tokens,
        /// and plays it.
        /// </summary>
        public void Play(Color color, int runLength)
        {
            RunLength = runLength;

            // Each module is a struct that writes straight through to the ParticleSystem, so
            // `var main = shards.main; main.x = ...` really does change the system (even though
            // changing a copy of a struct normally wouldn't).
            ParticleSystem.MainModule main = shards.main;
            // A MinMaxGradient made from two colors picks a random color between them for each
            // particle, so mixing in some white makes the burst sparkle instead of being flat.
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.5f));
            float speed = SpeedMultiplier(runLength);
            main.startSpeed = new ParticleSystem.MinMaxCurve(shardSpeed.x * speed, shardSpeed.y * speed);

            // A burst emits a fixed number of particles all at once (here at time 0), instead of
            // a steady stream per second.
            int count = Mathf.RoundToInt(shardCount * CountMultiplier(runLength));
            shards.emission.SetBurst(0, new ParticleSystem.Burst(0f, count));

            // Play() normally starts the child systems too; the flash is a child, but it should
            // only go off for big matches, so play the shards on their own.
            shards.Play(withChildren: false);
            if (flash != null && HasFlash(runLength))
            {
                flash.Play();
            }
        }

        // The mappings below are `static` and `public` so the Edit Mode tests can check them
        // with no scene or particles. They group runs like the score and sounds do: 3, 4, 5+.

        /// <summary>How many times the base shard count a run of <paramref name="runLength"/> throws out.</summary>
        public static float CountMultiplier(int runLength)
        {
            if (runLength >= 5)
            {
                return 2f;
            }
            if (runLength == 4)
            {
                return 1.5f;
            }
            return 1f;
        }

        /// <summary>How many times the base shard speed a run of <paramref name="runLength"/> uses.</summary>
        public static float SpeedMultiplier(int runLength)
        {
            if (runLength >= 5)
            {
                return 1.3f;
            }
            if (runLength == 4)
            {
                return 1.15f;
            }
            return 1f;
        }

        /// <summary>Whether a run of <paramref name="runLength"/> gets the white flash.</summary>
        public static bool HasFlash(int runLength)
        {
            return runLength >= 5;
        }
    }
}
