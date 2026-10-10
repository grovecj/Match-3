using System;
using System.Collections;
using Match3.Achievements;

namespace Match3.Online
{
    /// <summary>
    /// The global leaderboard server (grovecj/match-3-api), one method per request the game
    /// makes. <see cref="HttpLeaderboardApi"/> is the real one; tests use a fake, so they run
    /// without a network.
    ///
    /// Every method is a coroutine that calls <c>done</c> exactly once with the outcome, and
    /// never throws: being offline is a normal result here, not an error.
    /// </summary>
    public interface ILeaderboardApi
    {
        /// <summary>
        /// <c>POST /rounds</c>: tells the server a round is starting. The server notes the time
        /// and answers with a round token, which the score must be sent with. That's what stops
        /// someone posting a score without playing a round (see the API's README).
        /// </summary>
        IEnumerator StartRound(string accessToken, Action<ApiResult<string>> done);

        /// <summary>
        /// <c>POST /scores</c>: posts the round's score, with what happened in the round
        /// (<paramref name="stats"/>, or null to send none). Answers with the player's best and
        /// rank, and any achievements the round unlocked.
        /// </summary>
        IEnumerator SubmitScore(string accessToken, string roundToken, int score, RoundStats stats,
            Action<ApiResult<Standing>> done);

        /// <summary><c>GET /scores/top</c>: the best <paramref name="limit"/> players. Public: no token needed.</summary>
        IEnumerator GetTop(int limit, Action<ApiResult<GlobalEntry[]>> done);

        /// <summary><c>GET /me</c>: the signed-in player's tag, best score and rank.</summary>
        IEnumerator GetMe(string accessToken, Action<ApiResult<PlayerProfile>> done);
    }

    /// <summary>
    /// The outcome of one request: either <see cref="Value"/>, or what went wrong.
    /// </summary>
    public sealed class ApiResult<T>
    {
        public bool Succeeded { get; private set; }
        public T Value { get; private set; }

        /// <summary>For the log: the server's error code, "HTTP 503", or the connection error.</summary>
        public string Error { get; private set; }

        /// <summary>The server's error code (e.g. "round_too_short"), or null if it didn't send one.</summary>
        public string Code { get; private set; }

        /// <summary>
        /// True when the server answered (with an error); false when it couldn't be reached at
        /// all (offline, timed out, or something in the way answered instead).
        /// </summary>
        public bool Reached { get; private set; }

        public static ApiResult<T> Success(T value) => new ApiResult<T> { Succeeded = true, Value = value, Reached = true };

        public static ApiResult<T> Failure(string error, string code = null, bool reached = false) =>
            new ApiResult<T> { Error = error, Code = code, Reached = reached };
    }

    /// <summary>One line of the global leaderboard.</summary>
    [Serializable]
    public class GlobalEntry
    {
        // JsonUtility fills public fields whose names match the JSON keys exactly.
        public int rank;
        public string tag;
        public int score;
    }

    /// <summary>
    /// The answer to a posted score. Where the player stands: their best score, and its rank
    /// (1 = top; tied players share a rank). And what the round earned them.
    /// </summary>
    [Serializable]
    public class Standing
    {
        public int best;
        public int rank;

        /// <summary>
        /// The achievements this round unlocked (empty if none). Null means we don't know: the
        /// server's reply was lost and the standing had to be asked for again.
        /// </summary>
        public Achievement[] unlocked;
    }

    /// <summary>
    /// The signed-in player as the server knows them. <see cref="rank"/> is 0 if they haven't
    /// posted a score yet.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public string tag;
        public int best;
        public int rank;

        public bool HasScore => rank > 0;
    }
}
