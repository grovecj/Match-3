using System.Globalization;
using TMPro;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// One line of the Leaderboard panel: rank, initials and score, plus a highlight behind the
    /// line for the entry that was just added. Like the other views it only displays what the
    /// <see cref="LeaderboardMenu"/> hands it.
    /// </summary>
    public class LeaderboardRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private TMP_Text initialsText;
        [SerializeField] private TMP_Text scoreText;
        [Tooltip("Shown only for the entry that was just added.")]
        [SerializeField] private GameObject highlight;

        /// <summary>
        /// Shows <paramref name="entry"/> at <paramref name="rank"/> (1 = best). A null entry is
        /// an empty slot, shown as "---".
        /// </summary>
        public void Show(int rank, LeaderboardEntry entry, bool highlighted)
        {
            rankText.text = rank.ToString(CultureInfo.InvariantCulture);
            initialsText.text = entry != null ? entry.Initials : "---";
            scoreText.text = entry != null ? entry.Score.ToString("N0", CultureInfo.InvariantCulture) : "---";
            highlight.SetActive(highlighted);
        }
    }
}
