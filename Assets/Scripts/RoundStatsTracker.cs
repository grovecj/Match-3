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
            // Where a RoundStats method has the same shape as the event (no arguments, one int,
            // one ClearStep), it can be subscribed directly. The others pass more than the stats
            // need, so they go through small methods of our own.
            board.SwapStarted += stats.SwapStarted;
            board.LastHurrahStarted += stats.LastHurrahStarted;
            board.MatchCleared += stats.RunCleared;
            board.ClearStepScored += stats.StepScored;
            board.PowerGemCreated += OnPowerGemCreated;
            board.PowerGemDetonated += OnPowerGemDetonated;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.SwapStarted -= stats.SwapStarted;
                board.LastHurrahStarted -= stats.LastHurrahStarted;
                board.MatchCleared -= stats.RunCleared;
                board.ClearStepScored -= stats.StepScored;
                board.PowerGemCreated -= OnPowerGemCreated;
                board.PowerGemDetonated -= OnPowerGemDetonated;
            }
        }

        // The power gem events also say where the gem was (and which wave it went off in); the
        // stats only need to know which kind.
        private void OnPowerGemCreated(PowerKind kind, Vector2Int cell)
        {
            stats.PowerGemCreated(kind);
        }

        private void OnPowerGemDetonated(PowerKind kind, Vector2Int cell, int wave)
        {
            stats.PowerGemDetonated(kind);
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>Sets the Board. Call it while the GameObject is still inactive.</summary>
        internal void ConfigureForTests(Board board)
        {
            this.board = board;
        }
    }
}
