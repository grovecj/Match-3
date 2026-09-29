using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// Runs the round: a 60-second countdown that turns the board into a game. It decides when
    /// the player may swap, resets the board and score between rounds, and drives the timer,
    /// message, and Start/Play Again button in the side panel.
    ///
    /// This is a state machine with three states:
    ///   Ready     - the game just launched. The board is shown, input is off, and the
    ///               "Start Round" button waits for a click.
    ///   Playing   - the timer is counting down and the player can swap.
    ///   RoundOver - time ran out. The final score and a "Play Again" button are shown.
    /// Clicking the button in Ready or RoundOver starts a new round (-> Playing).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Gameplay")]
        [SerializeField] private Board board;
        [SerializeField] private BoardInput boardInput;
        [SerializeField] private ScoreKeeper scoreKeeper;
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

        private enum GameState { Ready, Playing, RoundOver }

        private GameState state;
        private float timeRemaining;

        private void Start()
        {
            if (board == null || boardInput == null || scoreKeeper == null ||
                timerText == null || messageText == null || roundButton == null || roundButtonLabel == null)
            {
                Debug.LogError("GameManager: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            // Button.onClick is a UnityEvent: a list of methods to call when the button is
            // clicked. Listeners can be added in the Inspector (the "On Click ()" list) or, as
            // here, from code, which keeps the wiring visible next to the method it calls.
            roundButton.onClick.AddListener(StartRound);

            EnterReady();
        }

        private void OnDestroy()
        {
            if (roundButton != null)
            {
                roundButton.onClick.RemoveListener(StartRound);
            }
        }

        private void Update()
        {
            // Each state gets its own case, so it's easy to see what happens every frame
            // in each one. Only Playing has per-frame work: running the clock.
            switch (state)
            {
                case GameState.Ready:
                case GameState.RoundOver:
                    // Nothing to do; we're waiting for a button click.
                    break;

                case GameState.Playing:
                    UpdatePlaying();
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

            // SetActive shows or hides a whole GameObject, including its children (so hiding
            // the button also hides its label).
            roundButton.gameObject.SetActive(false);
            messageText.gameObject.SetActive(false);
            SetInputEnabled(true);

            state = GameState.Playing;
        }

        private void EnterReady()
        {
            state = GameState.Ready;
            SetInputEnabled(false);
            timeRemaining = roundDuration;
            ShowTime();
            messageText.gameObject.SetActive(false);
            ShowButton("Start Round");
        }

        private void EnterRoundOver()
        {
            state = GameState.RoundOver;
            SetInputEnabled(false);
            string finalScore = scoreKeeper.Total.ToString("N0", CultureInfo.InvariantCulture);
            messageText.text = $"Time's up!\nFinal score: {finalScore}";
            messageText.gameObject.SetActive(true);
            ShowButton("Play Again");
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

        /// <summary>Displays the time left as m:ss, in red once it's down to the warning time.</summary>
        private void ShowTime()
        {
            // Round up, so the display reads 1:00 for the whole first second and only shows
            // 0:00 once time has really run out.
            int totalSeconds = Mathf.CeilToInt(timeRemaining);
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
