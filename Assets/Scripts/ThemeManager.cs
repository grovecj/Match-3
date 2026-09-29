using System.Collections.Generic;
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
    ///
    /// The player's choice is saved with PlayerPrefs (by each asset's <c>Id</c>) and restored
    /// the next time the game starts.
    /// </summary>
    public class ThemeManager : MonoBehaviour
    {
        // PlayerPrefs is a tiny key/value store Unity keeps for you between runs (on Windows
        // it lives in the registry). These are the keys we save the chosen ids under.
        private const string TokenStyleKey = "Settings.TokenStyle";
        private const string BoardThemeKey = "Settings.BoardTheme";

        [Header("Available")]
        [Tooltip("Token styles the player can choose from. The first is used if none is selected.")]
        [SerializeField] private TokenStyle[] tokenStyles;
        [Tooltip("Board themes the player can choose from. The first is used if none is selected.")]
        [SerializeField] private BoardTheme[] boardThemes;

        [Header("Current")]
        [Tooltip("Style in use (and the default when nothing is saved). Change it during Play mode to re-skin the tokens instantly.")]
        [SerializeField] private TokenStyle tokenStyle;
        [Tooltip("Theme in use (and the default when nothing is saved). Change it during Play mode to recolor the board instantly.")]
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
            LoadSavedSelection();
            SelectDefaultsIfEmpty();
        }

        /// <summary>
        /// Replaces the Inspector's selection with the one saved last time, if there is one.
        /// A saved id that no longer matches any asset (say, the asset was deleted) is ignored,
        /// which leaves the Inspector's selection in place as the default.
        /// </summary>
        private void LoadSavedSelection()
        {
            // GetString returns the second argument when the key has never been saved.
            string savedStyleId = PlayerPrefs.GetString(TokenStyleKey, "");
            string savedThemeId = PlayerPrefs.GetString(BoardThemeKey, "");

            TokenStyle savedStyle = FindById(tokenStyles, savedStyleId, style => style.Id);
            if (savedStyle != null)
            {
                tokenStyle = savedStyle;
            }

            BoardTheme savedTheme = FindById(boardThemes, savedThemeId, theme => theme.Id);
            if (savedTheme != null)
            {
                boardTheme = savedTheme;
            }
        }

        /// <summary>
        /// Returns the first item in <paramref name="items"/> whose id (read with
        /// <paramref name="getId"/>) equals <paramref name="id"/>, or null if none does.
        /// Null items, and an empty or null <paramref name="id"/>, never match.
        /// </summary>
        // Generic (<T>) so the same lookup works for both TokenStyle and BoardTheme. The caller
        // passes a small function (a lambda, like `style => style.Id`) that says where the id is.
        public static T FindById<T>(IEnumerable<T> items, string id, System.Func<T, string> getId)
            where T : class
        {
            if (items == null || string.IsNullOrEmpty(id))
            {
                return null;
            }
            foreach (T item in items)
            {
                if (item != null && getId(item) == id)
                {
                    return item;
                }
            }
            return null;
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

        /// <summary>Switches every token to <paramref name="style"/> and remembers the choice.</summary>
        public void SetTokenStyle(TokenStyle style)
        {
            if (style == null || style == tokenStyle)
            {
                return;
            }
            tokenStyle = style;
            PlayerPrefs.SetString(TokenStyleKey, style.Id);
            SaveAndNotify();
        }

        /// <summary>Switches the board and background colors to <paramref name="theme"/> and remembers the choice.</summary>
        public void SetBoardTheme(BoardTheme theme)
        {
            if (theme == null || theme == boardTheme)
            {
                return;
            }
            boardTheme = theme;
            PlayerPrefs.SetString(BoardThemeKey, theme.Id);
            SaveAndNotify();
        }

        private void SaveAndNotify()
        {
            // Unity writes PlayerPrefs to disk when the game quits normally. Saving now as well
            // means a crash (or stopping Play mode at a bad moment) can't lose the choice.
            PlayerPrefs.Save();
            ThemeChanged?.Invoke();
        }
    }
}
