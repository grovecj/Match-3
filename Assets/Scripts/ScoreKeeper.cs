using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Keeps the running score and the cascade multiplier. It listens to the
    /// <see cref="Board"/>'s <see cref="Board.ClearStepScored"/> event and adds what each clear
    /// step is worth, so the Board never has to know that scoring exists. Anything that wants
    /// to show the score (the UI) listens to <see cref="Changed"/> and
    /// <see cref="MultiplierChanged"/> in turn.
    ///
    /// The arithmetic itself lives in <see cref="ScoreRules"/>, where the tests can reach it.
    /// </summary>
    public class ScoreKeeper : MonoBehaviour
    {
        [SerializeField] private Board board;

        /// <summary>Fired whenever the total changes, with the new total.</summary>
        public event System.Action<int> Changed;

        /// <summary>Fired whenever the multiplier changes, with the new multiplier (1 = none).</summary>
        public event System.Action<int> MultiplierChanged;

        /// <summary>The current score.</summary>
        // "{ get; private set; }" is an auto-property: anyone can read it, only this class can set it.
        public int Total { get; private set; }

        /// <summary>
        /// The multiplier of the latest clear step: 1 for the player's own match, 2 for the
        /// first cascade, and so on (see <see cref="ScoreRules.Multiplier"/>). Back to 1 as soon
        /// as the player swaps again.
        /// </summary>
        public int Multiplier { get; private set; } = 1;

        // OnEnable/OnDisable run when the component is switched on/off (including when the scene
        // loads and unloads). Subscribing in one and unsubscribing in the other is the standard
        // pattern: it guarantees we never get callbacks while disabled, and that the Board
        // doesn't keep a reference to us after we've been destroyed.
        private void OnEnable()
        {
            if (board == null)
            {
                Debug.LogError("ScoreKeeper: no Board assigned.", this);
                return;
            }
            board.ClearStepScored += OnClearStepScored;
            board.SwapStarted += OnSwapStarted;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.ClearStepScored -= OnClearStepScored;
                board.SwapStarted -= OnSwapStarted;
            }
        }

        /// <summary>Sets the score back to 0 (e.g. when a new round starts).</summary>
        public void ResetScore()
        {
            SetTotal(0);
            SetMultiplier(1);
        }

        // Called by the Board once per clear step, once its chain reaction is over. Cascades are
        // steps too, so they score with no extra code here; their cascade index is what makes
        // them worth more.
        private void OnClearStepScored(ClearStep step)
        {
            SetMultiplier(ScoreRules.Multiplier(step.CascadeIndex));
            SetTotal(Total + step.Points);
        }

        // A new swap starts a new chain, so the multiplier starts again from x1.
        private void OnSwapStarted()
        {
            SetMultiplier(1);
        }

        private void SetTotal(int total)
        {
            Total = total;
            Changed?.Invoke(Total);
        }

        private void SetMultiplier(int multiplier)
        {
            // Only announce real changes: a run of x1 steps shouldn't make the UI redraw each time.
            if (multiplier == Multiplier)
            {
                return;
            }
            Multiplier = multiplier;
            MultiplierChanged?.Invoke(Multiplier);
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>Sets the Board. Call it while the GameObject is still inactive.</summary>
        internal void ConfigureForTests(Board board)
        {
            this.board = board;
        }
    }
}
