using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Keeps the running score. It listens to the <see cref="Board"/>'s
    /// <see cref="Board.MatchCleared"/> event and adds points for each cleared run, so the Board
    /// never has to know that scoring exists. Anything that wants to show the score (the UI)
    /// listens to <see cref="Changed"/> in turn.
    /// </summary>
    public class ScoreKeeper : MonoBehaviour
    {
        [SerializeField] private Board board;

        [Header("Points per Run")]
        [SerializeField, Min(0)] private int pointsForThree = 1000;
        [SerializeField, Min(0)] private int pointsForFour = 2000;
        [SerializeField, Min(0)] private int pointsForFiveOrMore = 5000;

        /// <summary>Fired whenever the total changes, with the new total.</summary>
        public event System.Action<int> Changed;

        /// <summary>The current score.</summary>
        // "{ get; private set; }" is an auto-property: anyone can read it, only this class can set it.
        public int Total { get; private set; }

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
            board.MatchCleared += OnMatchCleared;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.MatchCleared -= OnMatchCleared;
            }
        }

        /// <summary>Sets the score back to 0 (e.g. when a new round starts).</summary>
        public void ResetScore()
        {
            SetTotal(0);
        }

        // Called by the Board once per cleared run. Cascades fire the same event, so they score
        // automatically with no extra code here.
        private void OnMatchCleared(int runLength)
        {
            SetTotal(Total + PointsForRun(runLength));
        }

        /// <summary>Points awarded for a single run of <paramref name="runLength"/> tokens.</summary>
        private int PointsForRun(int runLength)
        {
            if (runLength >= 5)
            {
                return pointsForFiveOrMore;
            }
            if (runLength == 4)
            {
                return pointsForFour;
            }
            return pointsForThree;
        }

        private void SetTotal(int total)
        {
            Total = total;
            Changed?.Invoke(Total);
        }
    }
}
