using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Easing curves: functions that reshape an animation's progress <c>t</c> (0 at the start,
    /// 1 at the end) so motion isn't at constant speed. Feed the result to a Lerp in place of
    /// <c>t</c>. All of them return 0 at t = 0 and 1 at t = 1; they differ in what happens between.
    /// </summary>
    public static class Easing
    {
        // How far OutBack overshoots. 1.70158 is the standard value (from Robert Penner's easing
        // equations), which overshoots by about 10% of the distance travelled.
        private const float BackOvershoot = 1.70158f;

        /// <summary>
        /// Starts fast and slows into the end. Good for motion that's already moving when it
        /// starts, like a token the player just let go of.
        /// </summary>
        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float remaining = 1f - t;
            return 1f - remaining * remaining * remaining;
        }

        /// <summary>
        /// Starts slow and speeds up into the end: the mirror image of <see cref="OutCubic"/>.
        /// Good for something being pulled in, like matched gems sucked into a new power gem.
        /// </summary>
        public static float InCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        /// <summary>
        /// Like <see cref="OutCubic"/>, but goes a little past the end and settles back, like a
        /// spring. Values briefly go above 1 in the second half.
        /// </summary>
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            float u = t - 1f;
            return 1f + (BackOvershoot + 1f) * u * u * u + BackOvershoot * u * u;
        }
    }
}
