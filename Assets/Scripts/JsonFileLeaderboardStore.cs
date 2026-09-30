using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Keeps the leaderboard in a JSON file on this computer. The game uses a file in
    /// <see cref="Application.persistentDataPath"/>: a per-user folder Unity picks for each
    /// platform that survives restarts and updates (on Windows,
    /// %USERPROFILE%\AppData\LocalLow\&lt;company&gt;\&lt;product&gt;; on the web, browser storage).
    ///
    /// The file is read on every <see cref="Load"/> rather than cached. It's ten short lines,
    /// so that's cheap, and it means the file is always the one source of truth.
    ///
    /// Until the file exists (a fresh install, or a new browser), <see cref="Load"/> returns the
    /// default entries given to the constructor, if any. The first qualifying score is then
    /// saved alongside them, so the defaults get pushed off the list as players beat them.
    /// </summary>
    public class JsonFileLeaderboardStore : ILeaderboardStore
    {
        /// <summary>
        /// JsonUtility can only turn an object into JSON, not a bare list, so the list is
        /// wrapped in this small class. The file ends up as { "entries": [ ... ] }.
        /// </summary>
        [Serializable]
        private class SaveData
        {
            public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
        }

        private readonly string path;
        private readonly List<LeaderboardEntry> defaults;

        /// <param name="path">The JSON file to read and write.</param>
        /// <param name="defaults">
        /// What the leaderboard holds before anything has been saved (e.g.
        /// <see cref="Leaderboard.DefaultEntries"/>). Leave it out to start empty.
        /// </param>
        public JsonFileLeaderboardStore(string path, IEnumerable<LeaderboardEntry> defaults = null)
        {
            this.path = path;
            // Copy the list so a caller changing theirs later can't change our defaults.
            this.defaults = defaults != null ? new List<LeaderboardEntry>(defaults) : new List<LeaderboardEntry>();
        }

        public string FilePath => path;

        public List<LeaderboardEntry> Load()
        {
            if (!File.Exists(path))
            {
                // Clean copies the list, so callers can change what they get without touching defaults.
                return Clean(defaults);
            }

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                // A damaged or hand-edited file shouldn't crash the game. Start from an empty
                // leaderboard instead (the file is overwritten on the next submit).
                Debug.LogWarning($"Leaderboard: couldn't read {path}, starting empty. {e.Message}");
                return new List<LeaderboardEntry>();
            }

            return Clean(data != null && data.entries != null ? data.entries : new List<LeaderboardEntry>());
        }

        /// <summary>
        /// Returns the usable entries of <paramref name="loaded"/>, sorted best first and cut to
        /// <see cref="Leaderboard.Capacity"/>. Don't trust the file's order or length: someone
        /// may have edited it by hand.
        /// </summary>
        private static List<LeaderboardEntry> Clean(List<LeaderboardEntry> loaded)
        {
            var entries = new List<LeaderboardEntry>();
            foreach (LeaderboardEntry entry in loaded)
            {
                // Skip entries that would break sorting (a timestamp DateTime can't hold).
                if (entry != null && entry.HasValidTimestamp)
                {
                    entries.Add(entry);
                }
            }

            Leaderboard.Sort(entries);
            if (entries.Count > Leaderboard.Capacity)
            {
                entries.RemoveRange(Leaderboard.Capacity, entries.Count - Leaderboard.Capacity);
            }
            return entries;
        }

        public int Submit(LeaderboardEntry entry)
        {
            List<LeaderboardEntry> entries = Load();
            int rank = Leaderboard.Insert(entries, entry);
            if (rank >= 0)
            {
                Save(entries);
            }
            return rank;
        }

        private void Save(List<LeaderboardEntry> entries)
        {
            // The folder normally exists already, but creating it is harmless if it does and
            // saves a crash if it doesn't (e.g. a test pointing at a fresh temp folder).
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var data = new SaveData { entries = entries };
            // prettyPrint: true adds line breaks and indentation, so the file is easy to read
            // if you open it to see what's been saved.
            File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));
        }
    }
}
