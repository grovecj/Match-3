using System;
using System.Collections;
using Match3.Online;
using UnityEngine.Networking;

namespace Match3.Achievements
{
    /// <summary>
    /// The real <see cref="IAchievementsApi"/>. Achievements live on the same server as the
    /// leaderboard, so this sends its requests the same way, through
    /// <see cref="HttpLeaderboardApi.Send{T}"/> (bearer token, one retry on a dropped connection).
    /// </summary>
    public sealed class HttpAchievementsApi : IAchievementsApi
    {
        private readonly string baseUrl;

        public HttpAchievementsApi(string baseUrl)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
        }

        public IEnumerator GetCatalog(Action<ApiResult<Achievement[]>> done)
        {
            yield return HttpLeaderboardApi.Send(() => UnityWebRequest.Get(baseUrl + "/achievements"),
                null, AchievementsJson.ParseList, done);
        }

        public IEnumerator GetMine(string accessToken, Action<ApiResult<Achievement[]>> done)
        {
            yield return HttpLeaderboardApi.Send(() => UnityWebRequest.Get(baseUrl + "/me/achievements"),
                accessToken, AchievementsJson.ParseList, done);
        }
    }
}
