using TMPro;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Shows a <see cref="RoundSummary"/> in the card over the board at the end of a round:
    /// the score line (in gold for a new best) and the stat lines under it. The
    /// <see cref="GameManager"/> calls <see cref="Show"/> when a round ends and
    /// <see cref="Hide"/> when the next one starts.
    ///
    /// Built and wired by Match3 > Build Round Summary. It never blocks clicks, so the
    /// initials prompt, the menus and the Play Again button all work while it's up.
    /// </summary>
    public class RoundSummaryView : MonoBehaviour
    {
        [Tooltip("What's shown and hidden: the card's root.")]
        [SerializeField] private GameObject card;
        [SerializeField] private TMP_Text scoreLineText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Color scoreColor = Color.white;
        [SerializeField] private Color newBestColor = new Color(1f, 0.8f, 0.25f);

        private void Awake()
        {
            if (card == null || scoreLineText == null || statsText == null)
            {
                Debug.LogError("RoundSummaryView: assign the Card, Score Line Text and Stats Text.", this);
                enabled = false;
                return;
            }
            Hide();
        }

        public void Show(RoundSummary summary)
        {
            if (!enabled)
            {
                return;
            }
            scoreLineText.text = summary.ScoreLine;
            scoreLineText.color = summary.IsNewBest ? newBestColor : scoreColor;
            statsText.text = string.Join("\n", summary.StatLines);
            card.SetActive(true);
        }

        public void Hide()
        {
            if (card != null)
            {
                card.SetActive(false);
            }
        }
    }
}
