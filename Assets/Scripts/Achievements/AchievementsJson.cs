using System;
using Match3.Online;

namespace Match3.Achievements
{
    /// <summary>
    /// Turns the server's achievements reply into C# objects, like
    /// <see cref="LeaderboardJson"/> does for the leaderboard's. The reply looks like this:
    /// <code>
    /// { "game": "match3",
    ///   "achievements": [ { "key": "rounds-50", "name": "Veteran", "description": "Play 50 rounds",
    ///                       "icon": "rounds", "goal": 50, "progress": 23, "unlockedAt": null } ] }
    /// </code>
    /// </summary>
    public static class AchievementsJson
    {
        /// <summary>
        /// The achievements in <paramref name="json"/>, in the server's order, or null if the
        /// text isn't an achievements reply.
        /// </summary>
        public static Achievement[] ParseList(string json)
        {
            ListReply reply = LeaderboardJson.Parse<ListReply>(json);
            if (reply == null || reply.achievements == null)
            {
                return null;
            }
            // One without a key is no use to us (we couldn't tell it apart from the others), so
            // treat the whole reply as not understood rather than show half of it.
            foreach (Achievement achievement in reply.achievements)
            {
                if (achievement == null || string.IsNullOrEmpty(achievement.key))
                {
                    return null;
                }
            }
            return reply.achievements;
        }

        [Serializable]
        private class ListReply
        {
            public string game;
            public Achievement[] achievements;
        }
    }
}
