// LoopbackAuthFlow isn't in browser builds (see its file), so neither are its tests.
#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Match3.Accounts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Tests
{
    /// <summary>
    /// Play Mode tests for <see cref="LoopbackAuthFlow"/>, the Windows sign-in. These start the
    /// flow's real little web server on 127.0.0.1 and play the browser's part with real HTTP
    /// requests to it; only opening the browser is faked (it records the sign-in URL instead).
    /// Play Mode, because the flow is a coroutine that checks for requests once a frame.
    /// </summary>
    public class LoopbackAuthFlowPlayModeTests
    {
        private const string Issuer = "https://auth.example.com";
        // Real seconds to wait for anything before failing the test.
        private const float MaxWaitSeconds = 10;

        private GameObject runnerObject;
        private CoroutineRunner runner;
        private LoopbackAuthFlow flow;
        private string openedUrl;
        private AuthFlowResult result;

        [SetUp]
        public void SetUp()
        {
            flow = null;
            runnerObject = new GameObject("LoopbackAuthFlow (test)");
            runner = runnerObject.AddComponent<CoroutineRunner>();
            openedUrl = null;
            result = null;
        }

        [TearDown]
        public void TearDown()
        {
            flow?.Cancel();
            UnityEngine.Object.Destroy(runnerObject);
        }

        // Starts sign-in with a pretend browser that just remembers the URL it was sent to.
        private void StartSignIn(float timeoutSeconds = LoopbackAuthFlow.DefaultTimeoutSeconds)
        {
            flow = new LoopbackAuthFlow(Issuer, url => openedUrl = url, timeoutSeconds);
            runner.StartCoroutine(flow.SignIn(r => result = r));
        }

        // The sign-in URL's query: client_id, redirect_uri, state, code_challenge, ...
        private Dictionary<string, string> SignInQuery => AuthCallback.ParseQuery(openedUrl);
        private string RedirectUri => SignInQuery["redirect_uri"];
        private string State => SignInQuery["state"];
        private int Port => new Uri(RedirectUri).Port;

        [UnityTest]
        public IEnumerator SignIn_OpensTheBrowserAtTheSignInPage()
        {
            StartSignIn();

            Assert.That(openedUrl, Does.StartWith(Issuer + "/oauth2/authorize?"));
            Assert.That(SignInQuery["client_id"], Is.EqualTo(AuthConfig.DesktopClientId));
            Assert.That(RedirectUri, Does.Match(@"^http://127\.0\.0\.1:\d+/callback$"));
            Assert.That(SignInQuery["code_challenge_method"], Is.EqualTo("S256"));
            Assert.That(State, Is.Not.Empty);
            Assert.That(result, Is.Null, "Should wait for the browser.");
            yield break;
        }

        [UnityTest]
        public IEnumerator Callback_WithTheRightState_SucceedsAndFreesThePort()
        {
            StartSignIn();
            string challenge = SignInQuery["code_challenge"];

            Task<Response> browser = Get(RedirectUri + "?code=the-code&state=" + Uri.EscapeDataString(State));
            yield return WaitFor(() => browser.IsCompleted && result != null);

            Assert.That(browser.Result.Status, Is.EqualTo(200));
            Assert.That(browser.Result.Body, Does.Contain("close this tab"));
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(result.Code, Is.EqualTo("the-code"));
            Assert.That(result.RedirectUri, Is.EqualTo(RedirectUri), "The token request must repeat it exactly.");
            Assert.That(result.ClientId, Is.EqualTo(AuthConfig.DesktopClientId));
            Assert.That(Pkce.CreateChallenge(result.CodeVerifier), Is.EqualTo(challenge));
            AssertPortIsFree(Port);
        }

        [UnityTest]
        public IEnumerator Callback_WithAnotherState_IsTurnedAwayAndTheFlowKeepsWaiting()
        {
            StartSignIn();

            Task<Response> forged = Get(RedirectUri + "?code=attackers-code&state=forged");
            yield return WaitFor(() => forged.IsCompleted);
            Assert.That(forged.Result.Status, Is.EqualTo(400));
            yield return null;
            Assert.That(result, Is.Null);

            Task<Response> real = Get(RedirectUri + "?code=the-code&state=" + Uri.EscapeDataString(State));
            yield return WaitFor(() => real.IsCompleted && result != null);
            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(result.Code, Is.EqualTo("the-code"));
        }

        [UnityTest]
        public IEnumerator Request_ThatIsNotACallback_Gets404AndTheFlowKeepsWaiting()
        {
            StartSignIn();

            Task<Response> other = Get(RedirectUri);
            yield return WaitFor(() => other.IsCompleted);
            yield return null;

            Assert.That(other.Result.Status, Is.EqualTo(404));
            Assert.That(result, Is.Null);
        }

        [UnityTest]
        public IEnumerator Callback_PlayerSaidNo_FailsAsCancelled()
        {
            StartSignIn();

            Task<Response> browser = Get(RedirectUri + "?error=access_denied&state=" + Uri.EscapeDataString(State));
            yield return WaitFor(() => browser.IsCompleted && result != null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo("Sign-in was cancelled."));
            AssertPortIsFree(Port);
        }

        [UnityTest]
        public IEnumerator Cancel_FailsAsCancelledAndFreesThePort()
        {
            StartSignIn();
            int port = Port;

            flow.Cancel();
            AssertPortIsFree(port); // Straight away, not a frame later.
            yield return WaitFor(() => result != null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo("Sign-in was cancelled."));
        }

        [UnityTest]
        public IEnumerator NoCallback_TimesOutAndFreesThePort()
        {
            StartSignIn(timeoutSeconds: 0.5f);

            yield return WaitFor(() => result != null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Does.Contain("timed out"));
            AssertPortIsFree(Port);
        }

        [UnityTest]
        public IEnumerator SignIn_AfterACancel_WorksAgain()
        {
            StartSignIn();
            flow.Cancel();
            yield return WaitFor(() => result != null);

            result = null;
            runner.StartCoroutine(flow.SignIn(r => result = r));
            Task<Response> browser = Get(RedirectUri + "?code=the-code&state=" + Uri.EscapeDataString(State));
            yield return WaitFor(() => browser.IsCompleted && result != null);

            Assert.That(result.Succeeded, Is.True, result.Error);
        }

        // ---- Helpers --------------------------------------------------------------------------

        private static IEnumerator WaitFor(Func<bool> condition)
        {
            float giveUpAt = Time.realtimeSinceStartup + MaxWaitSeconds;
            while (!condition() && Time.realtimeSinceStartup < giveUpAt)
            {
                yield return null;
            }
            Assert.That(condition(), Is.True, "Timed out waiting.");
        }

        private static void AssertPortIsFree(int port)
        {
            // Nobody listening means connecting is refused straight away. (Checking by listening
            // on the port ourselves can fail on Linux for a minute after a connection closed,
            // even though nothing is using it.)
            using (var client = new TcpClient())
            {
                Assert.Throws<SocketException>(() => client.Connect(IPAddress.Loopback, port),
                    "The flow's server should have stopped listening.");
            }
        }

        private struct Response
        {
            public int Status;
            public string Body;
        }

        /// <summary>
        /// A GET request, like the browser following the redirect, on a background thread
        /// (Task.Run) so the game's frames, and the flow, keep running while it waits.
        /// </summary>
        private static Task<Response> Get(string url)
        {
            return Task.Run(() =>
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 5000;
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        return Read(response);
                    }
                }
                catch (WebException e) when (e.Response is HttpWebResponse error)
                {
                    // 4xx/5xx answers arrive as exceptions; we still want their status.
                    using (error)
                    {
                        return Read(error);
                    }
                }
            });
        }

        private static Response Read(HttpWebResponse response)
        {
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return new Response { Status = (int)response.StatusCode, Body = reader.ReadToEnd() };
            }
        }

        /// <summary>Something to run the flow's coroutine on (only MonoBehaviours can).</summary>
        private class CoroutineRunner : MonoBehaviour
        {
        }
    }
}
#endif
