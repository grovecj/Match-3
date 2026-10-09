using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Match3.Online
{
    /// <summary>
    /// The real <see cref="ILeaderboardApi"/>: HTTP requests to the leaderboard server with
    /// UnityWebRequest (see <see cref="Accounts.HttpTokenClient"/> for how that works).
    ///
    /// Requests that need a signed-in player carry the access token in an
    /// <c>Authorization: Bearer ...</c> header. The server checks the token's signature
    /// itself, against the accounts server's public keys, so it knows who's posting without
    /// asking the accounts server on every request.
    /// </summary>
    public sealed class HttpLeaderboardApi : ILeaderboardApi
    {
        /// <summary>
        /// Where the server lives: next to the web build, on the same site, so the browser needs
        /// no CORS permission to call it.
        /// </summary>
        public const string DefaultBaseUrl = "https://games.cartergrove.me/match3/api";

        // A request that hasn't finished after this many seconds is given up on.
        private const int TimeoutSeconds = 15;

        private readonly string baseUrl;

        public HttpLeaderboardApi(string baseUrl)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
        }

        public IEnumerator StartRound(string accessToken, Action<ApiResult<string>> done)
        {
            // The server needs no body here: who's playing comes from the token.
            using (UnityWebRequest request = UnityWebRequest.Post(baseUrl + "/rounds", "{}", "application/json"))
            {
                yield return Send(request, accessToken);
                done(Read(request, LeaderboardJson.ParseRoundToken));
            }
        }

        public IEnumerator SubmitScore(string accessToken, string roundToken, int score, Action<ApiResult<Standing>> done)
        {
            // JsonUtility.ToJson writes an object's public fields as JSON, the reverse of FromJson.
            string body = JsonUtility.ToJson(new ScoreSubmission { roundToken = roundToken, score = score });
            using (UnityWebRequest request = UnityWebRequest.Post(baseUrl + "/scores", body, "application/json"))
            {
                yield return Send(request, accessToken);
                done(Read(request, LeaderboardJson.ParseStanding));
            }
        }

        public IEnumerator GetTop(int limit, Action<ApiResult<GlobalEntry[]>> done)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(baseUrl + "/scores/top?limit=" + limit))
            {
                yield return Send(request, null);
                done(Read(request, LeaderboardJson.ParseTop));
            }
        }

        public IEnumerator GetMe(string accessToken, Action<ApiResult<PlayerProfile>> done)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(baseUrl + "/me"))
            {
                yield return Send(request, accessToken);
                done(Read(request, LeaderboardJson.ParseProfile));
            }
        }

        private static IEnumerator Send(UnityWebRequest request, string accessToken)
        {
            request.timeout = TimeoutSeconds;
            if (accessToken != null)
            {
                request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            }
            yield return request.SendWebRequest();
        }

        /// <summary>
        /// Turns a finished request into a result, using <paramref name="parse"/> (which returns
        /// null for a reply it doesn't understand) on a successful one.
        /// </summary>
        private static ApiResult<T> Read<T>(UnityWebRequest request, Func<string, T> parse) where T : class
        {
            string body = request.downloadHandler != null ? request.downloadHandler.text : null;

            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    T value = parse(body);
                    return value != null
                        ? ApiResult<T>.Success(value)
                        : ApiResult<T>.Failure("unexpected reply from the leaderboard server");

                case UnityWebRequest.Result.ProtocolError:
                    // An HTTP error. If it came from our server, the body has an error code
                    // ("round_too_short", ...). If not (say an HTML page from something in front
                    // of the server), there's no code, and we treat it like not reaching it.
                    string code = LeaderboardJson.ParseErrorCode(body);
                    return ApiResult<T>.Failure(code ?? "HTTP " + request.responseCode, code, reached: code != null);

                default:
                    // ConnectionError (offline, DNS, CORS, timeout) or DataProcessingError.
                    return ApiResult<T>.Failure(request.error);
            }
        }

        [Serializable]
        private class ScoreSubmission
        {
            public string roundToken;
            public int score;
        }
    }
}
