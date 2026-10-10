using System.Globalization;
using Match3.Accounts;
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
    /// This is a state machine with four states:
    ///   Ready     - the game just launched. The board is shown, input is off, and the
    ///               "Start Round" button waits for a click.
    ///   Playing   - the timer is counting down and the player can swap.
    ///   Paused    - Esc or P was pressed mid-round, or the game lost focus. Time is frozen, the
    ///               board is blurred, and a "Paused" overlay is shown. Esc, P, or the Resume
    ///               button goes back to Playing.
    ///   RoundOver - time ran out. The final score and a "Play Again" button are shown.
    /// Clicking the button in Ready or RoundOver starts a new round (-> Playing).
    /// The Settings menu (gear button), the Leaderboard and the Account menu are only available
    /// in Ready and RoundOver. A RoundOver score that makes the top 10 is offered to the leaderboard, which
    /// asks for the player's initials. A signed-in player's rounds are also posted to the global
    /// leaderboard.
    ///
    /// It also plays the end-of-round countdown: a tick for each of the last few seconds and a
    /// horn when the clock hits 0:00.
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
        [Tooltip("Shows \"Time's up! Final score: X\" when a round ends. Hidden otherwise.")]
        [SerializeField] private TMP_Text messageText;
        [Tooltip("The one button used both to start the first round and to play again.")]
        [SerializeField] private Button roundButton;
        [SerializeField] private TMP_Text roundButtonLabel;

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

        private enum GameState { Ready, Playing, Paused, RoundOver }

        private GameState state;
        private float timeRemaining;
        // The whole second the timer showed last frame, so we can tell when it changes.
        private int lastSecondShown;
        private InputAction pauseAction;

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

            // InputSystem.actions is the project-wide actions asset (Assets/Settings/
            // InputSystem_Actions, set under Project Settings > Input System Package). Unity
            // enables it automatically, so we only need to find our action by "Map/Action" name.
            pauseAction = InputSystem.actions.FindAction("Player/Pause", throwIfNotFound: true);

            pauseOverlay.SetActive(false);
            EnterReady();
        }

        private void OnDestroy()
        {
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
            if (!hasFocus && state == GameState.Playing)
            {
                Pause();
            }
        }

        // Unity calls this with true when the app is suspended: minimized on some platforms,
        // sent to the background on mobile, or (in a browser) the tab being hidden. Focus loss
        // usually comes first, but not on every platform, so we handle both. Pause() is only
        // called from Playing, so getting both events can't pause twice.
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && state == GameState.Playing)
            {
                Pause();
            }
        }

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
            if (timeRemaining > 0f)
            {
                // Time.deltaTime is the number of seconds since the last frame. Subtracting it
                // every frame makes the timer count real seconds whatever the frame rate is.
                timeRemaining = Mathf.Max(0f, timeRemaining - Time.deltaTime);
                ShowTime();
                PlayCountdownCue();

                if (timeRemaining <= 0f)
                {
                    // Time's up: no new swaps from here on.
                    SetInputEnabled(false);
                }
            }

            // Once the clock hits 0, a cascade that's still playing out is allowed to finish
            // (and score) before the round really ends, so we wait for the board to settle.
            if (timeRemaining <= 0f && !board.IsResolving)
            {
                EnterRoundOver();
            }
        }

        /// <summary>Starts a new round. Called by the Start Round / Play Again button.</summary>
        private void StartRound()
        {
            board.Regenerate();
            scoreKeeper.ResetScore();
            timeRemaining = roundDuration;
            ShowTime();
            // Start the countdown from the full clock, so last round's 0 isn't remembered.
            lastSecondShown = SecondsShown();

            // SetActive shows or hides a whole GameObject, including its children (so hiding
            // the button also hides its label).
            roundButton.gameObject.SetActive(false);
            messageText.gameObject.SetActive(false);
            settingsMenu.SetAvailable(false);
            leaderboardMenu.SetAvailable(false);
            accountMenu.SetAvailable(false);
            // For a signed-in player, this registers the round with the global leaderboard (in
            // the background), so its score can be posted when it ends.
            leaderboardMenu.RoundStarted();
            SetInputEnabled(true);

            state = GameState.Playing;
        }

        /// <summary>Freezes the round: stops time, blocks input, and blurs the board.</summary>
        private void Pause()
        {
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

            state = GameState.Playing;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            pauseOverlay.SetActive(false);
            boardBlur.Hide();

            // If the clock had already hit 0 (we were only waiting for a cascade to finish),
            // swapping stays off.
            SetInputEnabled(timeRemaining > 0f);
        }

        private void EnterReady()
        {
            state = GameState.Ready;
            SetInputEnabled(false);
            timeRemaining = roundDuration;
            ShowTime();
            messageText.gameObject.SetActive(false);
            ShowButton("Start Round");
            settingsMenu.SetAvailable(true);
            leaderboardMenu.SetAvailable(true);
            accountMenu.SetAvailable(true);
        }

        private void EnterRoundOver()
        {
            state = GameState.RoundOver;
            SetInputEnabled(false);
            string finalScore = scoreKeeper.Total.ToString("N0", CultureInfo.InvariantCulture);
            messageText.text = $"Time's up!\nFinal score: {finalScore}";
            messageText.gameObject.SetActive(true);
            ShowButton("Play Again");
            settingsMenu.SetAvailable(true);
            leaderboardMenu.SetAvailable(true);
            accountMenu.SetAvailable(true);

            // If the score makes the local top 10, this pops up the initials prompt over the
            // round-end screen. A signed-in player's score also goes to the global leaderboard.
            leaderboardMenu.OfferEntry(scoreKeeper.Total);
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

        // Plays a tick or the horn if the timer display just changed to a second that has one.
        // Because this only runs from UpdatePlaying, a pause simply stops it being called, and
        // lastSecondShown is still there on resume: no tick is repeated or skipped.
        private void PlayCountdownCue()
        {
            int second = SecondsShown();
            CountdownCue cue = GetCountdownCue(lastSecondShown, second, CountdownFrom);
            lastSecondShown = second;

            if (cue == CountdownCue.Tick)
            {
                audioManager.Play(SoundId.CountdownTick, second == 1 ? FinalTickPitch : 1f, TickVolume);
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

        // Round up, so the display reads 1:00 for the whole first second and only shows 0:00
        // once time has really run out.
        private int SecondsShown()
        {
            return Mathf.CeilToInt(timeRemaining);
        }

        /// <summary>Displays the time left as m:ss, in red once it's down to the warning time.</summary>
        private void ShowTime()
        {
            int totalSeconds = SecondsShown();
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            // ":00" pads the seconds to two digits, so 5 seconds shows as "0:05", not "0:5".
            timerText.text = $"{minutes}:{seconds:00}";
            // Compare the real time left (not the rounded display) so fractional warning times
            // work too. For whole seconds it's the same: 0:10 appears exactly when it turns red.
            timerText.color = timeRemaining <= warningTime ? timerWarningColor : timerColor;
        }
    }
}
