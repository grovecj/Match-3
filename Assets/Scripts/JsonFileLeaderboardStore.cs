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

        public JsonFileLeaderboardStore(string path)
        {
            this.path = path;
        }

        public string FilePath => path;

        public List<LeaderboardEntry> Load()
        {
            if (!File.Exists(path))
            {
                return new List<LeaderboardEntry>();
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

            var entries = new List<LeaderboardEntry>();
            if (data != null && data.entries != null)
            {
                foreach (LeaderboardEntry entry in data.entries)
                {
                    // Skip entries that would break sorting (a timestamp DateTime can't hold).
                    if (entry != null && entry.HasValidTimestamp)
                    {
                        entries.Add(entry);
                    }
                }
            }

            // Don't trust the file's order or length: someone may have edited it by hand.
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
