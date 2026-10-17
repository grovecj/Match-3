using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// The "How to play" card (issue #112): one window that shows which shapes make which power
    /// gems, how combos and cascades score, and what the Last Hurrah is, each with a picture
    /// drawn in the player's own token style and board colours.
    ///
    /// It opens from two buttons: one in the side panel, which the GameManager only shows
    /// between rounds (<see cref="SetAvailable"/>, like the other menus), and one on the pause
    /// overlay. Opening it from pause doesn't resume the game: the card just sits on top of
    /// the overlay, and closing it uncovers the overlay again. Both buttons pulse until the
    /// card has been opened once, on this device.
    ///
    /// It only closes with its Close button, not Esc: Esc is the pause key, and in a browser
    /// it leaves fullscreen before the game sees it.
    ///
    /// Built by Match3 > Build How To Play.
    /// </summary>
    public class HowToPlayMenu : MonoBehaviour
    {
        /// <summary>PlayerPrefs key: 1 once the card has been opened, which stops the pulsing.</summary>
        public const string SeenKey = "HowToPlay.Seen";

        [Tooltip("Where the current token style and board theme come from.")]
        [SerializeField] private ThemeManager themeManager;
        [Tooltip("Where the token types come from (the pictures pick their colours from its list).")]
        [SerializeField] private Board board;

        [Header("Buttons")]
        [Tooltip("The How to play button in the side panel. Shown between rounds only.")]
        [SerializeField] private Button sidePanelButton;
        [Tooltip("The How to play button on the pause overlay. Optional; shown with the overlay.")]
        [SerializeField] private Button pauseButton;
        [Tooltip("How much the buttons grow and shrink while pulsing, as a fraction of their size.")]
        [SerializeField, Range(0f, 0.2f)] private float pulseAmount = 0.06f;
        [Tooltip("Pulses per second.")]
        [SerializeField, Min(0.1f)] private float pulseRate = 1.2f;

        [Header("Window")]
        [Tooltip("The full-screen modal (dimmed backdrop + window).")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button closeButton;
        [Tooltip("Every picture cell in the window, redrawn each time it opens or the theme changes.")]
        [SerializeField] private HowToPlayIcon[] icons = new HowToPlayIcon[0];

        // Read once from PlayerPrefs in Awake, then kept here, so Update doesn't ask every frame.
        private bool seen;

        public bool IsOpen => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (themeManager == null || board == null || sidePanelButton == null || panel == null || closeButton == null)
            {
                Debug.LogError("HowToPlayMenu: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            sidePanelButton.onClick.AddListener(Open);
            if (pauseButton != null)
            {
                pauseButton.onClick.AddListener(Open);
            }
            closeButton.onClick.AddListener(Close);
            themeManager.ThemeChanged += Refresh;

            seen = PlayerPrefs.GetInt(SeenKey, 0) == 1;
            panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (sidePanelButton != null)
            {
                sidePanelButton.onClick.RemoveListener(Open);
            }
            if (pauseButton != null)
            {
                pauseButton.onClick.RemoveListener(Open);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
            if (themeManager != null)
            {
                themeManager.ThemeChanged -= Refresh;
            }
        }

        private void Update()
        {
            if (seen)
            {
                return;
            }
            // Unscaled time, because the pause button is only ever seen while the game is
            // paused, when Time.time stands still. Sin goes -1..1, so (sin + 1) / 2 goes 0..1:
            // the buttons swell from their normal size, never shrink below it.
            float wave = (Mathf.Sin(Time.unscaledTime * pulseRate * 2f * Mathf.PI) + 1f) / 2f;
            SetButtonScale(1f + pulseAmount * wave);
        }

        /// <summary>Shows the side panel's button when true. When false (a round is running) it's hidden and the card closed.</summary>
        public void SetAvailable(bool available)
        {
            if (!enabled)
            {
                return;
            }
            sidePanelButton.gameObject.SetActive(available);
            if (!available)
            {
                Close();
            }
        }

        private void Open()
        {
            panel.SetActive(true);
            Refresh();

            if (!seen)
            {
                seen = true;
                PlayerPrefs.SetInt(SeenKey, 1);
                // Saved now, not when the game quits, so it sticks even if the browser tab is
                // simply closed.
                PlayerPrefs.Save();
                SetButtonScale(1f);
            }
            Select(closeButton);
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            panel.SetActive(false);
            // Give focus back to whichever button is still showing (the pause one, if paused).
            if (sidePanelButton.gameObject.activeInHierarchy)
            {
                Select(sidePanelButton);
            }
            else
            {
                Select(pauseButton != null && pauseButton.gameObject.activeInHierarchy ? pauseButton : null);
            }
        }

        /// <summary>Redraws the pictures in the current style and theme. Does nothing while the card is closed.</summary>
        private void Refresh()
        {
            if (!IsOpen)
            {
                return;
            }
            foreach (HowToPlayIcon icon in icons)
            {
                if (icon != null)
                {
                    icon.Show(themeManager.TokenStyle, themeManager.BoardTheme, board.TokenTypes);
                }
            }
        }

        private void SetButtonScale(float scale)
        {
            // localScale grows the button around its pivot without moving it or changing the
            // size the layout gave it, so the portrait layout and the pulse don't fight.
            var size = new Vector3(scale, scale, 1f);
            sidePanelButton.transform.localScale = size;
            if (pauseButton != null)
            {
                pauseButton.transform.localScale = size;
            }
        }

        /// <summary>
        /// Gives keyboard/gamepad focus to <paramref name="selectable"/> (null = nothing), one
        /// frame from now, so the key press that got us here can't also press it. (The same as
        /// in LeaderboardMenu, which explains it in more detail.)
        /// </summary>
        private void Select(Selectable selectable)
        {
            if (EventSystem.current == null)
            {
                return;
            }
            EventSystem.current.SetSelectedGameObject(null);
            if (selectable != null)
            {
                StartCoroutine(SelectNextFrame(selectable));
            }
        }

        private static IEnumerator SelectNextFrame(Selectable selectable)
        {
            // `yield return null` waits one frame, and still does while paused: it counts
            // frames, not game time.
            yield return null;
            if (selectable != null && selectable.gameObject.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            }
        }
    }
}
