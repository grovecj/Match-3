using Match3.Accounts;
using Match3.Achievements;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>What the end-of-round countdown should sound like on a given frame.</summary>
    public enum CountdownCue
    {
        None,
        Tick,
        Horn,
    }

    /// <summary>
    /// Runs the round: a 60-second countdown that turns the board into a game. It decides when
    /// the player may swap, resets the board and score between rounds, and drives the timer,
    /// message, and Start/Play Again button in the side panel.
    ///
    /// This is a state machine with five states:
    ///   Ready      - the game just launched. The board is shown, input is off, and the
    ///                "Start Round" button waits for a click.
    ///   Playing    - the timer is counting down and the player can swap.
    ///   LastHurrah - time ran out with power gems still on the board. A "Last Hurrah!" banner
    ///                shows while the board sets them all off (and scores them); input stays
    ///                off. Skipped when there are no power gems left.
    ///   Paused     - Esc or P was pressed mid-round, or the game lost focus. Time is frozen, the
    ///                board is blurred, and a "Paused" overlay is shown. Esc, P, or the Resume
    ///                button goes back to where it was: Playing or LastHurrah.
    ///   RoundOver  - time ran out and the board has settled. "Time's up!", the round summary
    ///                (the score against the personal best, and a few stats) and a "Play
    ///                Again" button are shown.
    /// Clicking the button in Ready or RoundOver starts a new round (-> Playing).
    /// The Settings menu (gear button), the Leaderboard and the Account menu are only available
    /// in Ready and RoundOver. A RoundOver score that makes the top 10 is offered to the leaderboard, which
    /// asks for the player's initials. A signed-in player's rounds are also posted to the global
    /// leaderboard.
    ///
    /// It also plays the end-of-round countdown: a tick for each of the last few seconds and a
    /// horn when the clock hits 0:00 (so the horn comes before the Last Hurrah, not after).
    ///
    /// The clock (a <see cref="RoundClock"/>) counts game time, not wall-clock time: it is held
    /// while the board reshuffles and while power gems go off, so a round can take longer than
    /// a minute to play. The ticks and horn follow the game clock, so they wait out a hold too.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        /// <summary>A tick plays as the timer changes to each second from this one down to 1.</summary>
        public const int CountdownFrom = 5;

        // The last tick (0:01) plays a little higher, as a final "get ready".
        private const float FinalTickPitch = 1.1f;
        // Ticks play below full volume so the horn, at full volume, is clearly the loudest.
        private const float TickVolume = 0.6f;

        [Header("Gameplay")]
        [SerializeField] private Board board;
        [SerializeField] private BoardInput boardInput;
        [SerializeField] private ScoreKeeper scoreKeeper;
        [SerializeField] private AudioManager audioManager;
        [SerializeField, Min(1f)] private float roundDuration = 60f;
        [Tooltip("When this many seconds (or fewer) are left, the timer turns red.")]
        [SerializeField, Min(0f)] private float warningTime = 10f;

        [Header("UI")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private Color timerColor = Color.white;
        [SerializeField] private Color timerWarningColor = new Color(1f, 0.3f, 0.3f);
        [Tooltip("The timer's color while the clock is held (power gems going off, or the board reshuffling), so the player sees that it stopped.")]
        [SerializeField] private Color timerHeldColor = new Color(0.45f, 0.85f, 1f);
        [Tooltip("Shows \"Time's up!\" when a round ends (with the whole round summary if there's no Round Summary View), \"Last Hurrah!\" while leftover power gems go off, and \"No more moves!\" while the board reshuffles. Hidden otherwise.")]
        [SerializeField] private TMP_Text messageText;
        [Tooltip("How long, in seconds, \"No more moves!\" stays up when the board reshuffles.")]
        [SerializeField, Min(0f)] private float noMovesMessageDuration = 1.5f;
        [Tooltip("The one button used both to start the first round and to play again.")]
        [SerializeField] private Button roundButton;
        [SerializeField] private TMP_Text roundButtonLabel;

        [Header("Round summary")]
        [Tooltip("Shows the \"Best\" line under the score. Optional, like the two below; all three are wired by Match3 > Build Round Summary.")]
        [SerializeField] private ScoreView scoreView;
        [Tooltip("The card over the board with the round's summary. Without it, the summary goes in the message instead.")]
        [SerializeField] private RoundSummaryView roundSummaryView;
        [Tooltip("The round's stats, for the summary's stat lines. The same tracker the server's stats come from.")]
        [SerializeField] private RoundStatsTracker roundStats;

        [Header("Pause")]
        [Tooltip("The gray \"Paused\" panel over the board. Hidden unless paused.")]
        [SerializeField] private GameObject pauseOverlay;
        [SerializeField] private Button resumeButton;
        [SerializeField] private BoardBlur boardBlur;

        [Header("Settings")]
        [Tooltip("Shown between rounds only, so the look of the board can't change mid-round.")]
        [SerializeField] private SettingsMenu settingsMenu;

        [Header("Leaderboard")]
        [Tooltip("Shown between rounds only. Gets each finished round's score.")]
        [SerializeField] private LeaderboardMenu leaderboardMenu;

        [Header("Account")]
        [Tooltip("Shown between rounds only, like Settings.")]
        [SerializeField] private AccountMenu accountMenu;

        [Header("Achievements")]
        [Tooltip("Shown between rounds only, like Settings. Optional: leave empty if the scene has no Achievements menu.")]
        [SerializeField] private AchievementsMenu achievementsMenu;

        private enum GameState { Ready, Playing, LastHurrah, Paused, RoundOver }

        private GameState state;
        // The state Pause() interrupted, so Resume() can go back to it. Before the Last Hurrah
        // there was only one state to pause from, and Resume could just say "Playing"; with two,
        // the state machine has to remember which.
        private GameState pausedFrom;
        // The round's game time. `readonly` means this field always holds the same RoundClock;
        // the clock itself still changes (Reset, Advance).
        private readonly RoundClock clock = new RoundClock(CountdownFrom);
        // Seconds until the "No more moves!" message is hidden again. 0 when it isn't showing.
        private float noMovesMessageTimeLeft;
        private InputAction pauseAction;
        // This device's best score. Made in Start, since PlayerPrefs can't be read before then
        // (Unity doesn't allow it in a field initializer or constructor).
        private PersonalBest personalBest;

        private void Start()
        {
            if (board == null || boardInput == null || scoreKeeper == null || audioManager == null ||
                timerText == null || messageText == null || roundButton == null || roundButtonLabel == null ||
                pauseOverlay == null || resumeButton == null || boardBlur == null || settingsMenu == null ||
                leaderboardMenu == null || accountMenu == null)
            {
                Debug.LogError("GameManager: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            // Button.onClick is a UnityEvent: a list of methods to call when the button is
            // clicked. Listeners can be added in the Inspector (the "On Click ()" list) or, as
            // here, from code, which keeps the wiring visible next to the method it calls.
            roundButton.onClick.AddListener(StartRound);
            resumeButton.onClick.AddListener(Resume);
            board.NoMovesLeft += ShowNoMovesMessage;

            // InputSystem.actions is the project-wide actions asset (Assets/Settings/
            // InputSystem_Actions, set under Project Settings > Input System Package). Unity
            // enables it automatically, so we only need to find our action by "Map/Action" name.
            pauseAction = InputSystem.actions.FindAction("Player/Pause", throwIfNotFound: true);

            personalBest = new PersonalBest(new PlayerPrefsPersonalBestStore());

            pauseOverlay.SetActive(false);
            EnterReady();
        }

        private void OnDestroy()
        {
            if (board != null)
            {
                board.NoMovesLeft -= ShowNoMovesMessage;
            }
            if (roundButton != null)
            {
                roundButton.onClick.RemoveListener(StartRound);
            }
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(Resume);
            }

            // Time.timeScale is global and outlives this scene, so never leave the game frozen.
            // The same goes for AudioListener.pause.
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        // Unity calls this when the game window gains or loses focus. Pausing when the player
        // clicks away means the clock doesn't run down while they're in another window. In a
        // browser (Web build) it fires when the page loses focus: clicking the address bar,
        // switching tabs, or switching to another app.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && CanPause)
            {
                Pause();
            }
        }

        // Unity calls this with true when the app is suspended: minimized on some platforms,
        // sent to the background on mobile, or (in a browser) the tab being hidden. Focus loss
        // usually comes first, but not on every platform, so we handle both. Pause() is only
        // called from a state that can pause, so getting both events can't pause twice.
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && CanPause)
            {
                Pause();
            }
        }

        // The two states in which a round is in progress. `=>` makes this a read-only property
        // worked out each time it's asked for, so it can never be out of date.
        private bool CanPause => state == GameState.Playing || state == GameState.LastHurrah;

        private void Update()
        {
            // Update keeps running while paused: Time.timeScale = 0 only makes Time.deltaTime
            // 0, it doesn't stop Update. That's what lets us notice Esc/P again to resume.
            // (The Pause action is bound to both keys in the actions asset. P exists because in
            // a browser, Esc is taken: it exits fullscreen before the game ever sees it.)
            bool pausePressed = pauseAction.WasPressedThisFrame();

            // Each state gets its own case, so it's easy to see what happens every frame
            // in each one.
            switch (state)
            {
                case GameState.Ready:
                case GameState.RoundOver:
                    // Nothing to do; we're waiting for a button click. Esc/P is ignored.
                    break;

                case GameState.Playing:
                    if (pausePressed)
                    {
                        Pause();
                    }
                    else
                    {
                        UpdatePlaying();
                    }
                    break;

                case GameState.LastHurrah:
                    if (pausePressed)
                    {
                        Pause();
                    }
                    // The Board does all the work (and pauses with everything else, since its
                    // coroutines run on scaled time). All there is to do here is notice when
                    // it has finished: it only goes idle once no power gem is left.
                    else if (!board.IsResolving)
                    {
                        EnterRoundOver();
                    }
                    break;

                case GameState.Paused:
                    if (pausePressed)
                    {
                        Resume();
                    }
                    break;
            }
        }

        private void UpdatePlaying()
        {
            // Counted down here (not in a coroutine) so that, like the round timer, it simply
            // stops while paused: UpdatePlaying isn't called then.
            if (noMovesMessageTimeLeft > 0f)
            {
                noMovesMessageTimeLeft -= Time.deltaTime;
                if (noMovesMessageTimeLeft <= 0f)
                {
                    messageText.gameObject.SetActive(false);
                }
            }

            if (!clock.HasRunOut)
            {
                // The clock is held while the board reshuffles (running out of moves is bad luck,
                // and the player can't do anything until the tokens have settled again) and
                // while power gems go off (a long chain should reward the player, not eat their
                // time). That's separate from the Paused state: input is already locked while
                // the board resolves, and Esc still pauses the game on top of a hold.
                bool held = board.IsReshuffling || board.IsDetonating;

                // Time.deltaTime is the number of seconds since the last frame. Counting it off
                // every frame makes the timer count real seconds whatever the frame rate is.
                PlayCountdownCue(clock.Advance(Time.deltaTime, held));
                ShowTime();

                if (clock.HasRunOut)
                {
                    // Time's up: no new swaps from here on.
                    SetInputEnabled(false);
                }
            }

            // Once the clock hits 0, a cascade that's still playing out is allowed to finish
            // (and score) before the round really ends, so we wait for the board to settle.
            if (clock.HasRunOut && !board.IsResolving)
            {
                // The Last Hurrah extends that rule: any power gems still on the board go off
                // too. TryStartLastHurrah says no when there are none, and then the round ends
                // exactly as it always did.
                if (board.TryStartLastHurrah())
                {
                    EnterLastHurrah();
                }
                else
                {
                    EnterRoundOver();
                }
            }
        }

        /// <summary>Starts a new round. Called by the Start Round / Play Again button.</summary>
        private void StartRound()
        {
            board.Regenerate();
            // Before the score resets, so the reset shows the line against this round's best.
            ShowBestUnderScore();
            scoreKeeper.ResetScore();
            if (roundStats != null)
            {
                roundStats.ResetStats();
            }
            if (roundSummaryView != null)
            {
                roundSummaryView.Hide();
            }
            clock.Reset(roundDuration);
            ShowTime();

            // SetActive shows or hides a whole GameObject, including its children (so hiding
            // the button also hides its label).
            roundButton.gameObject.SetActive(false);
            messageText.gameObject.SetActive(false);
            noMovesMessageTimeLeft = 0f;
            settingsMenu.SetAvailable(false);
            leaderboardMenu.SetAvailable(false);
            accountMenu.SetAvailable(false);
            SetAchievementsAvailable(false);
            // For a signed-in player, this registers the round with the global leaderboard (in
            // the background), so its score can be posted when it ends.
            leaderboardMenu.RoundStarted();
            SetInputEnabled(true);

            state = GameState.Playing;
        }

        /// <summary>Freezes the round: stops time, blocks input, and blurs the board.</summary>
        private void Pause()
        {
            pausedFrom = state;
            state = GameState.Paused;

            // Time.timeScale is how fast game time passes: 1 is normal speed, 0 is stopped.
            // At 0, Time.deltaTime is 0, so everything that moves by deltaTime freezes: our
            // timer, the swap and fall animations, and WaitForSeconds in the board's coroutines.
            // (UI buttons still work because the UI uses Time.unscaledDeltaTime, real time.)
            Time.timeScale = 0f;

            // timeScale doesn't touch audio: a sound that's playing would carry on over the
            // pause screen. AudioListener.pause pauses every AudioSource (mid-sound, so they pick
            // up where they left off on resume). A sound requested while paused starts off paused
            // too, so nothing is heard until Resume.
            AudioListener.pause = true;

            SetInputEnabled(false);
            boardBlur.Show();
            pauseOverlay.SetActive(true);
        }

        /// <summary>Picks the round back up exactly where it stopped. Also the Resume button.</summary>
        private void Resume()
        {
            if (state != GameState.Paused)
            {
                return;
            }

            state = pausedFrom;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            pauseOverlay.SetActive(false);
            boardBlur.Hide();

            // If the clock had already hit 0 (we were only waiting for a cascade or the Last
            // Hurrah to finish), swapping stays off.
            SetInputEnabled(!clock.HasRunOut);
        }

        private void EnterReady()
        {
            state = GameState.Ready;
            SetInputEnabled(false);
            clock.Reset(roundDuration);
            ShowTime();
            messageText.gameObject.SetActive(false);
            ShowBestUnderScore();
            ShowButton("Start Round");
            settingsMenu.SetAvailable(true);
            leaderboardMenu.SetAvailable(true);
            accountMenu.SetAvailable(true);
            SetAchievementsAvailable(true);
        }

        // Called on the same frame the Board started the hurrah. The Board waits a moment
        // before the first gem goes off (its Last Hurrah Delay), so the banner is read first.
        private void EnterLastHurrah()
        {
            state = GameState.LastHurrah;
            SetInputEnabled(false);
            // The banner takes over the message; don't let a "No more moves!" countdown hide it.
            noMovesMessageTimeLeft = 0f;
            messageText.text = "Last Hurrah!";
            messageText.gameObject.SetActive(true);
        }

        private void EnterRoundOver()
        {
            state = GameState.RoundOver;
            SetInputEnabled(false);
            // "Time's up!" takes over the message from here; don't let the countdown hide it.
            noMovesMessageTimeLeft = 0f;
            ShowRoundSummary();
            ShowButton("Play Again");
            settingsMenu.SetAvailable(true);
            leaderboardMenu.SetAvailable(true);
            accountMenu.SetAvailable(true);
            SetAchievementsAvailable(true);

            // If the score makes the local top 10, this pops up the initials prompt over the
            // round-end screen. A signed-in player's score also goes to the global leaderboard.
            leaderboardMenu.OfferEntry(scoreKeeper.Total);
        }

        /// <summary>
        /// Called by the Board when it has run out of moves and is about to reshuffle. Shows a
        /// short message so the tokens moving by themselves aren't a surprise.
        /// </summary>
        private void ShowNoMovesMessage()
        {
            // The board can also run dry on the cascade that ends the round. "Time's up!" is
            // about to replace the message then, so don't flash this one first. A duration of 0
            // means "no message": showing it would leave nothing counting down to hide it again.
            if (state != GameState.Playing || clock.HasRunOut || noMovesMessageDuration <= 0f)
            {
                return;
            }

            messageText.text = "No more moves!\nShuffling...";
            messageText.gameObject.SetActive(true);
            noMovesMessageTimeLeft = noMovesMessageDuration;
        }

        /// <summary>
        /// Compares the final score with the personal best, saves it if it's a new one, and
        /// shows the summary. By now the Last Hurrah is over, so its points are in the score.
        /// </summary>
        private void ShowRoundSummary()
        {
            int score = scoreKeeper.Total;
            // Read the best before recording this round, or the round would be compared with itself.
            int? previousBest = personalBest.Best;
            personalBest.Record(score);

            // The stats as the server gets them: OfferEntry, just after this, reads the same
            // tracker on the same frame. Without a tracker, the stat lines are all zeros.
            RoundStats stats = roundStats != null ? roundStats.Stats : new RoundStats();
            RoundSummary summary = RoundSummary.Create(score, previousBest, stats);

            if (roundSummaryView != null)
            {
                messageText.text = RoundSummary.Headline;
                roundSummaryView.Show(summary);
            }
            else
            {
                messageText.text = summary.AllLines();
            }
            messageText.gameObject.SetActive(true);
        }

        private void ShowBestUnderScore()
        {
            if (scoreView != null)
            {
                scoreView.ShowBest(personalBest.Best);
            }
        }

        // The Achievements menu is the one menu the game can run without, so it's the one
        // reference that's allowed to be empty.
        private void SetAchievementsAvailable(bool available)
        {
            if (achievementsMenu != null)
            {
                achievementsMenu.SetAvailable(available);
            }
        }

        private void ShowButton(string label)
        {
            roundButtonLabel.text = label;
            roundButton.gameObject.SetActive(true);
        }

        // Turning the BoardInput component off stops its Update from running at all, so no
        // presses reach the board. Its OnDisable also cancels any drag that was in progress.
        private void SetInputEnabled(bool inputEnabled)
        {
            boardInput.enabled = inputEnabled;
        }

        // Plays the tick or horn the clock asked for, if any. The clock only advances from
        // UpdatePlaying, so a pause simply stops it being called, and a hold makes it stand
        // still: either way no tick is repeated or skipped.
        private void PlayCountdownCue(CountdownCue cue)
        {
            if (cue == CountdownCue.Tick)
            {
                audioManager.Play(SoundId.CountdownTick, clock.SecondsShown == 1 ? FinalTickPitch : 1f, TickVolume);
            }
            else if (cue == CountdownCue.Horn)
            {
                audioManager.Play(SoundId.RoundEndHorn);
            }
        }

        /// <summary>
        /// Which countdown sound (if any) to play when the timer display goes from
        /// <paramref name="previousSecond"/> to <paramref name="currentSecond"/>: a tick on
        /// reaching each second from <paramref name="countdownFrom"/> down to 1, the horn on
        /// reaching 0.
        /// </summary>
        // This is "edge-triggered": it reacts to the value *changing*, not to the value itself.
        // The timer shows 0:03 for about 60 frames in a row; asking "is it 3?" would tick on
        // every one of them, while asking "did it just become 3?" ticks once.
        //
        // It's `static` and `public` (it uses nothing but its arguments) so the Edit Mode tests
        // can check the timing rules with plain numbers, without running a round.
        public static CountdownCue GetCountdownCue(int previousSecond, int currentSecond, int countdownFrom)
        {
            // Same second as last frame, or the clock went back up (a new round): nothing new.
            if (currentSecond >= previousSecond)
            {
                return CountdownCue.None;
            }
            // Checked before the tick, so a slow frame that jumps straight from 0:02 to 0:00
            // still sounds the horn. Only the *current* second is looked at, which is also why
            // a frame that skips a second gives one cue, not one per second skipped.
            if (currentSecond <= 0)
            {
                return CountdownCue.Horn;
            }
            return currentSecond <= countdownFrom ? CountdownCue.Tick : CountdownCue.None;
        }

        /// <summary>
        /// Displays the time left as m:ss: in red once it's down to the warning time, and in the
        /// held color while the clock is held.
        /// </summary>
        private void ShowTime()
        {
            int totalSeconds = clock.SecondsShown;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            // ":00" pads the seconds to two digits, so 5 seconds shows as "0:05", not "0:5".
            timerText.text = $"{minutes}:{seconds:00}";
            // The held color wins over red: in the last seconds it matters most that the player
            // sees the clock has stopped.
            // Otherwise, compare the real time left (not the rounded display) so fractional
            // warning times work too. For whole seconds it's the same: 0:10 appears exactly when
            // it turns red.
            if (clock.IsHeld)
            {
                timerText.color = timerHeldColor;
            }
            else
            {
                timerText.color = clock.TimeRemaining <= warningTime ? timerWarningColor : timerColor;
            }
        }
    }
}
