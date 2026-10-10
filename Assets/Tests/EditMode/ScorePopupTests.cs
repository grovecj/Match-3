using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="ScorePopup"/>'s mappings: what a popup says, and how much
    /// bigger and brighter it gets with the multiplier.
    /// </summary>
    public class ScorePopupTests
    {
        [Test]
        public void AtTimesOne_ItShowsJustThePoints()
        {
            Assert.AreEqual("+1000", ScorePopup.TextFor(1000, 1));
        }

        [Test]
        public void WithAMultiplier_ItShowsTheMultipliedPointsAndTheMultiplier()
        {
            // The points are already multiplied (ClearStep.Points); the x2 just says why.
            string text = ScorePopup.TextFor(2000, 2);
            StringAssert.StartsWith("+2000", text);
            StringAssert.Contains("x2", text);
        }

        [Test]
        public void HigherMultipliers_AreBigger_UpToTheMax()
        {
            Assert.AreEqual(1f, ScorePopup.ScaleFor(1), 0.0001f);
            for (int multiplier = 2; multiplier <= ScoreRules.MaxMultiplier; multiplier++)
            {
                Assert.That(ScorePopup.ScaleFor(multiplier), Is.GreaterThan(ScorePopup.ScaleFor(multiplier - 1)));
            }
            Assert.AreEqual(ScorePopup.ScaleFor(ScoreRules.MaxMultiplier), ScorePopup.ScaleFor(ScoreRules.MaxMultiplier + 3), 0.0001f);
        }

        [Test]
        public void HigherMultipliers_AreWarmer()
        {
            // "Brighter" here means more saturated: further from white toward gold. Less blue
            // is the simplest measure of that for these colours.
            for (int multiplier = 2; multiplier <= ScoreRules.MaxMultiplier; multiplier++)
            {
                Assert.That(ScorePopup.ColorFor(multiplier).b, Is.LessThan(ScorePopup.ColorFor(multiplier - 1).b));
            }
            Assert.AreEqual(1f, ScorePopup.ColorFor(1).a, 0.0001f, "Popups start fully opaque.");
        }
    }
}
