using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// The Settings menu: a gear button that opens a modal panel where the player picks a token
    /// style and a board theme and sets the sound volume. Choices apply immediately (the board
    /// behind the panel updates as you click) and are saved, so there's no Apply/Cancel.
    ///
    /// The options aren't laid out by hand. At startup this reads the ThemeManager's lists and
    /// instantiates one option prefab per style/theme into a layout group, so adding a new
    /// TokenStyle or BoardTheme asset to the ThemeManager makes it show up here automatically.
    ///
    /// The GameManager decides when the menu is allowed (between rounds only) by calling
    /// <see cref="SetAvailable"/>.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        [SerializeField] private ThemeManager themeManager;
        [Tooltip("Supplies the token types shown in each style's preview row.")]
        [SerializeField] private Board board;

        [Header("Panel")]
        [Tooltip("The gear button in the side panel that opens the menu.")]
        [SerializeField] private Button openButton;
        [Tooltip("The full-screen modal (dimmed backdrop + window). Hidden while the menu is closed.")]
        [SerializeField] private GameObject modal;
        [SerializeField] private Button closeButton;

        [Header("Token Style section")]
        [Tooltip("Layout group the token style options are created in.")]
        [SerializeField] private RectTransform tokenStyleList;
        [SerializeField] private TokenStyleOption tokenStyleOptionPrefab;

        [Header("Board Colors section")]
        [Tooltip("Layout group the board theme options are created in.")]
        [SerializeField] private RectTransform boardThemeList;
        [SerializeField] private BoardThemeOption boardThemeOptionPrefab;

        [Header("Sound section")]
        [SerializeField] private AudioManager audioManager;
        [Tooltip("Goes from 0 (muted) to 100 (full volume), in whole percent.")]
        [SerializeField] private Slider volumeSlider;
        [Tooltip("Shows the slider's value, e.g. \"80%\".")]
        [SerializeField] private TMP_Text volumeValueText;
        [Tooltip("Plays a sample sound at the current volume.")]
        [SerializeField] private Button testSoundButton;

        private readonly List<TokenStyleOption> tokenStyleOptions = new List<TokenStyleOption>();
        private readonly List<BoardThemeOption> boardThemeOptions = new List<BoardThemeOption>();
        private InputAction cancelAction;

        /// <summary>
        /// Fired when the menu closes, after the choices are saved. PreferencesSync listens, to
        /// send them to the player's account.
        /// </summary>
        public event System.Action Closed;

        public bool IsOpen => modal != null && modal.activeSelf;

        private void Awake()
        {
            if (themeManager == null || board == null || openButton == null || modal == null ||
                closeButton == null || tokenStyleList == null || tokenStyleOptionPrefab == null ||
                boardThemeList == null || boardThemeOptionPrefab == null || audioManager == null ||
                volumeSlider == null || volumeValueText == null || testSoundButton == null)
            {
                Debug.LogError("SettingsMenu: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            // A Slider's onValueChanged passes the new value along, so it can call a method that
            // takes a float directly.
            volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
            testSoundButton.onClick.AddListener(PlayTestSound);

            // "UI/Cancel" is the Esc (and gamepad B/East) action from the project-wide input
            // actions: the standard "back out of this menu" input.
            cancelAction = InputSystem.actions.FindAction("UI/Cancel", throwIfNotFound: true);

            modal.SetActive(false);
        }

        private void Start()
        {
            BuildOptions();
            RefreshSelection();

            // Start (not Awake) so the AudioManager's Awake has already loaded the saved volume.
            RefreshVolume();
        }

        private void OnEnable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged += RefreshSelection;
            }
            if (audioManager != null)
            {
                audioManager.VolumeChanged += RefreshVolume;
            }
            // Catch up on anything that changed while we weren't listening (a no-op before Start
            // has built the options).
            RefreshSelection();
            RefreshVolume();
        }

        private void OnDisable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged -= RefreshSelection;
            }
            if (audioManager != null)
            {
                audioManager.VolumeChanged -= RefreshVolume;
            }
        }

        private void OnDestroy()
        {
            if (openButton != null)
            {
                openButton.onClick.RemoveListener(Open);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
            if (volumeSlider != null)
            {
                volumeSlider.onValueChanged.RemoveListener(OnVolumeSliderChanged);
            }
            if (testSoundButton != null)
            {
                testSoundButton.onClick.RemoveListener(PlayTestSound);
            }
        }

        private void Update()
        {
            if (IsOpen && cancelAction.WasPressedThisFrame())
            {
                Close();
            }
        }

        /// <summary>
        /// Shows the gear button when true. When false (a round is running or paused) the gear
        /// is hidden and the menu is closed if it happened to be open.
        /// </summary>
        public void SetAvailable(bool available)
        {
            if (!enabled)
            {
                return;
            }
            openButton.gameObject.SetActive(available);
            if (!available)
            {
                Close();
            }
        }

        private void Open()
        {
            // The modal's backdrop is a full-screen Image with "Raycast Target" on, drawn on top
            // of everything else. Clicks land on it instead of on the buttons (or anything else)
            // behind it: that's what makes the panel modal. The board itself isn't clickable
            // right now anyway, because the GameManager only turns board input on mid-round.
            modal.SetActive(true);

            // Move keyboard/gamepad focus into the panel. Otherwise it stays on the gear, now
            // hidden behind the backdrop, and Enter/Space or the arrow keys would act on
            // controls the player can't see.
            Select(closeButton);
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            modal.SetActive(false);

            // The volume can change many times while dragging, so it's written to disk once
            // here rather than on every step.
            audioManager.SaveVolume();

            // Hand focus back to the gear, unless it's hidden because a round is starting.
            Select(openButton.gameObject.activeInHierarchy ? openButton : null);

            Closed?.Invoke();
        }

        /// <summary>
        /// Gives keyboard/gamepad focus to <paramref name="button"/> (null = nothing). The
        /// EventSystem tracks one "selected" UI object: arrow keys move from it, Enter clicks it.
        /// </summary>
        private static void Select(Button button)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(button != null ? button.gameObject : null);
            }
        }

        private void OnVolumeSliderChanged(float percent)
        {
            audioManager.SetVolume(percent / 100f);
            ShowVolume();
        }

        /// <summary>Moves the slider to the AudioManager's volume.</summary>
        private void RefreshVolume()
        {
            if (volumeSlider == null || volumeValueText == null || audioManager == null)
            {
                return;
            }
            // SetValueWithoutNotify moves the handle without firing onValueChanged: we're showing
            // the current volume, not changing it.
            volumeSlider.SetValueWithoutNotify(Mathf.Round(audioManager.Volume * 100f));
            ShowVolume();
        }

        private void ShowVolume()
        {
            volumeValueText.text = $"{Mathf.RoundToInt(volumeSlider.value)}%";
        }

        private void PlayTestSound()
        {
            audioManager.Play(SoundId.Match3);
        }

        /// <summary>Creates one option per available style and theme.</summary>
        private void BuildOptions()
        {
            foreach (TokenStyle style in themeManager.TokenStyles)
            {
                if (style == null)
                {
                    continue;
                }
                // Instantiate(prefab, parent) copies the prefab and makes it a child of parent.
                // The list's layout group then positions it: we never set positions ourselves.
                TokenStyleOption option = Instantiate(tokenStyleOptionPrefab, tokenStyleList);
                option.Setup(style, board.TokenTypes);
                // The lambda "captures" this loop's style, so each option sets its own style.
                option.Clicked += () => themeManager.SetTokenStyle(style);
                tokenStyleOptions.Add(option);
            }

            foreach (BoardTheme theme in themeManager.BoardThemes)
            {
                if (theme == null)
                {
                    continue;
                }
                BoardThemeOption option = Instantiate(boardThemeOptionPrefab, boardThemeList);
                option.Setup(theme);
                option.Clicked += () => themeManager.SetBoardTheme(theme);
                boardThemeOptions.Add(option);
            }
        }

        /// <summary>Highlights the options that match what the ThemeManager is using.</summary>
        private void RefreshSelection()
        {
            foreach (TokenStyleOption option in tokenStyleOptions)
            {
                option.SetSelected(option.TokenStyle == themeManager.TokenStyle);
            }
            foreach (BoardThemeOption option in boardThemeOptions)
            {
                option.SetSelected(option.BoardTheme == themeManager.BoardTheme);
            }
        }
    }
}
