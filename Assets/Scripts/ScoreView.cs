using System.Globalization;
using TMPro;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Displays the <see cref="ScoreKeeper"/>'s total in a TextMeshPro text, formatted with
    /// thousands separators (e.g. "12,000"). It only displays; it never changes the score.
    /// </summary>
    public class ScoreView : MonoBehaviour
    {
        [SerializeField] private ScoreKeeper scoreKeeper;
        // TMP_Text is the base class of both TextMeshProUGUI (UI text on a Canvas) and
        // TextMeshPro (text in the world), so either kind can be dragged in here.
        [SerializeField] private TMP_Text valueText;

        private void OnEnable()
        {
            if (scoreKeeper == null || valueText == null)
            {
                Debug.LogError("ScoreView: assign a Score Keeper and a Value Text.", this);
                return;
            }
            scoreKeeper.Changed += Show;
            // Show the current value right away rather than waiting for the first change.
            Show(scoreKeeper.Total);
        }

        private void OnDisable()
        {
            if (scoreKeeper != null)
            {
                scoreKeeper.Changed -= Show;
            }
        }

        private void Show(int total)
        {
            // "N0" = number with group separators and 0 decimal places. The separator depends on
            // the culture (German uses "12.000"), so we pin it to the invariant culture to always
            // get "12,000".
            valueText.text = total.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
