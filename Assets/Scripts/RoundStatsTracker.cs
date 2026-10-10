using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Keeps the current round's <see cref="RoundStats"/> by listening to the
    /// <see cref="Board"/>'s events, the same way <see cref="ScoreKeeper"/> keeps the score. It
    /// hears exactly the matches the score does, so the two always agree.
    ///
    /// The <see cref="Online.OnlineLeaderboardClient"/> resets it when a round starts and reads
    /// it when the round ends.
    /// </summary>
    public class RoundStatsTracker : MonoBehaviour
    {
        [SerializeField] private Board board;

        private readonly RoundStats stats = new RoundStats();

        /// <summary>The round so far, as a copy (so what's sent can't change while it's on its way).</summary>
        public RoundStats Stats => stats.Copy();

        /// <summary>Starts counting from zero. Call when a round starts.</summary>
        public void ResetStats()
        {
            stats.Reset();
        }

        // Subscribe/unsubscribe in OnEnable/OnDisable, the same pattern as ScoreKeeper.
        private void OnEnable()
        {
            if (board == null)
            {
                Debug.LogError("RoundStatsTracker: needs a Board assigned.", this);
                return;
            }
            // The RoundStats methods have the same shape as these events (no arguments, or one
            // int), so they can be subscribed directly. ClearStepResolved passes two ints and we
            // only want the second, so that one goes through a method of our own.
            board.SwapStarted += stats.SwapStarted;
            board.MatchCleared += stats.RunCleared;
            board.ClearStepResolved += OnClearStepResolved;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.SwapStarted -= stats.SwapStarted;
                board.MatchCleared -= stats.RunCleared;
                board.ClearStepResolved -= OnClearStepResolved;
            }
        }

        private void OnClearStepResolved(int longestRun, int cascadeIndex)
        {
            stats.StepResolved(cascadeIndex);
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>Sets the Board. Call it while the GameObject is still inactive.</summary>
        internal void ConfigureForTests(Board board)
        {
            this.board = board;
        }
    }
}
