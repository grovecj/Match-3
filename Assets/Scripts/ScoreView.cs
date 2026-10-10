using System.Globalization;
using TMPro;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Displays the <see cref="ScoreKeeper"/>'s total in a TextMeshPro text, formatted with
    /// thousands separators (e.g. "12,000"), and the cascade multiplier ("x3") while it's above
    /// x1. It only displays; it never changes the score.
    /// </summary>
    public class ScoreView : MonoBehaviour
    {
        [SerializeField] private ScoreKeeper scoreKeeper;
        // TMP_Text is the base class of both TextMeshProUGUI (UI text on a Canvas) and
        // TextMeshPro (text in the world), so either kind can be dragged in here.
        [SerializeField] private TMP_Text valueText;

        [Tooltip("Shows the cascade multiplier (\"x3\") while it's above x1. Optional: if empty, one is made at the right-hand end of the Value Text's row when the game starts.")]
        [SerializeField] private TMP_Text multiplierText;

        [Tooltip("Colour of the multiplier, so it stands out from the score.")]
        [SerializeField] private Color multiplierColor = new Color(1f, 0.84f, 0.3f);

        private void Awake()
        {
            if (multiplierText == null && valueText != null)
            {
                multiplierText = CreateMultiplierText(valueText);
            }
        }

        private void OnEnable()
        {
            if (scoreKeeper == null || valueText == null)
            {
                Debug.LogError("ScoreView: assign a Score Keeper and a Value Text.", this);
                return;
            }
            scoreKeeper.Changed += Show;
            scoreKeeper.MultiplierChanged += ShowMultiplier;
            // Show the current values right away rather than waiting for the first change.
            Show(scoreKeeper.Total);
            ShowMultiplier(scoreKeeper.Multiplier);
        }

        private void OnDisable()
        {
            if (scoreKeeper != null)
            {
                scoreKeeper.Changed -= Show;
                scoreKeeper.MultiplierChanged -= ShowMultiplier;
            }
        }

        private void Show(int total)
        {
            // "N0" = number with group separators and 0 decimal places. The separator depends on
            // the culture (German uses "12.000"), so we pin it to the invariant culture to always
            // get "12,000".
            valueText.text = total.ToString("N0", CultureInfo.InvariantCulture);
        }

        private void ShowMultiplier(int multiplier)
        {
            // x1 means "no multiplier", so there's nothing worth showing. Switching the whole
            // GameObject off hides it without losing its text or layout.
            bool active = multiplier > 1;
            multiplierText.gameObject.SetActive(active);
            if (active)
            {
                multiplierText.text = "x" + multiplier;
            }
        }

        /// <summary>
        /// Makes the multiplier's text by copying <paramref name="source"/> (so it has the same
        /// font and size, and sits on the same line) and aligning the copy to the right-hand end
        /// of that row.
        /// </summary>
        // Instantiate works on any GameObject, not just prefabs: it clones it, children and all.
        // Passing a parent puts the clone under it; `false` keeps the clone's local position,
        // anchors and size the same as the original's, which is what we want here.
        private TMP_Text CreateMultiplierText(TMP_Text source)
        {
            TMP_Text text = Instantiate(source, source.transform.parent, false);
            text.name = "MultiplierValue";
            // The score is centred across the row, so the right-hand end of it is free.
            text.horizontalAlignment = HorizontalAlignmentOptions.Right;
            text.color = multiplierColor;
            text.raycastTarget = false;
            text.gameObject.SetActive(false);
            return text;
        }
    }
}
