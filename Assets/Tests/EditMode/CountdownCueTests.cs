using System.Collections.Generic;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="GameManager.GetCountdownCue"/>: which sound the timer
    /// display changing from one second to another should play. It's a plain static method, so
    /// a whole round's worth of timing can be checked with numbers alone: no scene, no clock,
    /// no audio.
    /// </summary>
    public class CountdownCueTests
    {
        private const int From = GameManager.CountdownFrom;

        /// <summary>
        /// Feeds a sequence of displayed seconds through GetCountdownCue the way the
        /// GameManager does (each one compared with the one before) and collects every cue
        /// that isn't None.
        /// </summary>
        private static List<CountdownCue> CuesFor(params int[] secondsShown)
        {
            var cues = new List<CountdownCue>();
            for (int i = 1; i < secondsShown.Length; i++)
            {
                CountdownCue cue = GameManager.GetCountdownCue(secondsShown[i - 1], secondsShown[i], From);
                if (cue != CountdownCue.None)
                {
                    cues.Add(cue);
                }
            }
            return cues;
        }

        [TestCase(60, 59)]
        [TestCase(11, 10)]
        [TestCase(7, 6)]
        public void NoCue_AboveTheCountdown(int previous, int current)
        {
            Assert.AreEqual(CountdownCue.None, GameManager.GetCountdownCue(previous, current, From));
        }

        [TestCase(60)]
        [TestCase(5)]
        [TestCase(1)]
        [TestCase(0)]
        public void NoCue_WhileTheSecondHasNotChanged(int second)
        {
            // Most frames: the display reads the same as it did last frame.
            Assert.AreEqual(CountdownCue.None, GameManager.GetCountdownCue(second, second, From));
        }

        [TestCase(6, 5)]
        [TestCase(5, 4)]
        [TestCase(4, 3)]
        [TestCase(3, 2)]
        [TestCase(2, 1)]
        public void Tick_OnReachingEachCountdownSecond(int previous, int current)
        {
            Assert.AreEqual(CountdownCue.Tick, GameManager.GetCountdownCue(previous, current, From));
        }

        [Test]
        public void Horn_OnReachingZero()
        {
            Assert.AreEqual(CountdownCue.Horn, GameManager.GetCountdownCue(1, 0, From));
        }

        [Test]
        public void AWholeCountdown_IsFiveTicksThenOneHorn()
        {
            // Each second appears several times, as it does over the frames it's on screen.
            List<CountdownCue> cues = CuesFor(7, 7, 6, 6, 6, 5, 5, 4, 4, 4, 3, 3, 2, 2, 2, 1, 1, 0, 0, 0);

            var expected = new[]
            {
                CountdownCue.Tick, CountdownCue.Tick, CountdownCue.Tick, CountdownCue.Tick, CountdownCue.Tick,
                CountdownCue.Horn,
            };
            CollectionAssert.AreEqual(expected, cues);
        }

        [Test]
        public void AFrameSpikeThatSkipsASecond_PlaysOneTickNotTwo()
        {
            // A long frame takes the display straight from 0:04 to 0:02, never showing 0:03.
            Assert.AreEqual(CountdownCue.Tick, GameManager.GetCountdownCue(4, 2, From));
            CollectionAssert.AreEqual(
                new[] { CountdownCue.Tick, CountdownCue.Tick, CountdownCue.Tick, CountdownCue.Horn },
                CuesFor(5, 4, 2, 1, 0));
        }

        [Test]
        public void AFrameSpikeIntoTheCountdown_PlaysOneTick()
        {
            // From well above the countdown straight into the middle of it.
            Assert.AreEqual(CountdownCue.Tick, GameManager.GetCountdownCue(8, 3, From));
        }

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(30)]
        public void AFrameSpikeThatReachesZero_PlaysOnlyTheHorn(int previous)
        {
            Assert.AreEqual(CountdownCue.Horn, GameManager.GetCountdownCue(previous, 0, From));
        }

        [Test]
        public void Restarting_PlaysNothing_ThenCountsDownAgain()
        {
            // Play Again: the clock jumps from 0 back up to the full round.
            Assert.AreEqual(CountdownCue.None, GameManager.GetCountdownCue(0, 60, From));

            // Two rounds back to back each get their own five ticks and a horn.
            List<CountdownCue> cues = CuesFor(6, 5, 4, 3, 2, 1, 0, 60, 59, 6, 5, 4, 3, 2, 1, 0);
            Assert.AreEqual(10, cues.FindAll(cue => cue == CountdownCue.Tick).Count);
            Assert.AreEqual(2, cues.FindAll(cue => cue == CountdownCue.Horn).Count);
        }

        [Test]
        public void CountdownFrom_SetsHowManyTicksThereAre()
        {
            Assert.AreEqual(CountdownCue.None, GameManager.GetCountdownCue(5, 4, 3));
            Assert.AreEqual(CountdownCue.Tick, GameManager.GetCountdownCue(4, 3, 3));
            Assert.AreEqual(CountdownCue.Tick, GameManager.GetCountdownCue(11, 10, 10));
        }
    }
}
