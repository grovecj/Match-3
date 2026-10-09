using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Match3.Online;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// The leaderboards' UI, in two modal windows:
    ///   - the initials prompt, shown at the end of a round whose score makes the local top 10.
    ///     The player types 1-3 letters and presses Submit (or Enter), or Skip (or Esc) to save
    ///     nothing.
    ///   - the Leaderboard panel, opened with the Leaderboard button in the side panel (and
    ///     automatically after submitting). It has two tabs:
    ///       Local  - the top 10 on this computer, with the new entry highlighted.
    ///       Global - the top 10 players everywhere, from the leaderboard server, with the
    ///                player's own line highlighted. A line under the list says how the last
    ///                round's post went, or to sign in.
    ///
    /// The local entries are kept by an <see cref="ILeaderboardStore"/>. This class makes a
    /// <see cref="JsonFileLeaderboardStore"/>, but it only ever uses it through the interface.
    /// The global ones come from an <see cref="OnlineLeaderboardClient"/>, which also posts each
    /// signed-in player's rounds by itself: no initials needed, the server knows who they are.
    ///
    /// Like the Settings menu, the GameManager decides when the leaderboard is allowed (between
    /// rounds only) by calling <see cref="SetAvailable"/>, says when a round starts with
    /// <see cref="RoundStarted"/>, and hands over each finished round's score with
    /// <see cref="OfferEntry"/>.
    /// </summary>
    public class LeaderboardMenu : MonoBehaviour
    {
        /// <summary>PlayerPrefs key for the initials used last time, used to pre-fill the prompt.</summary>
        public const string LastInitialsKey = "Leaderboard.LastInitials";

        /// <summary>
        /// The file name the leaderboard is saved under, in Application.persistentDataPath.
        ///
        /// On Windows the Editor and a built game share the same persistentDataPath, so without
        /// separate names every test round played in the Editor would show up on the leaderboard
        /// of a build run on the same PC. #if UNITY_EDITOR is a compile-time switch: the Editor
        /// compiles the first line, a build compiles the second.
        /// </summary>
#if UNITY_EDITOR
        public const string FileName = "leaderboard-editor.json";
#else
        public const string FileName = "leaderboard.json";
#endif

        [Header("Leaderboard panel")]
        [Tooltip("The Leaderboard button in the side panel that opens the panel.")]
        [SerializeField] private Button openButton;
        [Tooltip("The full-screen modal (dimmed backdrop + window) listing the top 10.")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button closeButton;
        [Tooltip("One row per place, best first. There should be Leaderboard.Capacity of them.")]
        [SerializeField] private LeaderboardRow[] rows;
        [SerializeField] private Button localTabButton;
        [SerializeField] private Button globalTabButton;
        [Tooltip("The line under the list: the global tab's news (\"Sign in to...\", \"Posted!...\").")]
        [SerializeField] private TMP_Text statusText;

        [Header("Global leaderboard")]
        [SerializeField] private OnlineLeaderboardClient online;

        [Header("Initials prompt")]
        [Tooltip("The full-screen modal asking for initials after a top-10 round.")]
        [SerializeField] private GameObject prompt;
        [Tooltip("Shows the score that made the list.")]
        [SerializeField] private TMP_Text promptScoreText;
        [SerializeField] private TMP_InputField initialsField;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button skipButton;

        // The selected tab's button is drawn solid, the other faded.
        private static readonly Color SelectedTabColor = Color.white;
        private static readonly Color OtherTabColor = new Color(1f, 1f, 1f, 0.4f);

        private enum Tab { Local, Global }

        private ILeaderboardStore store;
        private InputAction cancelAction;
        private int pendingScore;
        // The rank (0-based) of the entry submitted since the last round started, or -1.
        private int highlightedRank = -1;
        private Tab tab = Tab.Local;

        public bool IsOpen => panel != null && panel.activeSelf;
        public bool IsPrompting => prompt != null && prompt.activeSelf;

        private void Awake()
        {
            if (openButton == null || panel == null || closeButton == null || rows == null || rows.Length == 0 ||
                localTabButton == null || globalTabButton == null || statusText == null || online == null ||
                prompt == null || promptScoreText == null || initialsField == null ||
                submitButton == null || skipButton == null)
            {
                // Built before the Global tab existed? Run Match3 > Build Leaderboard Menu again.
                Debug.LogError("LeaderboardMenu: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            // Path.Combine joins folder and file name with the right separator for the platform.
            // Until a score is saved, the store shows the default entries (150k down to 50k).
            store = new JsonFileLeaderboardStore(Path.Combine(Application.persistentDataPath, FileName),
                Leaderboard.DefaultEntries());

            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            submitButton.onClick.AddListener(Submit);
            skipButton.onClick.AddListener(Skip);
            localTabButton.onClick.AddListener(ShowLocal);
            globalTabButton.onClick.AddListener(ShowGlobal);
            // Redraw when news arrives from the server (see AccountMenu for how events work).
            online.Changed += OnOnlineChanged;

            // Input validation for the initials field:
            //  - characterLimit stops typing after 3 characters.
            //  - onValidateInput is called for every character typed (or pasted) before it's
            //    added. It returns the character to add, or '\0' to drop it. Ours uppercases
            //    letters and drops everything else, so the field can only ever hold A-Z.
            //  - onValueChanged fires after every change; we use it to grey out Submit while
            //    the field is empty.
            //  - onSubmit fires when Enter is pressed in the field.
            initialsField.characterLimit = Leaderboard.MaxInitials;
            initialsField.onValidateInput = ValidateInitial;
            initialsField.onValueChanged.AddListener(OnInitialsChanged);
            initialsField.onSubmit.AddListener(OnInitialsSubmitted);

            cancelAction = InputSystem.actions.FindAction("UI/Cancel", throwIfNotFound: true);

            panel.SetActive(false);
            prompt.SetActive(false);
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
            if (submitButton != null)
            {
                submitButton.onClick.RemoveListener(Submit);
            }
            if (skipButton != null)
            {
                skipButton.onClick.RemoveListener(Skip);
            }
            if (localTabButton != null)
            {
                localTabButton.onClick.RemoveListener(ShowLocal);
            }
            if (globalTabButton != null)
            {
                globalTabButton.onClick.RemoveListener(ShowGlobal);
            }
            if (online != null)
            {
                online.Changed -= OnOnlineChanged;
            }
            if (initialsField != null)
            {
                initialsField.onValueChanged.RemoveListener(OnInitialsChanged);
                initialsField.onSubmit.RemoveListener(OnInitialsSubmitted);
            }
        }

        private void Update()
        {
            if (!cancelAction.WasPressedThisFrame())
            {
                return;
            }
            // Esc backs out of whichever window is showing.
            if (IsPrompting)
            {
                Skip();
            }
            else if (IsOpen)
            {
                Close();
            }
        }

        /// <summary>
        /// Shows the Leaderboard button when true. When false (a round is running) the button is
        /// hidden, both windows are closed, and the "just added" highlight is cleared.
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
                ClosePrompt();
                Close();
                highlightedRank = -1;
            }
        }

        /// <summary>
        /// Called when a round starts, so a signed-in player's round can be registered with the
        /// global leaderboard.
        /// </summary>
        public void RoundStarted()
        {
            if (enabled)
            {
                online.RoundStarted();
            }
        }

        /// <summary>
        /// Called with the final score when a round ends. A signed-in player's score is posted to
        /// the global leaderboard in the background. If it also makes the local top 10, asks for
        /// initials; otherwise the normal round-end screen is all that shows.
        /// </summary>
        public void OfferEntry(int score)
        {
            if (!enabled)
            {
                return;
            }
            online.RoundEnded(score);
            if (!Leaderboard.Qualifies(store.Load(), score))
            {
                return;
            }

            pendingScore = score;
            promptScoreText.text = score.ToString("N0", CultureInfo.InvariantCulture);

            // Pre-fill with the initials used last time. Setting .text directly skips
            // onValidateInput, so clean it up ourselves in case the saved value is odd.
            initialsField.text = Leaderboard.CleanInitials(PlayerPrefs.GetString(LastInitialsKey, ""));
            UpdateSubmitButton();

            prompt.SetActive(true);
            // Selecting an input field also activates it (shows the caret and selects the text),
            // so the player can type straight away; typing replaces the pre-filled initials.
            Select(initialsField);
        }

        // Matches TMP_InputField.OnValidateInput: the text so far, where the new character is
        // going, and the character itself.
        private static char ValidateInitial(string text, int charIndex, char addedChar)
        {
            return Leaderboard.ValidateInitial(addedChar);
        }

        private void OnInitialsChanged(string text)
        {
            UpdateSubmitButton();
        }

        private void UpdateSubmitButton()
        {
            // interactable = false greys the button out and ignores clicks.
            submitButton.interactable = Leaderboard.CleanInitials(initialsField.text).Length > 0;
        }

        private void OnInitialsSubmitted(string text)
        {
            Submit();
        }

        /// <summary>Saves the entry and shows the leaderboard with it highlighted.</summary>
        private void Submit()
        {
            if (!IsPrompting)
            {
                return;
            }

            string initials = Leaderboard.CleanInitials(initialsField.text);
            if (initials.Length == 0)
            {
                // Enter on an empty field: nothing to save. Enter also ends editing, so put the
                // caret back in the field.
                initialsField.ActivateInputField();
                return;
            }

            PlayerPrefs.SetString(LastInitialsKey, initials);
            PlayerPrefs.Save();

            highlightedRank = store.Submit(new LeaderboardEntry(initials, pendingScore, DateTime.UtcNow));
            ClosePrompt();
            // Show the local list, where the new entry is.
            tab = Tab.Local;
            Open();
        }

        /// <summary>Closes the prompt without saving anything.</summary>
        private void Skip()
        {
            if (!IsPrompting)
            {
                return;
            }
            ClosePrompt();
            Select(openButton);
        }

        private void ClosePrompt()
        {
            if (!IsPrompting)
            {
                return;
            }
            initialsField.DeactivateInputField();
            prompt.SetActive(false);
        }

        private void Open()
        {
            if (tab == Tab.Global)
            {
                online.RefreshBoard();
            }
            Refresh();
            // The panel's backdrop swallows clicks to everything behind it, as in the Settings
            // menu, which is what makes it modal.
            panel.SetActive(true);
            Select(closeButton);
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            panel.SetActive(false);
            // Hand focus back to the Leaderboard button, unless it's hidden because a round is
            // starting.
            Select(openButton.gameObject.activeInHierarchy ? openButton : null);
        }

        private void ShowLocal()
        {
            tab = Tab.Local;
            Refresh();
        }

        private void ShowGlobal()
        {
            tab = Tab.Global;
            // Load it fresh each time the tab is picked; until it arrives, the rows show what
            // was loaded last (or "---") and the status line says "Loading...".
            online.RefreshBoard();
            Refresh();
        }

        private void OnOnlineChanged()
        {
            if (IsOpen && tab == Tab.Global)
            {
                Refresh();
            }
        }

        /// <summary>Draws the selected tab: its button, its rows and the status line.</summary>
        private void Refresh()
        {
            // targetGraphic is the button's background Image; Button tints it on hover/press,
            // multiplying this base color.
            localTabButton.targetGraphic.color = tab == Tab.Local ? SelectedTabColor : OtherTabColor;
            globalTabButton.targetGraphic.color = tab == Tab.Global ? SelectedTabColor : OtherTabColor;

            if (tab == Tab.Local)
            {
                RefreshLocal();
            }
            else
            {
                RefreshGlobal();
            }
        }

        /// <summary>Fills the rows from the store: entries first, then empty "---" slots.</summary>
        private void RefreshLocal()
        {
            List<LeaderboardEntry> entries = store.Load();
            for (int i = 0; i < rows.Length; i++)
            {
                LeaderboardEntry entry = i < entries.Count ? entries[i] : null;
                rows[i].Show(i + 1, entry, i == highlightedRank);
            }
            statusText.text = "Scores on this computer";
        }

        /// <summary>Fills the rows from the last global load, and says where the player stands.</summary>
        private void RefreshGlobal()
        {
            GlobalEntry[] entries = online.Top;
            PlayerProfile me = online.Me;
            for (int i = 0; i < rows.Length; i++)
            {
                GlobalEntry entry = entries != null && i < entries.Length ? entries[i] : null;
                if (entry == null)
                {
                    rows[i].ShowEmpty(i + 1);
                    continue;
                }
                // The player's own line. Tags aren't unique, so match the rank and score too.
                bool mine = me != null && me.HasScore &&
                    entry.rank == me.rank && entry.score == me.best && entry.tag == me.tag;
                rows[i].Show(entry.rank, entry.tag, entry.score, mine);
            }
            statusText.text = GlobalStatus(me);
        }

        private string GlobalStatus(PlayerProfile me)
        {
            if (online.BoardMessage != null)
            {
                return online.BoardMessage;
            }
            if (online.Loading && online.Top == null)
            {
                return "Loading...";
            }
            if (!online.IsSignedIn)
            {
                return "Sign in to post to the global leaderboard.";
            }
            // News about the round that just ended comes first.
            if (online.PostMessage != null)
            {
                return online.PostMessage;
            }
            if (me != null && me.HasScore)
            {
                string best = me.best.ToString("N0", CultureInfo.InvariantCulture);
                return $"You ({me.tag}): best {best}, #{me.rank}";
            }
            return "Your rounds are posted automatically while you're signed in.";
        }

        /// <summary>
        /// Gives keyboard/gamepad focus to <paramref name="selectable"/> (null = nothing), one
        /// frame from now.
        ///
        /// Why wait a frame? Pressing Enter in the initials field submits the entry and opens
        /// the panel in the middle of the EventSystem's update. If the panel's Close button were
        /// selected right away, that same Enter press could reach it too and close the panel
        /// the moment it opened. Waiting until the next frame lets that press finish first.
        /// </summary>
        private void Select(Selectable selectable)
        {
            if (EventSystem.current == null)
            {
                return;
            }
            // Deselect now, so nothing (like the hidden Leaderboard button) keeps focus meanwhile.
            EventSystem.current.SetSelectedGameObject(null);
            if (selectable != null)
            {
                StartCoroutine(SelectNextFrame(selectable));
            }
        }

        // A coroutine is a method Unity runs a bit at a time: "yield return null" pauses it
        // until the next frame (even while Time.timeScale is 0), then it carries on from there.
        private static IEnumerator SelectNextFrame(Selectable selectable)
        {
            yield return null;
            if (selectable != null && selectable.gameObject.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            }
        }
    }
}
