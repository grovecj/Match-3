using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Plays a quick sound whenever the player swaps two tokens, and a soft "nope" when the swap
    /// makes no match and the tokens slide back. So a valid swap sounds like swap -> match, and
    /// an invalid one like swap -> invalid.
    ///
    /// Like <see cref="MatchSounds"/>, it only listens to the <see cref="Board"/>'s events, so
    /// the Board never has to know that sound exists.
    /// </summary>
    public class SwapSounds : MonoBehaviour
    {
        [SerializeField] private Board board;
        [SerializeField] private AudioManager audioManager;

        // [Range] turns the field into a slider in the Inspector, limited to 0-1.
        [Tooltip("How loud the swap sound is, relative to the master volume. It plays on every " +
                 "swap, so it's kept quiet to not get tiring.")]
        [SerializeField, Range(0f, 1f)] private float swapVolume = 0.5f;
        [Tooltip("How loud the invalid-swap sound is, relative to the master volume.")]
        [SerializeField, Range(0f, 1f)] private float invalidSwapVolume = 0.8f;

        // Subscribe/unsubscribe in OnEnable/OnDisable, the same pattern as MatchSounds.
        private void OnEnable()
        {
            if (board == null || audioManager == null)
            {
                Debug.LogError("SwapSounds: needs both a Board and an AudioManager assigned.", this);
                return;
            }
            board.SwapStarted += OnSwapStarted;
            board.SwapRejected += OnSwapRejected;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.SwapStarted -= OnSwapStarted;
                board.SwapRejected -= OnSwapRejected;
            }
        }

        private void OnSwapStarted()
        {
            audioManager.Play(SoundId.Swap, volumeScale: swapVolume);
        }

        private void OnSwapRejected()
        {
            audioManager.Play(SoundId.InvalidSwap, volumeScale: invalidSwapVolume);
        }
    }
}
