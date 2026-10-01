using System;
using System.Collections;

namespace Match3.Accounts
{
    /// <summary>
    /// The platform-specific half of signing in: getting the player to the accounts server's
    /// sign-in page and bringing back an authorization code. What happens after that (trading
    /// the code for tokens, refreshing them, signing out) is the same everywhere and lives in
    /// <see cref="AccountManager"/>.
    ///
    /// Why an interface? The browser build and the Windows build get the code back in very
    /// different ways (a page redirect, <see cref="WebRedirectAuthFlow"/>, vs. a tiny local web
    /// server, <see cref="LoopbackAuthFlow"/>), and tests need a fake
    /// that answers instantly without any browser at all. AccountManager only talks to this
    /// interface, so any of them can be plugged in.
    /// </summary>
    public interface IAuthFlow
    {
        /// <summary>
        /// Starts sign-in, and calls <paramref name="done"/> with the result when it's over.
        ///
        /// It's an IEnumerator so it can run as a coroutine and wait for the player without
        /// freezing the game. The web flow is special: it navigates the page away, so the game
        /// is unloaded and <paramref name="done"/> is never called. The result shows up on the
        /// next page load instead, through <see cref="TryResume"/>.
        /// </summary>
        IEnumerator SignIn(Action<AuthFlowResult> done);

        /// <summary>
        /// Called once when the game starts. If this launch is the player coming back from a
        /// sign-in started earlier (the web flow's redirect), returns true with the result.
        /// Otherwise returns false.
        /// </summary>
        bool TryResume(out AuthFlowResult result);

        /// <summary>
        /// Gives up on a <see cref="SignIn"/> that's still waiting for the player: it then calls
        /// its <c>done</c> with a failure. Also called when the game shuts down, so nothing is
        /// left running. Does nothing if no sign-in is running.
        /// </summary>
        void Cancel();
    }

    /// <summary>
    /// What an <see cref="IAuthFlow"/> hands back: either an authorization code plus everything
    /// needed to exchange it for tokens, or an error message for the player.
    /// </summary>
    public sealed class AuthFlowResult
    {
        public bool Succeeded => Error == null;
        public string Code { get; private set; }
        /// <summary>The PKCE verifier whose challenge was sent with this sign-in.</summary>
        public string CodeVerifier { get; private set; }
        /// <summary>The redirect URI used; the token request has to repeat it exactly.</summary>
        public string RedirectUri { get; private set; }
        public string ClientId { get; private set; }
        /// <summary>A message for the player if sign-in failed, else null.</summary>
        public string Error { get; private set; }

        public static AuthFlowResult Success(string code, string codeVerifier, string redirectUri, string clientId)
        {
            return new AuthFlowResult
            {
                Code = code,
                CodeVerifier = codeVerifier,
                RedirectUri = redirectUri,
                ClientId = clientId,
            };
        }

        public static AuthFlowResult Failure(string error) => new AuthFlowResult { Error = error };
    }
}
