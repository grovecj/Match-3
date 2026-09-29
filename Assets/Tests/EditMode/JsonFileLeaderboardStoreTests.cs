using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="JsonFileLeaderboardStore"/>. Each test uses its own file
    /// in a fresh temp folder, so they never touch the real leaderboard in
    /// Application.persistentDataPath.
    /// </summary>
    public class JsonFileLeaderboardStoreTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private string folder;
        private string path;

        [SetUp]
        public void SetUp()
        {
            // A new GUID each time gives every test a folder no other test (or run) uses.
            folder = Path.Combine(Path.GetTempPath(), "Match3LeaderboardTests", Guid.NewGuid().ToString("N"));
            path = Path.Combine(folder, "leaderboard.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Test]
        public void Load_NoFile_ReturnsEmptyList()
        {
            var store = new JsonFileLeaderboardStore(path);

            Assert.That(store.Load(), Is.Empty);
        }

        [Test]
        public void Submit_ThenLoadInANewStore_ReturnsTheEntries()
        {
            // Two separate store objects reading the same file is what happens when the game is
            // closed and started again, so this is the "scores persist across restarts" check.
            var first = new JsonFileLeaderboardStore(path);
            first.Submit(new LeaderboardEntry("LOW", 1000, Start));
            first.Submit(new LeaderboardEntry("TOP", 9000, Start.AddMinutes(1)));

            var second = new JsonFileLeaderboardStore(path);
            var loaded = second.Load();

            Assert.That(loaded.Count, Is.EqualTo(2));
            Assert.That(loaded[0].Initials, Is.EqualTo("TOP"));
            Assert.That(loaded[0].Score, Is.EqualTo(9000));
            Assert.That(loaded[0].TimestampUtc, Is.EqualTo(Start.AddMinutes(1)));
            Assert.That(loaded[1].Initials, Is.EqualTo("LOW"));
        }

        [Test]
        public void Submit_ReturnsRank()
        {
            var store = new JsonFileLeaderboardStore(path);
            store.Submit(new LeaderboardEntry("TOP", 9000, Start));

            int rank = store.Submit(new LeaderboardEntry("LOW", 1000, Start.AddMinutes(1)));

            Assert.That(rank, Is.EqualTo(1));
        }

        [Test]
        public void Submit_KeepsOnlyTheTopTen()
        {
            var store = new JsonFileLeaderboardStore(path);
            for (int i = 1; i <= Leaderboard.Capacity + 2; i++)
            {
                store.Submit(new LeaderboardEntry("AAA", i * 1000, Start.AddMinutes(i)));
            }

            var loaded = store.Load();

            Assert.That(loaded.Count, Is.EqualTo(Leaderboard.Capacity));
            Assert.That(loaded[0].Score, Is.EqualTo(12000));
            Assert.That(loaded[loaded.Count - 1].Score, Is.EqualTo(3000));
        }

        [Test]
        public void Submit_NonQualifyingScore_SavesNothing()
        {
            var store = new JsonFileLeaderboardStore(path);
            for (int i = 1; i <= Leaderboard.Capacity; i++)
            {
                store.Submit(new LeaderboardEntry("AAA", i * 1000, Start.AddMinutes(i)));
            }
            string before = File.ReadAllText(path);

            int rank = store.Submit(new LeaderboardEntry("LOW", 500, Start.AddMinutes(100)));

            Assert.That(rank, Is.EqualTo(-1));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        }

        [Test]
        public void Load_SkipsEntriesWithImpossibleTimestamps()
        {
            // Valid JSON, but one tick count is far beyond DateTime.MaxValue (as if hand-edited).
            // Two equal scores force the sort to compare timestamps.
            Directory.CreateDirectory(folder);
            File.WriteAllText(path,
                "{\"entries\":[" +
                "{\"initials\":\"BAD\",\"score\":5000,\"utcTicks\":9223372036854775807}," +
                "{\"initials\":\"OK\",\"score\":5000,\"utcTicks\":" + Start.Ticks + "}]}");
            var store = new JsonFileLeaderboardStore(path);

            var loaded = store.Load();

            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded[0].Initials, Is.EqualTo("OK"));
        }

        [Test]
        public void Load_CorruptFile_ReturnsEmptyList_AndWarns()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "this is not json {");
            var store = new JsonFileLeaderboardStore(path);

            // LogAssert.Expect says "this warning is supposed to be logged". Without it, the
            // test would still pass, but expecting it also checks that we do warn.
            LogAssert.Expect(LogType.Warning, new Regex("couldn't read"));
            Assert.That(store.Load(), Is.Empty);
        }
    }
}
