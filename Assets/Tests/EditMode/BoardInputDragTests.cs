using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="BoardInput.TryGetSwapDirection"/>, the drag-to-direction
    /// math pulled out of <c>BoardInput.UpdateDrag</c>. It's a static method taking plain numbers,
    /// so there's no pointer, camera, or board to set up.
    ///
    /// The numbers below are chosen to be exact in binary floating point (0.5, 0.75, 0.375, ...),
    /// so "exactly at the threshold" really is exact and the boundary tests aren't at the mercy
    /// of rounding.
    /// </summary>
    public class BoardInputDragTests
    {
        // Same shape as the Inspector defaults: threshold 0.5 (half a 1-unit cell), tolerance 0.75.
        private const float Threshold = 0.5f;
        private const float Tolerance = 0.75f;

        /// <summary>Asserts the drag is accepted and points in <paramref name="expected"/>.</summary>
        private static void AssertDirection(Vector2 delta, Vector2Int expected, float tolerance = Tolerance)
        {
            bool ok = BoardInput.TryGetSwapDirection(delta, Threshold, tolerance, out Vector2Int direction);
            Assert.IsTrue(ok, $"Expected drag {delta} to pick a direction.");
            Assert.AreEqual(expected, direction);
        }

        /// <summary>Asserts the drag is rejected (and the out value is left at zero).</summary>
        private static void AssertNoDirection(Vector2 delta, float tolerance = Tolerance)
        {
            bool ok = BoardInput.TryGetSwapDirection(delta, Threshold, tolerance, out Vector2Int direction);
            Assert.IsFalse(ok, $"Expected drag {delta} to be rejected.");
            Assert.AreEqual(Vector2Int.zero, direction);
        }

        // ---- Short drags ----

        [Test]
        public void NoMovement_IsRejected()
        {
            AssertNoDirection(Vector2.zero);
        }

        [Test]
        public void ShortDrag_IsRejected()
        {
            AssertNoDirection(new Vector2(0.25f, 0f));
            AssertNoDirection(new Vector2(0f, -0.25f));
        }

        [Test]
        public void ShortDiagonalDrag_IsRejected()
        {
            // Length ~0.53 is past the threshold, but neither axis on its own is. The threshold
            // is measured along the dominant axis, not the straight-line distance.
            AssertNoDirection(new Vector2(0.4375f, 0.3f));
        }

        // ---- The four directions ----

        [Test]
        public void RightDrag_ReturnsRight()
        {
            AssertDirection(new Vector2(1f, 0f), Vector2Int.right);
        }

        [Test]
        public void LeftDrag_ReturnsLeft()
        {
            AssertDirection(new Vector2(-1f, 0f), Vector2Int.left);
        }

        [Test]
        public void UpDrag_ReturnsUp()
        {
            AssertDirection(new Vector2(0f, 1f), Vector2Int.up);
        }

        [Test]
        public void DownDrag_ReturnsDown()
        {
            AssertDirection(new Vector2(0f, -1f), Vector2Int.down);
        }

        // ---- Slightly off-axis drags still pick the dominant axis ----

        [Test]
        public void MostlyHorizontalDrag_PicksHorizontal()
        {
            AssertDirection(new Vector2(1f, 0.25f), Vector2Int.right);
            AssertDirection(new Vector2(-1f, -0.25f), Vector2Int.left);
        }

        [Test]
        public void MostlyVerticalDrag_PicksVertical()
        {
            AssertDirection(new Vector2(0.25f, 1f), Vector2Int.up);
            AssertDirection(new Vector2(-0.25f, -1f), Vector2Int.down);
        }

        // ---- Near-45° drags ----

        [Test]
        public void ExactDiagonal_IsRejected_InAllFourQuadrants()
        {
            AssertNoDirection(new Vector2(1f, 1f));
            AssertNoDirection(new Vector2(-1f, 1f));
            AssertNoDirection(new Vector2(1f, -1f));
            AssertNoDirection(new Vector2(-1f, -1f));
        }

        [Test]
        public void NearDiagonal_AboveTolerance_IsRejected()
        {
            // minor / major = 0.875, over the 0.75 tolerance.
            AssertNoDirection(new Vector2(1f, 0.875f));
            AssertNoDirection(new Vector2(-0.875f, -1f));
        }

        // ---- Boundaries ----

        [Test]
        public void ExactlyAtThreshold_IsAccepted()
        {
            // The check is "major < threshold" means too short, so equal is long enough.
            AssertDirection(new Vector2(0.5f, 0f), Vector2Int.right);
            AssertDirection(new Vector2(0f, -0.5f), Vector2Int.down);
        }

        [Test]
        public void JustUnderThreshold_IsRejected()
        {
            AssertNoDirection(new Vector2(0.4375f, 0f));
            AssertNoDirection(new Vector2(0f, -0.4375f));
        }

        [Test]
        public void ExactlyAtDiagonalTolerance_IsRejected()
        {
            // minor / major = 0.75 exactly. The check is "minor >= major * tolerance", so the
            // boundary counts as diagonal.
            AssertNoDirection(new Vector2(1f, 0.75f));
            AssertNoDirection(new Vector2(-0.75f, 1f));
        }

        [Test]
        public void JustUnderDiagonalTolerance_IsAccepted()
        {
            // minor / major = 0.6875, just inside the tolerance.
            AssertDirection(new Vector2(1f, 0.6875f), Vector2Int.right);
            AssertDirection(new Vector2(-0.6875f, 1f), Vector2Int.up);
        }

        [Test]
        public void ToleranceOfOne_OnlyRejectsExactDiagonals()
        {
            AssertNoDirection(new Vector2(1f, 1f), tolerance: 1f);
            AssertDirection(new Vector2(1f, 0.9375f), Vector2Int.right, tolerance: 1f);
        }

        [Test]
        public void ToleranceOfZero_RejectsEverything()
        {
            // minor >= 0 is always true, so even a perfectly straight drag is "diagonal".
            // Not a useful setting, but it pins down what the Inspector's 0 end of the slider does.
            AssertNoDirection(new Vector2(1f, 0f), tolerance: 0f);
        }
    }
}
