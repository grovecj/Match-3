using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Holds the available token styles and board themes, and which of each is in use.
    /// Anything that draws themed visuals (the Board, the camera background) reads the current
    /// selection from here and listens to <see cref="ThemeChanged"/> to redraw when it changes.
    ///
    /// This is the observer pattern: the ThemeManager doesn't know who is displaying the theme,
    /// it just announces changes, so new themed things can be added without editing this class.
    /// </summary>
    public class ThemeManager : MonoBehaviour
    {
        [Header("Available")]
        [Tooltip("Token styles the player can choose from. The first is used if none is selected.")]
        [SerializeField] private TokenStyle[] tokenStyles;
        [Tooltip("Board themes the player can choose from. The first is used if none is selected.")]
        [SerializeField] private BoardTheme[] boardThemes;

        [Header("Current")]
        [Tooltip("Style in use. Change it during Play mode to re-skin the tokens instantly.")]
        [SerializeField] private TokenStyle tokenStyle;
        [Tooltip("Theme in use. Change it during Play mode to recolor the board instantly.")]
        [SerializeField] private BoardTheme boardTheme;

        /// <summary>Fired whenever the token style or board theme changes.</summary>
        public event System.Action ThemeChanged;

        private bool changedInInspector;

        public TokenStyle[] TokenStyles => tokenStyles;
        public BoardTheme[] BoardThemes => boardThemes;
        public TokenStyle TokenStyle => tokenStyle;
        public BoardTheme BoardTheme => boardTheme;

        // Awake runs before any Start, so by the time the Board builds itself in Start there's
        // always a selection to read.
        private void Awake()
        {
            SelectDefaultsIfEmpty();
        }

        /// <summary>Falls back to the first available style/theme for any empty selection.</summary>
        private void SelectDefaultsIfEmpty()
        {
            if (tokenStyle == null && tokenStyles != null && tokenStyles.Length > 0)
            {
                tokenStyle = tokenStyles[0];
            }
            if (boardTheme == null && boardThemes != null && boardThemes.Length > 0)
            {
                boardTheme = boardThemes[0];
            }
        }

        // Editing a field in the Inspector bypasses SetTokenStyle/SetBoardTheme, so notice it
        // here instead. Like Board, we only set a flag and react in Update: OnValidate can run
        // at awkward moments (e.g. while the scene is still loading).
        private void OnValidate()
        {
            changedInInspector = true;
        }

        private void Update()
        {
            if (changedInInspector)
            {
                changedInInspector = false;
                // Clearing a field in the Inspector would otherwise leave nothing selected and
                // blank out the board, so put a default back first.
                SelectDefaultsIfEmpty();
                ThemeChanged?.Invoke();
            }
        }

        /// <summary>Switches every token to <paramref name="style"/>.</summary>
        public void SetTokenStyle(TokenStyle style)
        {
            if (style == null || style == tokenStyle)
            {
                return;
            }
            tokenStyle = style;
            ThemeChanged?.Invoke();
        }

        /// <summary>Switches the board and background colors to <paramref name="theme"/>.</summary>
        public void SetBoardTheme(BoardTheme theme)
        {
            if (theme == null || theme == boardTheme)
            {
                return;
            }
            boardTheme = theme;
            ThemeChanged?.Invoke();
        }
    }
}
