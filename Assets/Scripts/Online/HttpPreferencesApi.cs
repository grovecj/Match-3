using System;
using System.Collections;
using UnityEngine.Networking;

namespace Match3.Online
{
    /// <summary>
    /// The real <see cref="IPreferencesApi"/>: <c>GET</c> and <c>PUT /me/preferences</c> on the
    /// same server as the leaderboard, sent the same way (see
    /// <see cref="HttpLeaderboardApi.Send{T}"/>).
    /// </summary>
    public sealed class HttpPreferencesApi : IPreferencesApi
    {
        private readonly string url;

        public HttpPreferencesApi(string baseUrl)
        {
            url = baseUrl.TrimEnd('/') + "/me/preferences";
        }

        public IEnumerator Load(string accessToken, Action<ApiResult<SavedPreferences>> done)
        {
            yield return HttpLeaderboardApi.Send(() => UnityWebRequest.Get(url),
                accessToken, PreferencesJson.Parse, done);
        }

        public IEnumerator Save(string accessToken, SavedPreferences change, Action<ApiResult<SavedPreferences>> done)
        {
            string body = PreferencesJson.ToJson(change);
            yield return HttpLeaderboardApi.Send(() =>
            {
                // GET reads, POST adds something new, PUT replaces what's at this address.
                // Unlike UnityWebRequest.Post, Put has no content-type argument, so say it's
                // JSON with a header (otherwise the server refuses to read it).
                UnityWebRequest request = UnityWebRequest.Put(url, body);
                request.SetRequestHeader("Content-Type", "application/json");
                return request;
            }, accessToken, PreferencesJson.Parse, done);
        }
    }
}
