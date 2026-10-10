using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Gives the camera a small shake after a clear step that took a lot of gems with it: a
    /// big blast feels big. A plain match of 3 doesn't shake at all; a Flame's blast shakes a
    /// little; a board-clearing chain shakes the most, but never much (see <see cref="ShakeAmount"/>).
    /// The player can turn it off in Settings (<see cref="ScreenShakeSetting"/>).
    ///
    /// Goes on the camera, next to the <see cref="BoardCamera"/>. Listens to the Board's
    /// <see cref="Board.ClearStepScored"/>, like the score does.
    /// </summary>
    // BoardCamera puts the camera in place every LateUpdate. Running after it (a higher
    // execution order runs later) lets this nudge the camera off that spot each frame without
    // the nudges adding up: next frame BoardCamera puts it back, and this nudges it again.
    [DefaultExecutionOrder(100)]
    public class ScreenShake : MonoBehaviour
    {
        /// <summary>A step must clear more gems than this before the screen shakes at all.</summary>
        public const int GemsBeforeShaking = 6;

        /// <summary>A step clearing this many gems or more shakes at <see cref="MaxShakeCells"/>.</summary>
        public const int GemsForFullShake = 40;

        /// <summary>The biggest shake, in cells: how far, at most, the camera is pushed off.</summary>
        public const float MaxShakeCells = 0.18f;

        /// <summary>How long, in seconds, a shake takes to die down.</summary>
        public const float Duration = 0.3f;

        // How many times a second the shake changes direction. Fast enough to read as a jolt.
        private const float Frequency = 30f;

        [SerializeField] private Board board;

        // How strong the current shake started out, in world units, and how long it's been going.
        private float strength;
        private float elapsed = Duration;

        private void OnEnable()
        {
            if (board == null)
            {
                Debug.LogError("ScreenShake: needs a Board assigned.", this);
                return;
            }
            board.ClearStepScored += OnClearStepScored;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.ClearStepScored -= OnClearStepScored;
            }
        }

        private void OnClearStepScored(ClearStep step)
        {
            if (!ScreenShakeSetting.Enabled)
            {
                return;
            }
            float amount = ShakeAmount(step.GemsCleared) * board.CellSize;
            // A small shake doesn't cut short a bigger one that's still going.
            if (amount > CurrentStrength())
            {
                strength = amount;
                elapsed = 0f;
            }
        }

        private void LateUpdate()
        {
            if (elapsed >= Duration)
            {
                return;
            }
            // Scaled time, so a shake freezes on pause like everything else.
            elapsed += Time.deltaTime;
            // Turned off mid-shake: stop at once rather than finishing it.
            if (!ScreenShakeSetting.Enabled)
            {
                elapsed = Duration;
                return;
            }

            // Perlin noise is smooth randomness: nearby inputs give nearby outputs, so the
            // camera jitters rather than teleporting. It returns 0..1; shifting to -1..1 lets
            // it push both ways. Different second inputs give x and y unrelated wobbles.
            float t = elapsed * Frequency;
            var offset = new Vector3(
                Mathf.PerlinNoise(t, 0f) * 2f - 1f,
                Mathf.PerlinNoise(0f, t) * 2f - 1f,
                0f);
            transform.position += offset * CurrentStrength();
        }

        /// <summary>How far the camera can be pushed off right now, in world units.</summary>
        private float CurrentStrength()
        {
            return strength * Falloff(elapsed);
        }

        // The two mappings below are `public static` so the Edit Mode tests can check them.

        /// <summary>
        /// How hard a step that cleared <paramref name="gemsCleared"/> gems shakes, in cells:
        /// nothing up to <see cref="GemsBeforeShaking"/>, then growing steadily up to
        /// <see cref="MaxShakeCells"/> at <see cref="GemsForFullShake"/>, and no more after that.
        /// </summary>
        public static float ShakeAmount(int gemsCleared)
        {
            if (gemsCleared <= GemsBeforeShaking)
            {
                return 0f;
            }
            // InverseLerp says how far a value is between two others, as 0..1 (clamped).
            float t = Mathf.InverseLerp(GemsBeforeShaking, GemsForFullShake, gemsCleared);
            return t * MaxShakeCells;
        }

        /// <summary>
        /// How much of a shake is left <paramref name="elapsed"/> seconds in: 1 at the start,
        /// easing down to 0 at <see cref="Duration"/>.
        /// </summary>
        public static float Falloff(float elapsed)
        {
            float remaining = 1f - Mathf.Clamp01(elapsed / Duration);
            // Squared, so it dies away quickly at first and then settles gently.
            return remaining * remaining;
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>Sets the Board the scene would assign. Call it while the GameObject is still inactive.</summary>
        internal void ConfigureForTests(Board board)
        {
            this.board = board;
        }
    }
}
