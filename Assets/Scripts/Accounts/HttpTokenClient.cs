using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Match3.Accounts
{
    /// <summary>
    /// The real <see cref="ITokenClient"/>: HTTP POSTs to the accounts server with
    /// UnityWebRequest.
    ///
    /// UnityWebRequest is Unity's HTTP client. It works on every platform, including the
    /// browser (where it becomes a fetch/XMLHttpRequest, so the server has to allow our page's
    /// origin with CORS). Sending a request doesn't block: <c>yield return
    /// request.SendWebRequest()</c> pauses the coroutine until the response arrives, and the
    /// game keeps running meanwhile.
    /// </summary>
    public sealed class HttpTokenClient : ITokenClient
    {
        // A request that hasn't finished after this many seconds is given up on.
        private const int TimeoutSeconds = 15;

        private readonly string issuer;
        private readonly string clientId;

        public HttpTokenClient(string issuer, string clientId)
        {
            this.issuer = issuer;
            this.clientId = clientId;
        }

        public IEnumerator ExchangeCode(AuthFlowResult code, Action<TokenResult> done)
        {
            // The "authorization_code" grant: the code, plus the PKCE verifier that proves we're
            // the ones who started this sign-in. The redirect URI must repeat the one we used.
            var form = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code.Code },
                { "redirect_uri", code.RedirectUri },
                { "client_id", code.ClientId },
                { "code_verifier", code.CodeVerifier },
            };
            yield return PostForTokens(form, done);
        }

        public IEnumerator Refresh(string refreshToken, Action<TokenResult> done)
        {
            var form = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "client_id", clientId },
            };
            yield return PostForTokens(form, done);
        }

        public IEnumerator Revoke(string refreshToken)
        {
            var form = new Dictionary<string, string>
            {
                { "token", refreshToken },
                { "token_type_hint", "refresh_token" },
                { "client_id", clientId },
            };
            // "using" disposes the request when we're done, freeing its native memory.
            using (UnityWebRequest request = UnityWebRequest.Post(AuthConfig.RevokeEndpoint(issuer), form))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    // Not fatal: we've already forgotten the token on our side.
                    Debug.LogWarning($"Accounts: couldn't revoke the refresh token ({request.error}).");
                }
            }
        }

        private IEnumerator PostForTokens(Dictionary<string, string> form, Action<TokenResult> done)
        {
            // Post with a Dictionary sends the fields as application/x-www-form-urlencoded,
            // the format OAuth token endpoints expect.
            using (UnityWebRequest request = UnityWebRequest.Post(AuthConfig.TokenEndpoint(issuer), form))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();
                done(ReadResponse(request));
            }
        }

        private static TokenResult ReadResponse(UnityWebRequest request)
        {
            string body = request.downloadHandler != null ? request.downloadHandler.text : null;

            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    TokenResponse tokens = ParseJson(body);
                    if (tokens == null || string.IsNullOrEmpty(tokens.access_token))
                    {
                        return TokenResult.Failure("the token response had no access token", rejected: false);
                    }
                    return TokenResult.Success(tokens.access_token, tokens.refresh_token, tokens.expires_in);

                case UnityWebRequest.Result.ProtocolError:
                    // The server answered with an HTTP error. A 400/401 with an OAuth "error"
                    // (like invalid_grant) is a firm no; a 5xx means the server is having
                    // trouble, which is worth retrying later.
                    TokenResponse error = ParseJson(body);
                    bool rejected = request.responseCode >= 400 && request.responseCode < 500 &&
                        error != null && !string.IsNullOrEmpty(error.error);
                    string reason = error != null && !string.IsNullOrEmpty(error.error)
                        ? error.error
                        : "HTTP " + request.responseCode;
                    return TokenResult.Failure(reason, rejected);

                default:
                    // ConnectionError (offline, DNS, CORS, timeout) or DataProcessingError.
                    return TokenResult.Failure(request.error, rejected: false);
            }
        }

        private static TokenResponse ParseJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<TokenResponse>(json);
            }
            catch (ArgumentException)
            {
                // Not JSON (e.g. an HTML error page from a proxy).
                return null;
            }
        }

        /// <summary>
        /// The token endpoint's JSON reply, success or error. JsonUtility fills fields whose
        /// names match the JSON keys exactly, hence the snake_case names.
        /// </summary>
        [Serializable]
        private class TokenResponse
        {
            public string access_token;
            public string refresh_token;
            public int expires_in;
            public string error;
            public string error_description;
        }
    }
}
