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
            yield return Send(() => UnityWebRequest.Post(baseUrl + "/rounds", "{}", "application/json"),
                accessToken, LeaderboardJson.ParseRoundToken, done);
        }

        public IEnumerator SubmitScore(string accessToken, string roundToken, int score, RoundStats stats,
            Action<ApiResult<Standing>> done)
        {
            // JsonUtility.ToJson writes an object's public fields as JSON, the reverse of FromJson.
            // It can't leave a field out, so "with stats" and "without" are two classes.
            string body = stats != null
                ? JsonUtility.ToJson(new ScoreWithStats { roundToken = roundToken, score = score, stats = stats })
                : JsonUtility.ToJson(new ScoreSubmission { roundToken = roundToken, score = score });
            ApiResult<Standing> result = null;
            bool retried = false;
            yield return Send(() => UnityWebRequest.Post(baseUrl + "/scores", body, "application/json"),
                accessToken, LeaderboardJson.ParseStanding, r => result = r, () => retried = true);

            // A retry that's told the round already has a score means the first try did reach the
            // server, and only its reply was lost. So it was posted; ask for the standing instead.
            // (That leaves Standing.unlocked null: which achievements it earned went with the reply.)
            if (retried && result.Code == "round_already_scored")
            {
                ApiResult<PlayerProfile> me = null;
                yield return GetMe(accessToken, r => me = r);
                if (me.Succeeded && me.Value.HasScore)
                {
                    result = ApiResult<Standing>.Success(new Standing { best = me.Value.best, rank = me.Value.rank });
                }
            }
            done(result);
        }

        public IEnumerator GetTop(int limit, Action<ApiResult<GlobalEntry[]>> done)
        {
            yield return Send(() => UnityWebRequest.Get(baseUrl + "/scores/top?limit=" + limit),
                null, LeaderboardJson.ParseTop, done);
        }

        public IEnumerator GetMe(string accessToken, Action<ApiResult<PlayerProfile>> done)
        {
            yield return Send(() => UnityWebRequest.Get(baseUrl + "/me"),
                accessToken, LeaderboardJson.ParseProfile, done);
        }

        /// <summary>
        /// Sends a request made by <paramref name="create"/> and reads its reply with
        /// <paramref name="parse"/>, trying once more if the connection itself failed.
        ///
        /// Why retry? UnityWebRequest keeps connections open and reuses them, which saves time.
        /// But Cloudflare (in front of the server) closes a connection that has been idle for
        /// about a minute, and a round lasts a minute: so the score is sent on a connection that
        /// was closed while the round was played, and fails without reaching the server. The
        /// failed connection is then thrown away, so the second try opens a fresh one. (Browsers
        /// do this by themselves, so the web build never sees the problem.)
        ///
        /// Only connection failures are retried, never an answer from the server: a 4xx or 5xx
        /// would just come back the same. A request can't be sent twice, so <paramref name="create"/>
        /// makes a new one for each try. <paramref name="onRetry"/> is called before the second.
        /// </summary>
        // Internal, so HttpPreferencesApi (the same server) sends its requests the same way.
        internal static IEnumerator Send<T>(Func<UnityWebRequest> create, string accessToken,
            Func<string, T> parse, Action<ApiResult<T>> done, Action onRetry = null) where T : class
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                // "using" disposes each request when we're done with it, freeing its native memory.
                using (UnityWebRequest request = create())
                {
                    request.timeout = TimeoutSeconds;
                    if (accessToken != null)
                    {
                        request.SetRequestHeader("Authorization", "Bearer " + accessToken);
                    }
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.ConnectionError && attempt == 1)
                    {
                        Debug.Log($"Match-3 API: {request.method} {request.url} failed ({request.error}); retrying once.");
                        onRetry?.Invoke();
                        continue;
                    }
                    done(Read(request, parse));
                    yield break;
                }
            }
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
                        : ApiResult<T>.Failure("unexpected reply from the server");

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

        [Serializable]
        private class ScoreWithStats
        {
            public string roundToken;
            public int score;
            public RoundStats stats;
        }
    }
}
