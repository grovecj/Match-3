using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Match3.Achievements
{
    /// <summary>
    /// One line of the Achievements panel: icon, name, how to earn it, and a progress bar.
    /// An unlocked one is bright with a gold icon; a locked one is dimmed. The
    /// <see cref="AchievementsMenu"/> makes these from a prefab, one per achievement.
    /// </summary>
    public class AchievementRow : MonoBehaviour
    {
        private const float LockedAlpha = 0.55f;

        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text descriptionText;
        [Tooltip("\"23/50\" while locked, \"Unlocked\" once earned.")]
        [SerializeField] private TMP_Text progressText;
        [Tooltip("The colored part of the progress bar. It's stretched across the bar by its anchors.")]
        [SerializeField] private RectTransform progressFill;
        [Tooltip("Fades the whole row while the achievement is locked.")]
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Color unlockedColor = new Color(1f, 0.8f, 0.25f);
        [SerializeField] private Color lockedColor = Color.white;

        public void Show(Achievement achievement, Sprite sprite)
        {
            bool unlocked = achievement.IsUnlocked;

            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.color = unlocked ? unlockedColor : lockedColor;
            nameText.text = achievement.name;
            nameText.color = unlocked ? unlockedColor : lockedColor;
            descriptionText.text = achievement.description;
            progressText.text = unlocked ? "Unlocked" : ProgressLabel(achievement);

            // A RectTransform's anchors are fractions of its parent: (0,0) is the parent's
            // bottom-left corner, (1,1) its top-right. Moving the right-hand anchor to, say,
            // 0.4 makes the fill cover the left 40% of the bar, whatever size the bar is.
            progressFill.anchorMax = new Vector2(achievement.Fraction, 1f);

            // A CanvasGroup's alpha fades everything under it at once.
            group.alpha = unlocked ? 1f : LockedAlpha;
        }

        /// <summary>"23/50", with thousands separators: "21,000/1,000,000".</summary>
        public static string ProgressLabel(Achievement achievement)
        {
            return achievement.progress.ToString("N0", CultureInfo.InvariantCulture) + "/" +
                achievement.goal.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
