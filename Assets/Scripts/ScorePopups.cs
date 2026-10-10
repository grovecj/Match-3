using UnityEngine;
using UnityEngine.Pool;

namespace Match3
{
    /// <summary>
    /// Shows a floating number (a <see cref="ScorePopup"/>) for every clear step: the points it
    /// scored, multiplier included, in the middle of the gems it cleared. Listens to the
    /// <see cref="Board"/>'s <see cref="Board.ClearStepScored"/>, the same event the score
    /// itself comes from, so the popup always shows exactly what was added.
    ///
    /// Popups are pooled, like the power gem effects: a finished one is switched off and kept
    /// for the next clear rather than destroyed.
    /// </summary>
    public class ScorePopups : MonoBehaviour
    {
        [SerializeField] private Board board;

        // Popups are drawn this far in front of the tokens: above the beams and particles too,
        // so the number is never hidden by the blast that earned it.
        private const int OrderOffset = 30;

        private ObjectPool<ScorePopup> pool;

        private void Awake()
        {
            pool = new ObjectPool<ScorePopup>(
                createFunc: CreatePopup,
                actionOnGet: popup => popup.gameObject.SetActive(true),
                actionOnRelease: popup => popup.gameObject.SetActive(false),
                actionOnDestroy: popup => Destroy(popup.gameObject),
                collectionCheck: false,
                defaultCapacity: 4,
                maxSize: 16);
        }

        private void OnEnable()
        {
            if (board == null)
            {
                Debug.LogError("ScorePopups: needs a Board assigned.", this);
                return;
            }
            board.ClearStepScored += OnClearStepScored;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.ClearStepScored -= OnClearStepScored;
            }
        }

        private void OnClearStepScored(ClearStep step)
        {
            int points = step.Points;
            if (points <= 0)
            {
                return;
            }

            SpriteRenderer tokenRenderer = board.TokenRenderer;
            int layer = tokenRenderer != null ? tokenRenderer.sortingLayerID : 0;
            int order = (tokenRenderer != null ? tokenRenderer.sortingOrder : 0) + OrderOffset;

            ScorePopup popup = pool.Get();
            popup.Show(board.GridToWorld(step.CenterCell), points, ScoreRules.Multiplier(step.CascadeIndex),
                board.CellSize, layer, order);
        }

        /// <summary>Makes a new popup, in code: a GameObject with world-space text on it.</summary>
        private ScorePopup CreatePopup()
        {
            var popupObject = new GameObject("Score Popup");
            popupObject.transform.SetParent(transform, false);
            // AddComponent<ScorePopup> adds the TextMeshPro too ([RequireComponent]). The text
            // picks up the project's default TMP font by itself.
            ScorePopup popup = popupObject.AddComponent<ScorePopup>();
            popup.Finished += finished => pool.Release(finished);
            return popup;
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>Sets the Board the scene would assign. Call it while the GameObject is still inactive.</summary>
        internal void ConfigureForTests(Board board)
        {
            this.board = board;
        }
    }
}
