using System.Collections.Generic;

namespace Match3
{
    /// <summary>
    /// Somewhere the leaderboard is kept. The <see cref="LeaderboardMenu"/> only talks to this
    /// interface, never to a particular class, so the local JSON file
    /// (<see cref="JsonFileLeaderboardStore"/>) can later be swapped for an online leaderboard
    /// without touching the menu: the new store just has to provide these two methods.
    /// </summary>
    public interface ILeaderboardStore
    {
        /// <summary>The saved entries, best first (at most <see cref="Leaderboard.Capacity"/>).</summary>
        List<LeaderboardEntry> Load();

        /// <summary>
        /// Adds <paramref name="entry"/> if it makes the top <see cref="Leaderboard.Capacity"/>
        /// and saves the result. Returns the entry's 0-based rank, or -1 if it didn't place.
        /// </summary>
        int Submit(LeaderboardEntry entry);
    }
}
