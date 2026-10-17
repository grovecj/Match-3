using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="RoundSummary"/>: what the round-over screen says for each
    /// way a round can compare with the personal best, and the stat lines.
    /// </summary>
    public class RoundSummaryTests
    {
        [Test]
        public void FirstRoundEver_ShowsOnlyTheScore()
        {
            RoundSummary summary = RoundSummary.Create(84200, null, new RoundStats());

            Assert.That(summary.ScoreLine, Is.EqualTo("84,200"));
            Assert.That(summary.IsNewBest, Is.False);
        }

        [Test]
        public void BeatingTheBest_SaysNewBest()
        {
            RoundSummary summary = RoundSummary.Create(84200, 84000, new RoundStats());

            Assert.That(summary.ScoreLine, Is.EqualTo("84,200: New best!"));
            Assert.That(summary.IsNewBest, Is.True);
        }

        [Test]
        public void MissingTheBest_SaysHowFarShort()
        {
            RoundSummary summary = RoundSummary.Create(71800, 84200, new RoundStats());

            Assert.That(summary.ScoreLine, Is.EqualTo("71,800, 12,400 short of your best"));
            Assert.That(summary.IsNewBest, Is.False);
        }

        [Test]
        public void EqualToTheBest_IsNotANewBest()
        {
            RoundSummary summary = RoundSummary.Create(84000, 84000, new RoundStats());

            Assert.That(summary.ScoreLine, Is.EqualTo("84,000: equals your best"));
            Assert.That(summary.IsNewBest, Is.False);
        }

        [Test]
        public void StatLines_ArePowerGemsMadeBestChainAndMostGemsFromOneSwap()
        {
            var stats = new RoundStats
            {
                flamesCreated = 3,
                starsCreated = 1,
                hypercubesCreated = 0,
                bestChain = 4,
                mostGemsOneSwap = 23,
            };

            RoundSummary summary = RoundSummary.Create(84200, 84000, stats);

            Assert.That(summary.StatLines, Is.EqualTo(new[]
            {
                "Flames 3 · Stars 1 · Hypercubes 0",
                "Best chain: 4",
                "Most gems from one swap: 23",
            }));
        }

        [Test]
        public void StatLines_ComeFromTheStatsGiven()
        {
            // The summary reads the very RoundStats the server gets, so what it shows is
            // what's posted. Counting the way the Board's events would:
            var stats = new RoundStats();
            stats.SwapStarted();
            stats.PowerGemCreated(PowerKind.Star);
            stats.StepScored(new ClearStep(0, new[] { 3 }, 0, gemsCleared: 3));
            stats.StepScored(new ClearStep(1, new[] { 4 }, 0, gemsCleared: 4));

            RoundSummary summary = RoundSummary.Create(1000, null, stats);

            Assert.That(summary.StatLines[0], Is.EqualTo("Flames 0 · Stars 1 · Hypercubes 0"));
            Assert.That(summary.StatLines[1], Is.EqualTo("Best chain: 2"));
            Assert.That(summary.StatLines[2], Is.EqualTo("Most gems from one swap: 7"));
        }

        [Test]
        public void AllLines_AreTheHeadlineScoreAndStatsInOrder()
        {
            RoundSummary summary = RoundSummary.Create(84200, 84000, new RoundStats());

            Assert.That(summary.AllLines(), Is.EqualTo(
                "Time's up!\n84,200: New best!\nFlames 0 · Stars 0 · Hypercubes 0\n" +
                "Best chain: 0\nMost gems from one swap: 0"));
        }
    }
}
