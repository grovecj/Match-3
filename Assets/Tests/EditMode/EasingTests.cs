using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>Edit Mode tests for the <see cref="Easing"/> curves.</summary>
    public class EasingTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void OutCubic_StartsAtZeroAndEndsAtOne()
        {
            Assert.AreEqual(0f, Easing.OutCubic(0f), Tolerance);
            Assert.AreEqual(1f, Easing.OutCubic(1f), Tolerance);
        }

        [Test]
        public void OutCubic_IsAheadOfLinearInTheMiddle()
        {
            // "Out" easing starts fast, so halfway through the time it's more than halfway there.
            Assert.That(Easing.OutCubic(0.5f), Is.GreaterThan(0.5f));
        }

        [Test]
        public void OutCubic_ClampsOutOfRangeInput()
        {
            Assert.AreEqual(0f, Easing.OutCubic(-1f), Tolerance);
            Assert.AreEqual(1f, Easing.OutCubic(2f), Tolerance);
        }

        [Test]
        public void OutBack_StartsAtZeroAndEndsAtOne()
        {
            Assert.AreEqual(0f, Easing.OutBack(0f), Tolerance);
            Assert.AreEqual(1f, Easing.OutBack(1f), Tolerance);
        }

        [Test]
        public void OutBack_OvershootsBeforeSettling()
        {
            // It goes past the end part-way through, then comes back to exactly 1.
            Assert.That(Easing.OutBack(0.75f), Is.GreaterThan(1f));
        }

        [Test]
        public void OutBack_ClampsOutOfRangeInput()
        {
            Assert.AreEqual(0f, Easing.OutBack(-1f), Tolerance);
            Assert.AreEqual(1f, Easing.OutBack(2f), Tolerance);
        }
    }
}
