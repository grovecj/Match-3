using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Match3.Accounts
{
    /// <summary>
    /// The Account button in the side panel and the modal window it opens. The window shows
    /// "Sign in with Google" when signed out, and the player's name with a Sign out button when
    /// signed in, plus any message from the <see cref="AccountManager"/> (such as "Sign-in was
    /// cancelled."). While the Windows build waits for the player to finish in their browser,
    /// it shows a Cancel button instead.
    ///
    /// This class only displays things and forwards clicks; the AccountManager does the actual
    /// work and raises Changed when there's something new to show.
    ///
    /// Like Settings and the Leaderboard, the GameManager only allows it between rounds, by
    /// calling <see cref="SetAvailable"/>.
    /// </summary>
    public class AccountMenu : MonoBehaviour
    {
        private const string ButtonLabel = "Account";

        [SerializeField] private AccountManager accountManager;

        [Header("Side panel")]
        [Tooltip("The Account button in the side panel that opens the window.")]
        [SerializeField] private Button openButton;
        [Tooltip("The button's label: \"Account\", or the player's name when signed in.")]
        [SerializeField] private TMP_Text openButtonLabel;

        [Header("Window")]
        [Tooltip("The full-screen modal (dimmed backdrop + window).")]
        [SerializeField] private GameObject panel;
        [Tooltip("\"Not signed in\", \"Signing in...\" or \"Signed in as\".")]
        [SerializeField] private TMP_Text statusText;
        [Tooltip("The player's name, shown when signed in.")]
        [SerializeField] private TMP_Text nameText;
        [Tooltip("The AccountManager's message, if there is one.")]
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button signInButton;
        [SerializeField] private Button signOutButton;
        [Tooltip("Stops waiting for the browser while signing in.")]
        [SerializeField] private Button cancelSignInButton;
        [SerializeField] private Button closeButton;

        private InputAction cancelAction;
        private AccountManager.AccountState lastState;

        public bool IsOpen => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (accountManager == null || openButton == null || openButtonLabel == null || panel == null ||
                statusText == null || nameText == null || messageText == null ||
                signInButton == null || signOutButton == null || cancelSignInButton == null || closeButton == null)
            {
                // Built before the Cancel button existed? Run Match3 > Build Account Menu again.
                Debug.LogError("AccountMenu: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            signInButton.onClick.AddListener(accountManager.SignIn);
            signOutButton.onClick.AddListener(accountManager.SignOut);
            cancelSignInButton.onClick.AddListener(accountManager.CancelSignIn);
            // An event, like UnityEvent but plain C#: "+=" adds our method to the list of
            // methods the AccountManager calls when it raises Changed.
            accountManager.Changed += OnAccountChanged;

            cancelAction = InputSystem.actions.FindAction("UI/Cancel", throwIfNotFound: true);

            panel.SetActive(false);
            lastState = accountManager.State;
            Refresh();
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
            if (accountManager != null)
            {
                if (signInButton != null)
                {
                    signInButton.onClick.RemoveListener(accountManager.SignIn);
                }
                if (signOutButton != null)
                {
                    signOutButton.onClick.RemoveListener(accountManager.SignOut);
                }
                if (cancelSignInButton != null)
                {
                    cancelSignInButton.onClick.RemoveListener(accountManager.CancelSignIn);
                }
                // Always unsubscribe from another object's event: otherwise it keeps a
                // reference to this (destroyed) menu and would call into it.
                accountManager.Changed -= OnAccountChanged;
            }
        }

        private void Update()
        {
            if (IsOpen && cancelAction.WasPressedThisFrame())
            {
                Close();
            }
        }

        /// <summary>Shows the Account button when true. When false (a round is running) it's hidden and the window closed.</summary>
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

        private void OnAccountChanged()
        {
            AccountManager.AccountState previous = lastState;
            lastState = accountManager.State;
            Refresh();

            // A sign-in that just failed (cancelled, bad state, server down): open the window so
            // the player sees why, if we're between rounds.
            bool signInFailed = previous == AccountManager.AccountState.SigningIn &&
                accountManager.State == AccountManager.AccountState.SignedOut;
            if (signInFailed && !IsOpen && openButton.gameObject.activeInHierarchy)
            {
                Open();
            }
            else if (IsOpen)
            {
                // The button that had focus may just have been hidden (e.g. Sign out).
                SelectDefault();
            }
        }

        /// <summary>Updates every text and button to match the AccountManager.</summary>
        private void Refresh()
        {
            AccountManager.AccountState state = accountManager.State;
            bool signedIn = state == AccountManager.AccountState.SignedIn;
            string playerName = accountManager.DisplayName;

            switch (state)
            {
                case AccountManager.AccountState.SigningIn:
                    statusText.text = accountManager.CanCancelSignIn
                        ? "Finish signing in in your browser..."
                        : "Signing in...";
                    break;
                case AccountManager.AccountState.SignedIn:
                    statusText.text = "Signed in as";
                    break;
                default:
                    statusText.text = accountManager.CanSignIn
                        ? "Not signed in"
                        : "Signing in isn't available here.";
                    break;
            }

            nameText.text = signedIn ? (string.IsNullOrEmpty(playerName) ? "(no name)" : playerName) : "";
            nameText.gameObject.SetActive(signedIn);

            string message = accountManager.Message;
            messageText.text = message ?? "";
            messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));

            signInButton.gameObject.SetActive(state == AccountManager.AccountState.SignedOut && accountManager.CanSignIn);
            signOutButton.gameObject.SetActive(signedIn);
            cancelSignInButton.gameObject.SetActive(accountManager.CanCancelSignIn);

            openButtonLabel.text = signedIn && !string.IsNullOrEmpty(playerName) ? playerName : ButtonLabel;
        }

        private void Open()
        {
            Refresh();
            panel.SetActive(true);
            SelectDefault();
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            panel.SetActive(false);
            Select(openButton.gameObject.activeInHierarchy ? openButton : null);
        }

        // Focus the window's main action: Sign in, else Sign out, else Cancel, else Close.
        private void SelectDefault()
        {
            if (signInButton.gameObject.activeSelf)
            {
                Select(signInButton);
            }
            else if (signOutButton.gameObject.activeSelf)
            {
                Select(signOutButton);
            }
            else if (cancelSignInButton.gameObject.activeSelf)
            {
                Select(cancelSignInButton);
            }
            else
            {
                Select(closeButton);
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
            yield return null;
            if (selectable != null && selectable.gameObject.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            }
        }
    }
}
