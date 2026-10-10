using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The round's clock: how much game time is left, and which countdown sound (if any) each
    /// step of it should play. It knows nothing about scenes or UI; the GameManager calls
    /// <see cref="Advance"/> once a frame and shows the result.
    ///
    /// "Game time" is not the same as wall-clock time. The clock only moves when it's told to,
    /// and it can be held (while the board reshuffles, or while power gems go off), so a round
    /// that shows 60 seconds can take a good deal longer than a minute to play.
    /// </summary>
    // A plain C# class (not a MonoBehaviour) so the Edit Mode tests can run a whole round's
    // worth of time through it with numbers alone: no scene, no frames, no audio.
    public class RoundClock
    {
        private readonly int countdownFrom;

        // The whole second shown after the last Advance, so we can tell when it changes.
        private int lastSecondShown;

        /// <param name="countdownFrom">A tick plays on reaching each second from this one down to 1.</param>
        public RoundClock(int countdownFrom)
        {
            this.countdownFrom = countdownFrom;
        }

        /// <summary>Seconds of game time left in the round. Never below 0.</summary>
        public float TimeRemaining { get; private set; }

        /// <summary>True if the last <see cref="Advance"/> was told to hold the clock.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>
        /// The whole seconds the timer display shows. Rounded up, so the display reads 1:00 for
        /// the whole first second and only shows 0:00 once time has really run out.
        /// </summary>
        public int SecondsShown => Mathf.CeilToInt(TimeRemaining);

        /// <summary>True once the clock has run all the way down.</summary>
        public bool HasRunOut => TimeRemaining <= 0f;

        /// <summary>
        /// Sets the clock to <paramref name="duration"/> seconds, for a new round. The countdown
        /// starts from the full clock, so last round's 0:00 isn't remembered.
        /// </summary>
        public void Reset(float duration)
        {
            TimeRemaining = Mathf.Max(0f, duration);
            lastSecondShown = SecondsShown;
            IsHeld = false;
        }

        /// <summary>
        /// Runs the clock forward by <paramref name="deltaTime"/> seconds, unless
        /// <paramref name="held"/> is true, and returns the countdown sound to play for it.
        /// </summary>
        /// <remarks>
        /// A held step changes nothing but <see cref="IsHeld"/>: no time passes, and since the
        /// displayed second doesn't change, no cue plays. When the hold ends, the clock carries
        /// on from the same second it stopped on, so no tick is skipped or played twice.
        /// </remarks>
        public CountdownCue Advance(float deltaTime, bool held)
        {
            IsHeld = held;
            if (held || HasRunOut)
            {
                return CountdownCue.None;
            }

            TimeRemaining = Mathf.Max(0f, TimeRemaining - deltaTime);
            int second = SecondsShown;
            CountdownCue cue = GameManager.GetCountdownCue(lastSecondShown, second, countdownFrom);
            lastSecondShown = second;
            return cue;
        }
    }
}
