using System;
using System.Collections;
using UnityEngine;

namespace Match3.Accounts
{
    /// <summary>
    /// Who's signed in, if anyone, with their cartergrove.me account. A state machine:
    ///   SignedOut - nobody. <see cref="SignIn"/> starts signing in.
    ///   SigningIn - waiting for the sign-in page, then for the code to be traded for tokens.
    ///               Ends in SignedIn, or back in SignedOut with a <see cref="Message"/>.
    ///   SignedIn  - we have a refresh token (saved, so it survives a reload) and the
    ///               player's name. <see cref="SignOut"/> goes back to SignedOut.
    /// Every change raises <see cref="Changed"/>, which is how the Account menu keeps up.
    ///
    /// Other code (the global leaderboard, later) asks for an access token with
    /// <see cref="GetAccessToken"/>, which refreshes it first if it's about to expire.
    ///
    /// Accounts are optional: nothing here ever stops the game. If the accounts server is down
    /// or the player is offline, we say so in <see cref="Message"/> and carry on.
    ///
    /// The platform-specific part (how the player gets to the sign-in page and back) is an
    /// <see cref="IAuthFlow"/>. Only the browser build has one so far; elsewhere
    /// <see cref="CanSignIn"/> is false.
    /// </summary>
    public class AccountManager : MonoBehaviour
    {
        public enum AccountState { SignedOut, SigningIn, SignedIn }

        // Refresh the access token when it has less than this left, so a token we hand out
        // doesn't expire on its way to the server.
        private const double RefreshMarginSeconds = 60;

        [Tooltip("The accounts server. Only change it to test against a local server (see Docs/Accounts.md), and don't commit that.")]
        [SerializeField] private string issuer = AuthConfig.DefaultIssuer;

        /// <summary>Raised whenever <see cref="State"/>, the name, or <see cref="Message"/> changes.</summary>
        public event Action Changed;

        public AccountState State { get; private set; } = AccountState.SignedOut;

        /// <summary>The signed-in player's name, or null.</summary>
        public string DisplayName { get; private set; }

        /// <summary>The signed-in player's permanent id, or null.</summary>
        public string UserId { get; private set; }

        /// <summary>
        /// Something the player should know about the last thing that happened (e.g. "Sign-in
        /// was cancelled."), or null.
        /// </summary>
        public string Message { get; private set; }

        /// <summary>False where there's no way to sign in yet (the Editor and the Windows build).</summary>
        public bool CanSignIn => flow != null;

        private IAuthFlow flow;
        private ITokenClient tokenClient;
        private TokenStore store;

        // The access token lives only in memory; see TokenStore for why.
        private string accessToken;
        private DateTime accessTokenExpiresUtc;

        // True while a refresh request is out. The server rotates refresh tokens and treats a
        // reused old one as theft (revoking everything), so two refreshes must never overlap:
        // the second would send the token the first just used up.
        private bool refreshing;

        // Bumped every time the player signs out, so a request that was still in flight can
        // tell its answer is for an account that's gone.
        private int generation;

        private void Awake()
        {
            // Tests set these first (see ConfigureForTests); otherwise make the real ones.
            store ??= new TokenStore();
            tokenClient ??= new HttpTokenClient(issuer, AuthConfig.WebClientId);
#if UNITY_WEBGL && !UNITY_EDITOR
            flow ??= new WebRedirectAuthFlow(issuer);
#endif
        }

        private void Start()
        {
            // Coming back from the sign-in page? (Only the web flow ever says yes.)
            if (flow != null && flow.TryResume(out AuthFlowResult result))
            {
                SetState(AccountState.SigningIn);
                OnFlowFinished(result);
                return;
            }

            // Signed in on an earlier visit? Then we're signed in now, straight away, with the
            // saved name; the refresh below gets a fresh access token and checks the account
            // is still good. If the network is down, the player stays signed in regardless.
            if (store.HasRefreshToken)
            {
                UserId = store.UserId;
                DisplayName = store.Name;
                SetState(AccountState.SignedIn);
                StartCoroutine(RefreshAccessToken());
            }
            else
            {
                // Nothing changes, but let listeners draw the signed-out state.
                Changed?.Invoke();
            }
        }

        /// <summary>Starts signing in. In the browser this leaves the page and comes back.</summary>
        public void SignIn()
        {
            if (State != AccountState.SignedOut || !CanSignIn)
            {
                return;
            }
            Message = null;
            SetState(AccountState.SigningIn);
            StartCoroutine(flow.SignIn(OnFlowFinished));
        }

        /// <summary>Forgets the account here and asks the server to revoke its refresh token.</summary>
        public void SignOut()
        {
            if (State != AccountState.SignedIn)
            {
                return;
            }
            string refreshToken = store.RefreshToken;
            Forget();
            Message = null;
            SetState(AccountState.SignedOut);

            // Revoking is best effort: even if it fails, this game has already forgotten the
            // token, and it expires on its own in 30 days.
            if (refreshToken.Length > 0)
            {
                StartCoroutine(tokenClient.Revoke(refreshToken));
            }
        }

