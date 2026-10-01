using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Match3.Accounts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Tests
{
    /// <summary>
    /// Play Mode tests for <see cref="AccountManager"/>'s states (SignedOut / SigningIn /
    /// SignedIn), refreshing and signing out. A fake <see cref="IAuthFlow"/> stands in for the
    /// browser redirect and a fake <see cref="ITokenClient"/> for the accounts server, so these
    /// run anywhere, with no network. They're Play Mode tests because AccountManager runs its
    /// work as coroutines, which need real frames.
    /// </summary>
    public class AccountManagerPlayModeTests
    {
        // The tests' own PlayerPrefs keys, so they never touch a real signed-in account.
        private const string StorePrefix = "Match3Tests.Account.";
        private const int MaxFrames = 100;

        private GameObject gameObject;
        private AccountManager manager;
        private FakeAuthFlow flow;
        private FakeTokenClient tokens;
        private TokenStore store;
        private List<AccountManager.AccountState> changes;

        [SetUp]
        public void SetUp()
        {
            flow = new FakeAuthFlow();
            tokens = new FakeTokenClient();
            store = new TokenStore(StorePrefix);
            store.Clear();
            changes = new List<AccountManager.AccountState>();
        }

        [TearDown]
        public void TearDown()
        {
            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
            store.Clear();
        }

        // Makes the AccountManager with the fakes plugged in, and waits a frame for its Start.
        private IEnumerator CreateManager()
        {
            gameObject = new GameObject("AccountManager (test)");
            // While the GameObject is inactive, AddComponent doesn't run Awake yet, which gives
            // us the chance to plug the fakes in first.
            gameObject.SetActive(false);
            manager = gameObject.AddComponent<AccountManager>();
            manager.ConfigureForTests(flow, tokens, store);
            manager.Changed += () => changes.Add(manager.State);
            gameObject.SetActive(true);
            yield return null;
        }

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            for (int frame = 0; frame < MaxFrames && !condition(); frame++)
            {
                yield return null;
            }
            Assert.That(condition(), Is.True, "Timed out waiting.");
        }

        /// <summary>
        /// A JWT with the given claims. Only the payload matters to the game; the header and
        /// signature are placeholders.
        /// </summary>
        private static string Jwt(string sub, string name)
        {
            string payload = "{\"sub\":\"" + sub + "\",\"name\":\"" + name + "\"}";
            return "eyJhbGciOiJFUzI1NiJ9." + Base64Url.Encode(Encoding.UTF8.GetBytes(payload)) + ".c2ln";
        }

        private static AuthFlowResult Code() =>
            AuthFlowResult.Success("the-code", "the-verifier", "https://example.com/match3/", AuthConfig.WebClientId);

        [UnityTest]
        public IEnumerator Start_WithNothingSaved_IsSignedOut()
        {
            yield return CreateManager();

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedOut));
            Assert.That(manager.DisplayName, Is.Null);
            Assert.That(tokens.RefreshCalls, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SignIn_GoesThroughSigningInToSignedIn()
        {
            flow.Hold = true;
            flow.Result = Code();
            tokens.ExchangeResult = TokenResult.Success(Jwt("user-1", "Test Player"), "refresh-1", 900);
            yield return CreateManager();

            manager.SignIn();
            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SigningIn));
            yield return null;
            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SigningIn), "Should wait for the flow.");

            flow.Hold = false;
            yield return WaitFor(() => manager.State != AccountManager.AccountState.SigningIn);

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedIn));
            Assert.That(manager.DisplayName, Is.EqualTo("Test Player"));
            Assert.That(manager.UserId, Is.EqualTo("user-1"));
            Assert.That(manager.Message, Is.Null);
            Assert.That(tokens.ExchangedCode, Is.EqualTo("the-code"));
            Assert.That(store.RefreshToken, Is.EqualTo("refresh-1"));
            Assert.That(store.Name, Is.EqualTo("Test Player"));
            Assert.That(changes, Is.EqualTo(new[]
            {
                AccountManager.AccountState.SignedOut, // Start
                AccountManager.AccountState.SigningIn,
                AccountManager.AccountState.SigningIn, // The code is back: no longer cancellable
                AccountManager.AccountState.SignedIn,
            }));
        }

        [UnityTest]
        public IEnumerator CancelSignIn_WhileWaitingForTheBrowser_IsSignedOut()
        {
            flow.Hold = true;
            flow.Result = Code();
            yield return CreateManager();
            manager.SignIn();
            Assert.That(manager.CanCancelSignIn, Is.True);

            manager.CancelSignIn();
            yield return WaitFor(() => manager.State != AccountManager.AccountState.SigningIn);

            Assert.That(flow.CancelCalls, Is.EqualTo(1));
            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedOut));
            Assert.That(manager.Message, Is.EqualTo("Sign-in was cancelled."));
            Assert.That(manager.CanCancelSignIn, Is.False);
            Assert.That(tokens.ExchangedCode, Is.Null);
        }

        [UnityTest]
        public IEnumerator CancelSignIn_WhenNotSigningIn_DoesNothing()
        {
            yield return CreateManager();

            manager.CancelSignIn();

            Assert.That(flow.CancelCalls, Is.EqualTo(0));
            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedOut));
        }

        [UnityTest]
        public IEnumerator Destroy_WhileWaitingForTheBrowser_CancelsTheFlow()
        {
            flow.Hold = true;
            yield return CreateManager();
            manager.SignIn();

            UnityEngine.Object.Destroy(gameObject);
            yield return null;

            Assert.That(flow.CancelCalls, Is.EqualTo(1), "So the Windows flow frees its port.");
        }

        [UnityTest]
        public IEnumerator SignIn_FlowFails_IsSignedOutWithAMessage()
        {
            flow.Result = AuthFlowResult.Failure("Sign-in was cancelled.");
            yield return CreateManager();

            manager.SignIn();
            yield return WaitFor(() => manager.State != AccountManager.AccountState.SigningIn);

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedOut));
            Assert.That(manager.Message, Is.EqualTo("Sign-in was cancelled."));
            Assert.That(tokens.ExchangedCode, Is.Null, "A failed sign-in has no code to exchange.");
            Assert.That(store.HasRefreshToken, Is.False);
        }

        [UnityTest]
        public IEnumerator SignIn_ServerUnreachable_IsSignedOutWithAMessage()
        {
            flow.Result = Code();
            tokens.ExchangeResult = TokenResult.Failure("Cannot connect to destination host", rejected: false);
            yield return CreateManager();

            manager.SignIn();
            yield return WaitFor(() => manager.State != AccountManager.AccountState.SigningIn);

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedOut));
            Assert.That(manager.Message, Is.Not.Null);
            Assert.That(store.HasRefreshToken, Is.False);
        }

        [UnityTest]
        public IEnumerator Start_ComingBackFromTheRedirect_SignsIn()
        {
            // What the web flow does after the page reloads with ?code=...&state=...
            flow.ResumeResult = Code();
            tokens.ExchangeResult = TokenResult.Success(Jwt("user-1", "Test Player"), "refresh-1", 900);
            yield return CreateManager();

            yield return WaitFor(() => manager.State == AccountManager.AccountState.SignedIn);

            Assert.That(manager.DisplayName, Is.EqualTo("Test Player"));
            Assert.That(flow.SignInCalls, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Start_ComingBackWithABadState_IsSignedOutWithAMessage()
        {
            flow.ResumeResult = AuthFlowResult.Failure("Sign-in failed: the response didn't match the request.");
            yield return CreateManager();

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedOut));
            Assert.That(manager.Message, Does.Contain("didn't match"));
            Assert.That(tokens.ExchangedCode, Is.Null);
            Assert.That(changes, Is.EqualTo(new[]
            {
                AccountManager.AccountState.SigningIn,
                AccountManager.AccountState.SignedOut,
            }));
        }

        [UnityTest]
        public IEnumerator Start_WithASavedRefreshToken_IsSignedInAndRefreshes()
        {
            // Like reloading the page after signing in on an earlier visit.
            store.Save("refresh-1", new JwtClaims { sub = "user-1", name = "Saved Name" });
            tokens.RefreshResult = TokenResult.Success(Jwt("user-1", "New Name"), "refresh-2", 900);
            yield return CreateManager();

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedIn));
            yield return WaitFor(() => tokens.RefreshCalls == 1 && manager.DisplayName == "New Name");

            Assert.That(tokens.RefreshedWith, Is.EqualTo(new[] { "refresh-1" }));
            Assert.That(store.RefreshToken, Is.EqualTo("refresh-2"), "The rotated refresh token must be saved.");
        }

        [UnityTest]
        public IEnumerator Start_WithASavedRefreshToken_Offline_StaysSignedIn()
        {
            store.Save("refresh-1", new JwtClaims { sub = "user-1", name = "Saved Name" });
            tokens.RefreshResult = TokenResult.Failure("Cannot connect to destination host", rejected: false);
            yield return CreateManager();

            yield return WaitFor(() => tokens.RefreshCalls == 1 && manager.Message != null);

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedIn));
            Assert.That(manager.DisplayName, Is.EqualTo("Saved Name"));
            Assert.That(store.RefreshToken, Is.EqualTo("refresh-1"));
        }

        [UnityTest]
        public IEnumerator Start_WithARevokedRefreshToken_SignsOut()
        {
            store.Save("refresh-1", new JwtClaims { sub = "user-1", name = "Saved Name" });
            tokens.RefreshResult = TokenResult.Failure("invalid_grant", rejected: true);
            yield return CreateManager();

            yield return WaitFor(() => manager.State == AccountManager.AccountState.SignedOut);

            Assert.That(manager.Message, Is.Not.Null);
            Assert.That(manager.DisplayName, Is.Null);
            Assert.That(store.HasRefreshToken, Is.False);
        }

        [UnityTest]
        public IEnumerator SignOut_ForgetsTheAccountAndRevokesTheRefreshToken()
        {
            flow.Result = Code();
            tokens.ExchangeResult = TokenResult.Success(Jwt("user-1", "Test Player"), "refresh-1", 900);
            yield return CreateManager();
            manager.SignIn();
            yield return WaitFor(() => manager.State == AccountManager.AccountState.SignedIn);

            manager.SignOut();

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedOut));
            Assert.That(manager.DisplayName, Is.Null);
            Assert.That(store.HasRefreshToken, Is.False);
            Assert.That(store.Name, Is.Empty);
            yield return WaitFor(() => tokens.Revoked.Count == 1);
            Assert.That(tokens.Revoked[0], Is.EqualTo("refresh-1"));

            string token = "not called";
            yield return manager.GetAccessToken(t => token = t);
            Assert.That(token, Is.Null, "Nobody is signed in, so there's no token.");
        }

        [UnityTest]
        public IEnumerator GetAccessToken_WhileFresh_DoesNotRefresh()
        {
            string accessToken = Jwt("user-1", "Test Player");
            flow.Result = Code();
            tokens.ExchangeResult = TokenResult.Success(accessToken, "refresh-1", 900);
            yield return CreateManager();
            manager.SignIn();
            yield return WaitFor(() => manager.State == AccountManager.AccountState.SignedIn);

            string token = null;
            yield return manager.GetAccessToken(t => token = t);

            Assert.That(token, Is.EqualTo(accessToken));
            Assert.That(tokens.RefreshCalls, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator GetAccessToken_NearExpiry_RefreshesOnceForConcurrentCallers()
        {
            // A token that expires right away, so the next request has to refresh it.
            flow.Result = Code();
            tokens.ExchangeResult = TokenResult.Success(Jwt("user-1", "Test Player"), "refresh-1", 0);
            string refreshedAccessToken = Jwt("user-1", "Test Player");
            tokens.RefreshResult = TokenResult.Success(refreshedAccessToken, "refresh-2", 900);
            yield return CreateManager();
            manager.SignIn();
            yield return WaitFor(() => manager.State == AccountManager.AccountState.SignedIn);

            // Two callers at once. With rotating refresh tokens, a second refresh would reuse
            // "refresh-1" after it's been used, which the server treats as theft.
            string first = null;
            string second = null;
            manager.StartCoroutine(manager.GetAccessToken(t => first = t));
            manager.StartCoroutine(manager.GetAccessToken(t => second = t));
            yield return WaitFor(() => first != null && second != null);

            Assert.That(tokens.RefreshCalls, Is.EqualTo(1));
            Assert.That(first, Is.EqualTo(refreshedAccessToken));
            Assert.That(second, Is.EqualTo(refreshedAccessToken));
            Assert.That(store.RefreshToken, Is.EqualTo("refresh-2"));
        }

        [UnityTest]
        public IEnumerator Refresh_WaitsForAnotherTabsLease_ThenUsesItsNewToken()
        {
            // Another tab of the game is mid-refresh: it holds the lease.
            store.Save("refresh-1", new JwtClaims { sub = "user-1", name = "Saved Name" });
            Assert.That(store.TryTakeRefreshLease("other-tab"), Is.True);
            tokens.RefreshResult = TokenResult.Success(Jwt("user-1", "Test Player"), "refresh-3", 900);
            yield return CreateManager();

            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }
            Assert.That(tokens.RefreshCalls, Is.EqualTo(0), "Must not refresh while another tab holds the lease.");

            // The other tab finishes: it saves its rotated token and lets go.
            store.Save("refresh-2", null);
            store.ReleaseRefreshLease("other-tab");
            yield return WaitFor(() => tokens.RefreshCalls == 1 && store.RefreshToken == "refresh-3");

            Assert.That(tokens.RefreshedWith, Is.EqualTo(new[] { "refresh-2" }), "Should send the other tab's new token, not the retired one.");
            Assert.That(store.HoldsRefreshLease("other-tab"), Is.False);
            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedIn));
        }

        [UnityTest]
        public IEnumerator SignIn_WhileAlreadySignedIn_DoesNothing()
        {
            flow.Result = Code();
            tokens.ExchangeResult = TokenResult.Success(Jwt("user-1", "Test Player"), "refresh-1", 900);
            yield return CreateManager();
            manager.SignIn();
            yield return WaitFor(() => manager.State == AccountManager.AccountState.SignedIn);

            manager.SignIn();

            Assert.That(manager.State, Is.EqualTo(AccountManager.AccountState.SignedIn));
            Assert.That(flow.SignInCalls, Is.EqualTo(1));
        }

        // ---- Fakes ----------------------------------------------------------------------------

        /// <summary>Answers with <see cref="Result"/>, after waiting while <see cref="Hold"/> is true.</summary>
        private class FakeAuthFlow : IAuthFlow
        {
            public AuthFlowResult Result;
            public AuthFlowResult ResumeResult;
            public bool Hold;
            public int SignInCalls;
            public int CancelCalls;
            private bool cancelled;

            public IEnumerator SignIn(Action<AuthFlowResult> done)
            {
                SignInCalls++;
                cancelled = false;
                while (Hold && !cancelled)
                {
                    yield return null;
                }
                done(cancelled ? AuthFlowResult.Failure("Sign-in was cancelled.") : Result);
            }

            public void Cancel()
            {
                CancelCalls++;
                cancelled = true;
            }

            public bool TryResume(out AuthFlowResult result)
            {
                result = ResumeResult;
                return ResumeResult != null;
            }
        }

        /// <summary>Answers each request with the result set up for it, a frame later (like a real server, but faster).</summary>
        private class FakeTokenClient : ITokenClient
        {
            public TokenResult ExchangeResult;
            public TokenResult RefreshResult;
            public string ExchangedCode;
            public int RefreshCalls;
            public readonly List<string> RefreshedWith = new List<string>();
            public readonly List<string> Revoked = new List<string>();

            public IEnumerator ExchangeCode(AuthFlowResult code, Action<TokenResult> done)
            {
                ExchangedCode = code.Code;
                yield return null;
                done(ExchangeResult);
            }

            public IEnumerator Refresh(string refreshToken, Action<TokenResult> done)
            {
                RefreshCalls++;
                RefreshedWith.Add(refreshToken);
                yield return null;
                done(RefreshResult);
            }

            public IEnumerator Revoke(string refreshToken)
            {
                Revoked.Add(refreshToken);
                yield return null;
            }
        }
    }
}
