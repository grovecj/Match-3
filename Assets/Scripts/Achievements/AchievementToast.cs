using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Match3.Achievements
{
    /// <summary>
    /// The "Achievement unlocked" toast: a small card that slides down from the top of the
    /// screen, stays a few seconds, and slides away, with a sound. If several achievements
    /// unlock at once, they queue up and are shown one after another.
    ///
    /// It never blocks anything: it has no buttons and ignores clicks, so the round-end screen
    /// underneath works as usual.
    /// </summary>
    public class AchievementToast : MonoBehaviour
    {
        [SerializeField] private AchievementsClient client;
        [SerializeField] private AudioManager audioManager;
        [Tooltip("Which sprite to show for each icon name.")]
        [SerializeField] private AchievementIcons icons;

        [Header("Card")]
        [Tooltip("The card that slides in. Anchored to the top of the screen; hidden between toasts.")]
        [SerializeField] private RectTransform card;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("Timing")]
        [Tooltip("How far below the top edge of the screen the card rests.")]
        [SerializeField, Min(0f)] private float topMargin = 24f;
        [SerializeField, Min(0.01f)] private float slideDuration = 0.35f;
        [SerializeField, Min(0f)] private float showDuration = 3f;

        // A Queue hands things back in the order they were added: first in, first out.
        private readonly Queue<Achievement> waiting = new Queue<Achievement>();
        private bool showing;

        private void Awake()
        {
            if (client == null || audioManager == null || icons == null || card == null || icon == null ||
                nameText == null || descriptionText == null)
            {
                Debug.LogError("AchievementToast: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }
            client.Unlocked += OnUnlocked;
            card.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (client != null)
            {
                client.Unlocked -= OnUnlocked;
            }
        }

        private void OnUnlocked(Achievement achievement)
        {
            waiting.Enqueue(achievement);
            if (!showing && isActiveAndEnabled)
            {
                StartCoroutine(ShowQueue());
            }
        }

        private IEnumerator ShowQueue()
        {
            showing = true;
            while (waiting.Count > 0)
            {
                Achievement achievement = waiting.Dequeue();
                Sprite sprite = icons.Get(achievement.icon);
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                nameText.text = achievement.name;
                descriptionText.text = achievement.description;

                // The card's pivot is its top edge, and it's anchored to the top of the screen,
                // so y = 0 has it touching the top edge from below, and y = its own height puts
                // it just out of sight above the screen.
                float hiddenY = card.rect.height;
                float shownY = -topMargin;

                card.gameObject.SetActive(true);
                audioManager.Play(SoundId.AchievementUnlocked);
                yield return Slide(hiddenY, shownY);
                // The "Realtime" wait ignores Time.timeScale, like the slide below.
                yield return new WaitForSecondsRealtime(showDuration);
                yield return Slide(shownY, hiddenY);
                card.gameObject.SetActive(false);
            }
            showing = false;
        }

        private IEnumerator Slide(float fromY, float toY)
        {
            for (float elapsed = 0f; elapsed < slideDuration; elapsed += Time.unscaledDeltaTime)
            {
                SetY(Mathf.LerpUnclamped(fromY, toY, Easing.OutCubic(elapsed / slideDuration)));
                // Time.unscaledDeltaTime is real time, so the toast keeps moving even if the
                // game is paused (Time.timeScale = 0), as UI should.
                yield return null;
            }
            SetY(toY);
        }

        private void SetY(float y)
        {
            Vector2 position = card.anchoredPosition;
            position.y = y;
            card.anchoredPosition = position;
        }
    }
}