        /// <summary>
        /// A coroutine that calls <paramref name="done"/> with a valid access token, refreshing
        /// it first if needed, or with null if nobody is signed in or the server can't be
        /// reached. Use it like:
        /// <code>yield return accountManager.GetAccessToken(token => ...);</code>
        /// </summary>
        public IEnumerator GetAccessToken(Action<string> done)
        {
            if (State == AccountState.SignedIn && !HasFreshAccessToken)
            {
                // Start a refresh unless one is already out, then wait for it to finish.
                // StartCoroutine runs the new coroutine up to its first "yield" right away, so
                // `refreshing` is already true when the next caller checks it.
                if (!refreshing)
                {
                    StartCoroutine(RefreshAccessToken());
                }
                while (refreshing)
                {
                    yield return null;
                }
            }
            done(State == AccountState.SignedIn && HasFreshAccessToken ? accessToken : null);
        }

        private bool HasFreshAccessToken =>
            accessToken != null && (accessTokenExpiresUtc - DateTime.UtcNow).TotalSeconds > RefreshMarginSeconds;

        // Called by the auth flow with the authorization code (or an error).
        private void OnFlowFinished(AuthFlowResult result)
        {
            if (!result.Succeeded)
            {
                Message = result.Error;
                SetState(AccountState.SignedOut);
                return;
            }
            StartCoroutine(ExchangeCode(result));
        }

        private IEnumerator ExchangeCode(AuthFlowResult code)
        {
            TokenResult result = null;
            // "yield return" an IEnumerator runs it as part of this coroutine and carries on
            // here once it has finished.
            yield return tokenClient.ExchangeCode(code, r => result = r);

            if (result == null || !result.Succeeded)
            {
                Debug.LogWarning($"Accounts: code exchange failed ({result?.Error}).");
                Message = "Couldn't sign in: the account server didn't accept the sign-in. You can keep playing without an account.";
                SetState(AccountState.SignedOut);
                yield break;
            }
            Keep(result);
            SetState(AccountState.SignedIn);
        }

        private IEnumerator RefreshAccessToken()
        {
            if (refreshing)
            {
                yield break; // One is already out; never send two (see `refreshing`).
            }

            int startedIn = generation;
            TokenResult result = null;
            refreshing = true;
            try
            {
                yield return tokenClient.Refresh(store.RefreshToken, r => result = r);
            }
            finally
            {
                // "finally" runs even if the coroutine is stopped (e.g. the object destroyed)
                // while waiting, so the flag can't get stuck on.
                refreshing = false;
            }

            if (generation != startedIn || State != AccountState.SignedIn)
            {
                // Signed out while we waited. Don't resurrect the account; just make sure the
                // brand-new refresh token (if we got one) is revoked too.
                if (result != null && result.Succeeded && !string.IsNullOrEmpty(result.RefreshToken))
                {
                    StartCoroutine(tokenClient.Revoke(result.RefreshToken));
                }
                yield break;
            }

            if (result != null && result.Succeeded)
            {
                Keep(result);
                Message = null;
                Changed?.Invoke();
            }
            else if (result != null && result.Rejected)
            {
                // The server says the refresh token is no good (expired after 30 days, revoked,
                // or signed out elsewhere). The account here is over.
                Debug.LogWarning($"Accounts: refresh rejected ({result.Error}); signing out.");
                Forget();
                Message = "Your sign-in has expired. Please sign in again.";
                SetState(AccountState.SignedOut);
            }
            else
            {
                Debug.LogWarning($"Accounts: refresh failed ({result?.Error}).");
                Message = "Couldn't reach the account server. You're still signed in, and can keep playing.";
                Changed?.Invoke();
            }
        }

        // Takes in a successful token response: the access token (memory only), the rotated
        // refresh token and the profile (saved).
        private void Keep(TokenResult result)
        {
            accessToken = result.AccessToken;
            accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(result.ExpiresInSeconds);

            JwtClaims claims = JwtClaims.Read(result.AccessToken);
            // The server always sends a new refresh token (rotation), but if one ever didn't,
            // the old one is still the one to keep.
            string refreshToken = string.IsNullOrEmpty(result.RefreshToken) ? store.RefreshToken : result.RefreshToken;
            store.Save(refreshToken, claims);

            UserId = store.UserId;
            DisplayName = store.Name;
            Message = null;
        }

        private void Forget()
        {
            generation++;
            store.Clear();
            accessToken = null;
            UserId = null;
            DisplayName = null;
        }

        private void SetState(AccountState state)
        {
            State = state;
            Changed?.Invoke();
        }

        // ---- Test hooks -------------------------------------------------------------------
        // Internal, so only this assembly and the Play Mode tests (see AssemblyInfo.cs) see it.

        /// <summary>
        /// Swaps in fakes for the platform flow, the network and the saved data. Call it while
        /// the GameObject is still inactive, before Awake and Start run.
        /// </summary>
        internal void ConfigureForTests(IAuthFlow flow, ITokenClient tokenClient, TokenStore store)
        {
            this.flow = flow;
            this.tokenClient = tokenClient;
            this.store = store;
        }
    }
}
