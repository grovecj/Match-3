using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// The Settings menu: a gear button that opens a modal panel where the player picks a token
    /// style and a board theme. Choices apply immediately (the board behind the panel updates
    /// as you click) and the ThemeManager saves them, so there's no Apply/Cancel.
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

        private readonly List<TokenStyleOption> tokenStyleOptions = new List<TokenStyleOption>();
        private readonly List<BoardThemeOption> boardThemeOptions = new List<BoardThemeOption>();
        private InputAction cancelAction;

        public bool IsOpen => modal != null && modal.activeSelf;

        private void Awake()
        {
            if (themeManager == null || board == null || openButton == null || modal == null ||
                closeButton == null || tokenStyleList == null || tokenStyleOptionPrefab == null ||
                boardThemeList == null || boardThemeOptionPrefab == null)
            {
                Debug.LogError("SettingsMenu: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);

            // "UI/Cancel" is the Esc (and gamepad B/East) action from the project-wide input
            // actions: the standard "back out of this menu" input.
            cancelAction = InputSystem.actions.FindAction("UI/Cancel", throwIfNotFound: true);

            modal.SetActive(false);
        }

        private void Start()
        {
            BuildOptions();
            RefreshSelection();
        }

        private void OnEnable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged += RefreshSelection;
            }
            // Catch up on anything that changed while we weren't listening (a no-op before Start
            // has built the options).
            RefreshSelection();
        }

        private void OnDisable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged -= RefreshSelection;
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

            // Hand focus back to the gear, unless it's hidden because a round is starting.
            Select(openButton.gameObject.activeInHierarchy ? openButton : null);
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
