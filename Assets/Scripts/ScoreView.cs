using System.Globalization;
using TMPro;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Displays the <see cref="ScoreKeeper"/>'s total in a TextMeshPro text, formatted with
    /// thousands separators (e.g. "12,000"), and the cascade multiplier ("x3") while it's above
    /// x1. Under it, once a round has been finished, the personal best ("Best 84,000"), which
    /// turns into "New best!" as soon as the score passes it. It only displays; it never
    /// changes the score or the best.
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

        [Tooltip("The \"Best 84,000\" line under the score. Optional: without it, the best isn't shown during play. Made by Match3 > Build Round Summary.")]
        [SerializeField] private TMP_Text bestText;

        [Tooltip("Colour of the best line once the score has passed it (\"New best!\").")]
        [SerializeField] private Color newBestColor = new Color(1f, 0.84f, 0.3f);

        // The best to beat this round, or null before the first finished round.
        private int? best;
        // The best line's colour from the scene, for when it isn't a new best.
        private Color bestColor = Color.white;

        private void Awake()
        {
            if (multiplierText == null && valueText != null)
            {
                multiplierText = CreateMultiplierText(valueText);
            }
            if (bestText != null)
            {
                bestColor = bestText.color;
                ShowBestLine(0);
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
            ShowBestLine(total);
        }

        /// <summary>
        /// Sets the best this round's score is up against (null for none yet). The
        /// <see cref="GameManager"/> calls it when a round starts, not when one ends, so a
        /// round that set a new best keeps saying "New best!" until the next one begins.
        /// </summary>
        public void ShowBest(int? newBest)
        {
            best = newBest;
            ShowBestLine(scoreKeeper != null ? scoreKeeper.Total : 0);
        }

        private void ShowBestLine(int total)
        {
            if (bestText == null)
            {
                return;
            }
            // Nothing to show before the first finished round.
            bestText.gameObject.SetActive(best.HasValue);
            if (!best.HasValue)
            {
                return;
            }
            // The same rule as the round summary, so the two never disagree (a tie isn't a new best).
            bool beaten = PersonalBest.Beats(total, best);
            bestText.text = beaten ? "New best!" : "Best " + best.Value.ToString("N0", CultureInfo.InvariantCulture);
            bestText.color = beaten ? newBestColor : bestColor;
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
        /// font and size), placing the copy inside it and aligning it to the right-hand end, so it
        /// sits on the same line and moves with the score when the layout changes (issue #114).
        /// </summary>
        // Instantiate works on any GameObject, not just prefabs: it clones it, children and all.
        private TMP_Text CreateMultiplierText(TMP_Text source)
        {
            TMP_Text text = Instantiate(source, source.transform, false);
            // Fill the score's own rectangle exactly.
            RectTransform rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
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
