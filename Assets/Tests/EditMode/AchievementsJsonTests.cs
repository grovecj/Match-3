using System.Collections.Generic;
using Match3.Achievements;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for reading the server's achievements reply
    /// (<see cref="AchievementsJson"/>), for what an <see cref="Achievement"/> says about
    /// itself, and for spotting which ones are newly unlocked
    /// (<see cref="AchievementsClient.NewlyUnlocked"/>).
    /// </summary>
    public class AchievementsJsonTests
    {
        // A reply in the exact shape the server sends.
        private const string Reply =
            "{\"game\":\"match3\",\"achievements\":[" +
            "{\"key\":\"first-round\",\"name\":\"Warm-Up\",\"description\":\"Finish a round\",\"icon\":\"rounds\"," +
            "\"goal\":1,\"progress\":1,\"unlockedAt\":\"2026-10-10T04:15:00.123456Z\"}," +
            "{\"key\":\"rounds-50\",\"name\":\"Veteran\",\"description\":\"Play 50 rounds\",\"icon\":\"rounds\"," +
            "\"goal\":50,\"progress\":23,\"unlockedAt\":null}]}";

        [Test]
        public void ParseList_ReadsEveryFieldInOrder()
        {
            Achievement[] list = AchievementsJson.ParseList(Reply);

            Assert.That(list, Has.Length.EqualTo(2));
            Assert.That(list[0].key, Is.EqualTo("first-round"));
            Assert.That(list[0].name, Is.EqualTo("Warm-Up"));
            Assert.That(list[0].description, Is.EqualTo("Finish a round"));
            Assert.That(list[0].icon, Is.EqualTo("rounds"));
            Assert.That(list[0].goal, Is.EqualTo(1));
            Assert.That(list[0].IsUnlocked, Is.True);
            Assert.That(list[1].key, Is.EqualTo("rounds-50"));
            Assert.That(list[1].progress, Is.EqualTo(23));
            Assert.That(list[1].IsUnlocked, Is.False, "\"unlockedAt\":null means locked.");
        }

        [Test]
        public void ParseList_AnEmptyListIsStillAList()
        {
            Assert.That(AchievementsJson.ParseList("{\"game\":\"match3\",\"achievements\":[]}"), Is.Empty);
        }

        [Test]
        public void ParseList_NotAnAchievementsReply_IsNull()
        {
            Assert.That(AchievementsJson.ParseList(null), Is.Null);
            Assert.That(AchievementsJson.ParseList(""), Is.Null);
            Assert.That(AchievementsJson.ParseList("{}"), Is.Null);
            Assert.That(AchievementsJson.ParseList("<!DOCTYPE html><html>"), Is.Null);
            Assert.That(AchievementsJson.ParseList("{\"achievements\":[{\"name\":\"No key\"}]}"), Is.Null);
        }

        [Test]
        public void Fraction_IsProgressOverGoal()
        {
            Assert.That(new Achievement { goal = 50, progress = 0 }.Fraction, Is.EqualTo(0f));
            Assert.That(new Achievement { goal = 50, progress = 25 }.Fraction, Is.EqualTo(0.5f));
            Assert.That(new Achievement { goal = 50, progress = 80 }.Fraction, Is.EqualTo(1f), "Never past full.");
            Assert.That(new Achievement { goal = 0, progress = 3 }.Fraction, Is.EqualTo(0f), "No goal, no dividing by zero.");
        }

        [Test]
        public void Fraction_IsFullOnceUnlocked()
        {
            var achievement = new Achievement { goal = 50, progress = 10, unlockedAt = "2026-10-10T04:15:00Z" };

            Assert.That(achievement.Fraction, Is.EqualTo(1f));
        }

        [Test]
        public void NewlyUnlocked_FindsWhatWasLockedBefore()
        {
            Achievement[] before = { Locked("a"), Unlocked("b"), Locked("c") };
            Achievement[] after = { Unlocked("a"), Unlocked("b"), Locked("c"), Unlocked("d") };

            List<Achievement> fresh = AchievementsClient.NewlyUnlocked(before, after);

            // "d" wasn't in the old list at all (a new achievement on the server), which counts.
            Assert.That(fresh.ConvertAll(a => a.key), Is.EqualTo(new[] { "a", "d" }));
        }

        [Test]
        public void NewlyUnlocked_WithNothingToCompare_IsEmpty()
        {
            Achievement[] list = { Unlocked("a") };

            Assert.That(AchievementsClient.NewlyUnlocked(null, list), Is.Empty);
            Assert.That(AchievementsClient.NewlyUnlocked(list, null), Is.Empty);
        }

        private static Achievement Locked(string key) => new Achievement { key = key, goal = 1 };

        private static Achievement Unlocked(string key) =>
            new Achievement { key = key, goal = 1, progress = 1, unlockedAt = "2026-10-10T04:15:00Z" };
    }
}
