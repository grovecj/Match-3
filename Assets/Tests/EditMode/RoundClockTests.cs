using System.Collections.Generic;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="RoundClock"/>: the round's game time, and the countdown
    /// cues it asks for. Frames are just numbers here, so a whole round (holds included) runs
    /// in no time.
    /// </summary>
    public class RoundClockTests
    {
        private const int From = GameManager.CountdownFrom;

        // A fixed "frame" length that divides a second exactly, so the sums stay exact.
        private const float Frame = 0.25f;

        /// <summary>
        /// Advances <paramref name="clock"/> by <paramref name="frames"/> frames, held or not,
        /// and collects every cue that isn't None, with the second the display showed for it.
        /// </summary>
        private static void Run(RoundClock clock, int frames, bool held, List<(CountdownCue, int)> cues)
        {
            for (int i = 0; i < frames; i++)
            {
                CountdownCue cue = clock.Advance(Frame, held);
                if (cue != CountdownCue.None)
                {
                    cues.Add((cue, clock.SecondsShown));
                }
            }
        }

        private static List<(CountdownCue, int)> FullCountdown()
        {
            return new List<(CountdownCue, int)>
            {
                (CountdownCue.Tick, 5),
                (CountdownCue.Tick, 4),
                (CountdownCue.Tick, 3),
                (CountdownCue.Tick, 2),
                (CountdownCue.Tick, 1),
                (CountdownCue.Horn, 0),
            };
        }

        [Test]
        public void Advance_CountsDown()
        {
            var clock = new RoundClock(From);
            clock.Reset(60f);

            clock.Advance(Frame, held: false);

            Assert.AreEqual(60f - Frame, clock.TimeRemaining);
            Assert.IsFalse(clock.IsHeld);
        }

        [Test]
        public void HeldClock_DoesNotAdvance_AndSaysItIsHeld()
        {
            var clock = new RoundClock(From);
            clock.Reset(60f);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(CountdownCue.None, clock.Advance(Frame, held: true));
            }

            Assert.AreEqual(60f, clock.TimeRemaining);
            Assert.IsTrue(clock.IsHeld);

            clock.Advance(Frame, held: false);
            Assert.IsFalse(clock.IsHeld, "The clock should run again as soon as the hold ends.");
            Assert.AreEqual(60f - Frame, clock.TimeRemaining);
        }

        [Test]
        public void ClockNeverGoesBelowZero()
        {
            var clock = new RoundClock(From);
            clock.Reset(1f);

            clock.Advance(5f, held: false);

            Assert.AreEqual(0f, clock.TimeRemaining);
            Assert.IsTrue(clock.HasRunOut);
            Assert.AreEqual(CountdownCue.None, clock.Advance(Frame, held: false), "Nothing more after the horn.");
        }

        [Test]
        public void WholeRound_WithoutHolds_PlaysEachCueOnce()
        {
            var clock = new RoundClock(From);
            clock.Reset(60f);
            var cues = new List<(CountdownCue, int)>();

            Run(clock, frames: 60 * 4, held: false, cues);

            Assert.AreEqual(FullCountdown(), cues);
        }

        // A hold in the middle of the countdown: the second it stopped on keeps its one tick,
        // and the ticks after it still come once each, at the right seconds.
        [TestCase(5.5f)]
        [TestCase(3f)]
        [TestCase(1f)]
        [TestCase(0.25f)]
        public void HoldDuringTheCountdown_NeitherSkipsNorRepeatsACue(float holdAt)
        {
            var clock = new RoundClock(From);
            clock.Reset(10f);
            var cues = new List<(CountdownCue, int)>();

            Run(clock, frames: (int)((10f - holdAt) / Frame), held: false, cues);
            Assert.AreEqual(holdAt, clock.TimeRemaining);
            Run(clock, frames: 40, held: true, cues);
            Assert.AreEqual(holdAt, clock.TimeRemaining, "No time should pass during a hold.");
            Run(clock, frames: 100, held: false, cues);

            Assert.AreEqual(FullCountdown(), cues);
        }

        [Test]
        public void ManyHolds_StillPlayEachCueOnce()
        {
            var clock = new RoundClock(From);
            clock.Reset(10f);
            var cues = new List<(CountdownCue, int)>();

            // A frame of play, then a hold, over and over: like a run of chains.
            while (!clock.HasRunOut)
            {
                Run(clock, frames: 1, held: false, cues);
                Run(clock, frames: 3, held: true, cues);
            }

            Assert.AreEqual(FullCountdown(), cues);
        }

        // Pausing (Esc) during a hold: the GameManager stops calling Advance at all, so the
        // clock just picks up where it was. Here that's "no calls for a while, then more held
        // frames, then the hold ends".
        [Test]
        public void PauseDuringAHold_ThenResume_CarriesOnFromTheSameTime()
        {
            var clock = new RoundClock(From);
            clock.Reset(10f);
            var cues = new List<(CountdownCue, int)>();

            Run(clock, frames: 26, held: false, cues);
            float atHold = clock.TimeRemaining;
            Run(clock, frames: 4, held: true, cues);
            // ... paused: no Advance calls ...
            Run(clock, frames: 4, held: true, cues);
            Assert.AreEqual(atHold, clock.TimeRemaining);
            Assert.IsTrue(clock.IsHeld);
            Run(clock, frames: 100, held: false, cues);

            Assert.AreEqual(FullCountdown(), cues);
        }

        [Test]
        public void Reset_StartsAFreshCountdown()
        {
            var clock = new RoundClock(From);
            clock.Reset(10f);
            var first = new List<(CountdownCue, int)>();
            Run(clock, frames: 100, held: false, first);

            clock.Reset(10f);
            Assert.AreEqual(10f, clock.TimeRemaining);
            Assert.IsFalse(clock.HasRunOut);
            var second = new List<(CountdownCue, int)>();
            Run(clock, frames: 100, held: false, second);

            Assert.AreEqual(FullCountdown(), second, "Last round's 0:00 shouldn't carry over into the new one.");
        }
    }
}
