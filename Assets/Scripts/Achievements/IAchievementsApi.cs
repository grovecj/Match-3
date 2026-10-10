using System;
using System.Collections;
using Match3.Online;

namespace Match3.Achievements
{
    /// <summary>
    /// The server's achievements requests. <see cref="HttpAchievementsApi"/> is the real one;
    /// tests use a fake. Like <see cref="ILeaderboardApi"/>, each method is a coroutine that
    /// calls <c>done</c> exactly once and never throws.
    ///
    /// Notice what's missing: there is no "unlock" request. The server unlocks achievements
    /// itself when it accepts a round's score, so there's nothing here to ask for one with.
    /// </summary>
    public interface IAchievementsApi
    {
        /// <summary><c>GET /achievements</c>: everything there is to earn. Public: no token needed.</summary>
        IEnumerator GetCatalog(Action<ApiResult<Achievement[]>> done);

        /// <summary><c>GET /me/achievements</c>: the same list with the signed-in player's progress.</summary>
        IEnumerator GetMine(string accessToken, Action<ApiResult<Achievement[]>> done);
    }
}
