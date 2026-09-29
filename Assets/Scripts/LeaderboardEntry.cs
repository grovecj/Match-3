using System;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// One line on the leaderboard: who (initials), how many points, and when.
    ///
    /// [Serializable] lets Unity's serializer (and so <see cref="JsonUtility"/>) save and load
    /// this class. JsonUtility only handles the field types Unity can serialize, and
    /// <see cref="DateTime"/> isn't one of them, so the time is stored as a plain number of
    /// ticks (100-nanosecond steps since year 1) and turned back into a DateTime on demand.
    /// </summary>
    [Serializable]
    public class LeaderboardEntry
    {
        [SerializeField] private string initials;
        [SerializeField] private int score;
        [SerializeField] private long utcTicks;

        // Unity's serializer needs a constructor with no parameters to create entries when it
        // reads them back from JSON.
        public LeaderboardEntry()
        {
        }

        public LeaderboardEntry(string initials, int score, DateTime timestampUtc)
        {
            this.initials = initials;
            this.score = score;
            utcTicks = timestampUtc.ToUniversalTime().Ticks;
        }

        public string Initials => initials;
        public int Score => score;

        /// <summary>When the score was set, in UTC. Used to break ties: earlier wins.</summary>
        public DateTime TimestampUtc => new DateTime(utcTicks, DateTimeKind.Utc);
    }
}
