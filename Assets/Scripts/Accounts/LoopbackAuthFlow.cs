// Everywhere except the browser build (which has WebRedirectAuthFlow instead): the Windows
// build and the Editor. A web page can't open a listening socket, so there'd be no point.
#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Match3.Accounts
{
    /// <summary>
    /// Sign-in for the Windows build (and the Editor), the standard way for native apps
    /// (RFC 8252, "OAuth 2.0 for Native Apps"):
    ///
    ///   1. <see cref="SignIn"/> starts a tiny web server on this computer, at
    ///      <c>http://127.0.0.1:&lt;a free port&gt;/callback</c>, and makes a PKCE verifier and
    ///      a state.
    ///   2. It opens the player's own browser at the accounts server's sign-in page, with that
    ///      address as the redirect URI.
    ///   3. The player signs in with Google. The accounts server sends the browser to
    ///      <c>http://127.0.0.1:&lt;port&gt;/callback?code=...&amp;state=...</c>, which is our
    ///      little server. We check the state, answer with a "you can close this tab" page, stop
    ///      the server, and hand the code to the AccountManager.
    ///
    /// Why the system browser, and not a web view inside the game? The player can see it's the
    /// real sign-in page (address bar, padlock), may already be signed in to Google there, and
    /// the game never gets to see their Google password. Google also refuses sign-ins from
    /// embedded web views for exactly that reason.
    ///
    /// Why 127.0.0.1 and not "localhost"? "localhost" is just a name, and could resolve to
    /// something else (or to IPv6 only). 127.0.0.1 is always this computer. Listening only
    /// there also means nothing outside this computer can connect, so the Windows firewall
    /// doesn't ask the player about it (it only asks for programs that accept outside
    /// connections).
    ///
    /// The server waits up to <see cref="DefaultTimeoutSeconds"/>, and <see cref="Cancel"/>
    /// stops it early (the Account window's Cancel button). Either way the port is freed.
    /// </summary>
    public sealed class LoopbackAuthFlow : IAuthFlow
    {
        /// <summary>How long to wait for the player to finish in the browser.</summary>
        public const float DefaultTimeoutSeconds = 180;

        private const string CallbackPath = "/callback";

        private readonly string issuer;
        private readonly Action<string> openBrowser;
        private readonly float timeoutSeconds;

        // The running web server, or null. HttpListener is .NET's built-in one: we tell it
        // which addresses ("prefixes") to answer, and then ask it for incoming requests.
        private HttpListener listener;
        private bool cancelled;

        /// <param name="issuer">The accounts server.</param>
        /// <param name="openBrowser">Opens a URL in the browser. Null means Application.OpenURL; tests pass their own.</param>
        /// <param name="timeoutSeconds">How long to wait for the browser to come back.</param>
        public LoopbackAuthFlow(string issuer, Action<string> openBrowser = null,
            float timeoutSeconds = DefaultTimeoutSeconds)
        {
            this.issuer = issuer;
            this.openBrowser = openBrowser ?? Application.OpenURL;
            this.timeoutSeconds = timeoutSeconds;
        }

        public IEnumerator SignIn(Action<AuthFlowResult> done)
        {
            cancelled = false;
            if (!TryStartListener(out int port))
            {
                done(AuthFlowResult.Failure("Couldn't start signing in on this computer. Please try again."));
                yield break;
            }

            string verifier = Pkce.CreateVerifier();
            string state = Pkce.RandomString(16);
            string redirectUri = "http://127.0.0.1:" + port + CallbackPath;
            openBrowser(AuthConfig.AuthorizeUrl(issuer, AuthConfig.DesktopClientId, redirectUri,
                Pkce.CreateChallenge(verifier), state));

            // Time.realtimeSinceStartup keeps counting while the game is paused (Time.time
            // wouldn't, if anything set timeScale to 0).
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            AuthFlowResult result = null;
            try
            {
                while (result == null)
                {
                    // GetContextAsync waits for the next request on a background thread and
                    // returns a Task: a "result that will arrive later". We must NOT touch
                    // Unity (or the AccountManager) from that background thread: almost all of
                    // Unity's API only works on the main thread. So instead of reacting when the
                    // request arrives, this coroutine (which runs on the main thread) checks once
                    // a frame whether the Task has finished. That hop from "some other thread"
                    // back to "the main thread" is called marshalling.
                    Task<HttpListenerContext> request = listener.GetContextAsync();
                    IgnoreErrorsLater(request);

                    while (!request.IsCompleted && !cancelled && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                    }

                    if (cancelled)
                    {
                        result = AuthFlowResult.Failure("Sign-in was cancelled.");
                    }
                    else if (!request.IsCompleted)
                    {
                        result = AuthFlowResult.Failure("Sign-in timed out. Please try again.");
                    }
                    else if (request.IsFaulted || request.IsCanceled)
                    {
                        // The server stopped under us (for example, the computer went to sleep).
                        Debug.LogWarning($"Accounts: the sign-in listener failed ({request.Exception?.GetBaseException().Message}).");
                        result = AuthFlowResult.Failure("Sign-in failed. Please try again.");
                    }
                    else
                    {
                        // Null if this request wasn't the sign-in coming back; then keep waiting.
                        result = HandleRequest(request.Result, state, verifier, redirectUri);
                    }
                }
            }
            finally
            {
                // However we got here (success, error, timeout, cancel, or the coroutine being
                // stopped), stop the server so the port is free again.
                StopListener();
            }
            done(result);
        }

        /// <summary>The game was just launched; there's never a sign-in to pick up on Windows.</summary>
        public bool TryResume(out AuthFlowResult result)
        {
            result = null;
            return false;
        }

        /// <summary>
        /// Stops waiting for the browser: the running <see cref="SignIn"/> finishes with "Sign-in
        /// was cancelled." on its next frame. The server is stopped straight away, so the port
        /// is freed even if that next frame never comes (the game is closing).
        /// </summary>
        public void Cancel()
        {
            cancelled = true;
            StopListener();
        }

        /// <summary>
        /// Looks at one request to our server. Returns the sign-in's result if it's the
        /// callback we're waiting for, or null if it's anything else (we answer it and carry on
        /// waiting).
        /// </summary>
        private static AuthFlowResult HandleRequest(HttpListenerContext context, string expectedState,
            string verifier, string redirectUri)
        {
            // RawUrl is the path and query exactly as sent, e.g. "/callback?code=...&state=...".
            if (!AuthCallback.TryParse(context.Request.RawUrl, out AuthCallback callback))
            {
                Respond(context, 404, "Not found", "There's nothing here.");
                return null;
            }

            // Any web page the player has open could send their browser to this address, so a
            // callback that doesn't carry our state isn't from our sign-in. Turn it away, but
            // keep waiting: the real one may still be on its way.
            if (!string.Equals(callback.State, expectedState, StringComparison.Ordinal))
            {
                Respond(context, 400, "Sign-in failed",
                    "This sign-in didn't come from Match-3. Close this tab and try again from the game.");
                return null;
            }

            string error = callback.Validate(expectedState);
            if (error != null)
            {
                Respond(context, 200, "Sign-in didn't finish", error + " You can close this tab and go back to Match-3.");
                return AuthFlowResult.Failure(error);
            }

            Respond(context, 200, "Signed in", "You can close this tab and go back to Match-3.");
            return AuthFlowResult.Success(callback.Code, verifier, redirectUri, AuthConfig.DesktopClientId);
        }

        /// <summary>Sends a small HTML page back to the browser.</summary>
        private static void Respond(HttpListenerContext context, int status, string title, string text)
        {
            // Only our own fixed text goes into the page (never anything from the request), so
            // there's nothing to escape.
            string html =
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>" + title + " - Match-3</title>" +
                "<style>body{font-family:sans-serif;background:#1e1b2e;color:#eee;text-align:center;padding-top:15vh}</style>" +
                "</head><body><h1>" + title + "</h1><p>" + text + "</p></body></html>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            try
            {
                HttpListenerResponse response = context.Response;
                response.StatusCode = status;
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = body.Length;
                response.OutputStream.Write(body, 0, body.Length);
                response.Close();
            }
            catch (Exception e)
            {
                // The browser went away before we answered. The sign-in itself is unaffected.
                Debug.LogWarning($"Accounts: couldn't answer the browser ({e.Message}).");
            }
        }

        /// <summary>
        /// Starts the server on a free port. We ask Windows for any free port by opening a
        /// socket on port 0, note the port it picked, and close it again. Another program could
        /// grab that port in between, so try a few times.
        /// </summary>
        private bool TryStartListener(out int port)
        {
            StopListener();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                port = FindFreePort();
                var candidate = new HttpListener();
                // A prefix must end in '/'. It also answers "/callback" without the slash.
                candidate.Prefixes.Add("http://127.0.0.1:" + port + CallbackPath + "/");
                try
                {
                    candidate.Start();
                    listener = candidate;
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Accounts: couldn't listen on port {port} ({e.Message}).");
                    candidate.Close();
                }
            }
            port = 0;
            return false;
        }

        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private void StopListener()
        {
            if (listener != null)
            {
                // Close stops listening and frees the port. A request we were still waiting
                // for fails, which IgnoreErrorsLater takes care of.
                listener.Close();
                listener = null;
            }
        }

        /// <summary>
        /// When the server is stopped while we're waiting, the waiting Task fails. A failed Task
        /// that nobody ever looks at gets reported as an "unobserved exception", so look at its
        /// error (and do nothing with it) whenever that happens.
        /// </summary>
        private static void IgnoreErrorsLater(Task task)
        {
            task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
#endif
