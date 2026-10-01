// This whole file only exists in browser builds. UNITY_WEBGL is also defined in the Editor
// while the build target is Web, but the Editor can't call into a .jslib (there's no browser
// page around it), hence the "&& !UNITY_EDITOR".
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Match3.Accounts
{
    /// <summary>
    /// Sign-in for the browser build, by redirect:
    ///
    ///   1. <see cref="SignIn"/> makes a PKCE verifier and a state, saves them in the tab's
    ///      sessionStorage, and sends this same tab to the accounts server's sign-in page. The
    ///      game unloads.
    ///   2. The player signs in with Google. The accounts server sends the tab back to our page
    ///      with <c>?code=...&amp;state=...</c> on the address.
    ///   3. The game loads again from scratch. <see cref="TryResume"/> reads the code from the
    ///      address, checks the state against the saved one, and tidies the address bar.
    ///
    /// Why the same tab? <c>Application.OpenURL</c> opens a new tab, and then there'd be two
    /// copies of the game and a lot of confusion about which one gets the code.
    ///
    /// Why sessionStorage rather than PlayerPrefs for the verifier and state? In a browser,
    /// PlayerPrefs.Save() copies to IndexedDB <i>asynchronously</i>, and we navigate away the
    /// moment they're saved, so they could be lost. sessionStorage is written immediately, only
    /// lives as long as the tab, and is only readable by our own site: exactly what a one-off
    /// sign-in secret needs.
    ///
    /// The browser-side code is in Assets/Plugins/WebGL/Auth.jslib.
    /// </summary>
    public sealed class WebRedirectAuthFlow : IAuthFlow
    {
        private const string VerifierKey = "match3.auth.verifier";
        private const string StateKey = "match3.auth.state";
        private const string RedirectUriKey = "match3.auth.redirectUri";

        // [DllImport("__Internal")] declares a function that's implemented outside C#; in a
        // browser build "__Internal" means our .jslib. Each name must match a function in it.
        [DllImport("__Internal")] private static extern void Match3Auth_Navigate(string url);
        [DllImport("__Internal")] private static extern void Match3Auth_ReplaceUrl(string url);
        [DllImport("__Internal")] private static extern void Match3Auth_SessionSet(string key, string value);
        [DllImport("__Internal")] private static extern string Match3Auth_SessionGet(string key);
        [DllImport("__Internal")] private static extern void Match3Auth_SessionRemove(string key);

        private readonly string issuer;

        public WebRedirectAuthFlow(string issuer)
        {
            this.issuer = issuer;
        }

        public IEnumerator SignIn(Action<AuthFlowResult> done)
        {
            string verifier = Pkce.CreateVerifier();
            string state = Pkce.RandomString(16);
            // Come back to this very page (e.g. https://games.cartergrove.me/match3/), minus
            // any query. The accounts server only accepts redirect URIs registered for
            // match3-web, so this has to be exactly one of those.
            string redirectUri = AuthCallback.WithoutQuery(Application.absoluteURL);

            Match3Auth_SessionSet(VerifierKey, verifier);
            Match3Auth_SessionSet(StateKey, state);
            Match3Auth_SessionSet(RedirectUriKey, redirectUri);

            string url = AuthConfig.AuthorizeUrl(issuer, AuthConfig.WebClientId, redirectUri,
                Pkce.CreateChallenge(verifier), state);
            Match3Auth_Navigate(url);

            // The page is on its way out; `done` is never called. The result arrives through
            // TryResume after the redirect back.
            yield break;
        }

        public bool TryResume(out AuthFlowResult result)
        {
            string url = Application.absoluteURL;
            if (!AuthCallback.TryParse(url, out AuthCallback callback))
            {
                result = null;
                return false;
            }

            string verifier = Match3Auth_SessionGet(VerifierKey);
            string expectedState = Match3Auth_SessionGet(StateKey);
            string redirectUri = Match3Auth_SessionGet(RedirectUriKey);

            // Whatever happens next, this sign-in attempt is used up: forget its secrets, and
            // take the code off the address bar so a reload doesn't try it again.
            Match3Auth_SessionRemove(VerifierKey);
            Match3Auth_SessionRemove(StateKey);
            Match3Auth_SessionRemove(RedirectUriKey);
            Match3Auth_ReplaceUrl(AuthCallback.RemoveCallbackParameters(url));

            string error = callback.Validate(expectedState);
            if (error == null && (string.IsNullOrEmpty(verifier) || string.IsNullOrEmpty(redirectUri)))
            {
                error = "Sign-in failed: the sign-in request was lost. Please try again.";
            }

            result = error == null
                ? AuthFlowResult.Success(callback.Code, verifier, redirectUri, AuthConfig.WebClientId)
                : AuthFlowResult.Failure(error);
            return true;
        }

        /// <summary>Nothing to cancel: by the time anyone could, this page has navigated away.</summary>
        public void Cancel()
        {
        }
    }
}
#endif
