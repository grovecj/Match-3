using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Plays a sound for every clear step on the <see cref="Board"/>: a bigger match gets a more
    /// exciting sound, and each cascade in the same turn plays it a little higher, so chain
    /// reactions sound like they're building up.
    ///
    /// Like <see cref="ScoreKeeper"/>, it only listens to the Board's events, so the Board never
    /// has to know that sound exists. It asks the <see cref="AudioManager"/> to play by
    /// <see cref="SoundId"/> and never touches clips itself.
    /// </summary>
    public class MatchSounds : MonoBehaviour
    {
        /// <summary>The highest a cascade can raise the pitch, in semitones (6 = half an octave).</summary>
        public const int MaxCascadeSemitones = 6;

        [SerializeField] private Board board;
        [SerializeField] private AudioManager audioManager;

        // Subscribe/unsubscribe in OnEnable/OnDisable, the same pattern as ScoreKeeper.
        private void OnEnable()
        {
            if (board == null || audioManager == null)
            {
                Debug.LogError("MatchSounds: needs both a Board and an AudioManager assigned.", this);
                return;
            }
            board.ClearStepResolved += OnClearStepResolved;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.ClearStepResolved -= OnClearStepResolved;
            }
        }

        // Called by the Board once per clear step, however many runs that step had.
        private void OnClearStepResolved(int longestRun, int cascadeIndex)
        {
            audioManager.Play(SoundForRun(longestRun), PitchForCascade(cascadeIndex));
        }

        // The two mappings below are `static` (they use nothing but their argument) and
        // `public`, so the Edit Mode tests can check them directly, with no scene, Board or audio.

        /// <summary>
        /// The sound for a step whose longest run is <paramref name="runLength"/> tokens. Uses
        /// the same grouping as the score: 3, 4, and 5 or more.
        /// </summary>
        public static SoundId SoundForRun(int runLength)
        {
            if (runLength >= 5)
            {
                return SoundId.Match5Plus;
            }
            if (runLength == 4)
            {
                return SoundId.Match4;
            }
            return SoundId.Match3;
        }

        /// <summary>
        /// The pitch for the <paramref name="cascadeIndex"/>th clear step of a turn: 1 (normal)
        /// for the player's own match, then one semitone higher per cascade, up to
        /// <see cref="MaxCascadeSemitones"/>.
        /// </summary>
        public static float PitchForCascade(int cascadeIndex)
        {
            // Pitch is a speed multiplier: 2 plays the clip twice as fast, which sounds exactly
            // one octave higher. An octave is 12 semitones spaced evenly on that multiplying
            // scale, so n semitones up is 2^(n/12). (Faster also means a bit shorter, which is
            // barely noticeable at +6 semitones: the clip plays about 30% quicker.)
            int semitones = Mathf.Clamp(cascadeIndex, 0, MaxCascadeSemitones);
            return Mathf.Pow(2f, semitones / 12f);
        }
    }
}
