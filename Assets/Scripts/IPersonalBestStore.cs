namespace Match3
{
    /// <summary>
    /// Somewhere the player's personal best is kept. <see cref="PersonalBest"/> only talks to
    /// this interface, the same way the <see cref="LeaderboardMenu"/> only talks to
    /// <see cref="ILeaderboardStore"/>: the game uses PlayerPrefs
    /// (<see cref="PlayerPrefsPersonalBestStore"/>), and the tests use a fake that keeps the
    /// number in memory.
    /// </summary>
    public interface IPersonalBestStore
    {
        /// <summary>
        /// Gets the saved best. Returns false (and sets <paramref name="best"/> to 0) if nothing
        /// has been saved yet.
        /// </summary>
        bool TryLoad(out int best);

        /// <summary>Saves <paramref name="best"/> as the new best, replacing any older one.</summary>
        void Save(int best);
    }
}
