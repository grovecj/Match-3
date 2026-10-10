using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Match3.Online
{
    /// <summary>
    /// Turns the leaderboard server's JSON replies into C# objects, with Unity's
    /// <see cref="JsonUtility"/>. Each method returns null if the text isn't what we expect
    /// (including when it isn't JSON at all, like an HTML error page from a proxy).
    ///
    /// JsonUtility is small and built in, but it has two limits that shape this class:
    ///   - The top level must be an object. <c>GET /scores/top</c> answers with a bare array,
    ///     so <see cref="ParseTop"/> wraps it in one first.
    ///   - It has no "nullable int", so a JSON <c>null</c> for a number is turned into 0 here
    ///     before parsing, rather than relying on what JsonUtility happens to do with it.
    /// </summary>
    public static class LeaderboardJson
    {
        public static string ParseRoundToken(string json)
        {
            RoundReply reply = Parse<RoundReply>(json);
            return reply != null && !string.IsNullOrEmpty(reply.roundToken) ? reply.roundToken : null;
        }

        public static Standing ParseStanding(string json)
        {
            Standing standing = Parse<Standing>(json);
            return standing != null && standing.rank > 0 ? standing : null;
        }

        public static GlobalEntry[] ParseTop(string json)
        {
            if (json == null || !json.TrimStart().StartsWith("["))
            {
                return null;
            }
            TopReply reply = Parse<TopReply>("{\"entries\":" + json + "}");
            return reply?.entries;
        }

        public static PlayerProfile ParseProfile(string json)
        {
            if (json == null)
            {
                return null;
            }
            // A player with no score yet has "best":null and "rank":null; read those as 0.
            json = Regex.Replace(json, "\"(best|rank)\"\\s*:\\s*null", "\"$1\":0");
            PlayerProfile profile = Parse<PlayerProfile>(json);
            return profile != null && !string.IsNullOrEmpty(profile.tag) ? profile : null;
        }

        /// <summary>
        /// The <c>code</c> from an error reply (e.g. <c>{"status":422,"code":"round_too_short",...}</c>),
        /// or null.
        /// </summary>
        public static string ParseErrorCode(string json)
        {
            ErrorReply reply = Parse<ErrorReply>(json);
            return reply != null && !string.IsNullOrEmpty(reply.code) ? reply.code : null;
        }

        /// <summary>
        /// <paramref name="json"/> as a <typeparamref name="T"/>, or null if it isn't a JSON object.
        /// (Internal: <see cref="PreferencesJson"/> uses it too.)
        /// </summary>
        internal static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (ArgumentException)
            {
                // Malformed JSON.
                return null;
            }
        }

        // The shapes of the replies that aren't one of the public classes in ILeaderboardApi.cs.
        [Serializable]
        private class RoundReply
        {
            public string roundToken;
        }

        [Serializable]
        private class TopReply
        {
            public GlobalEntry[] entries;
        }

        [Serializable]
        private class ErrorReply
        {
            public string code;
        }
    }
}
