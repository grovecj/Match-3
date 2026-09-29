using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the <see cref="Leaderboard"/> rules: ordering, which scores qualify,
    /// inserting into a full list, and cleaning up initials.
    /// </summary>
    public class LeaderboardTests
    {
        // A fixed starting time, so tests can say "a minute later" without depending on the clock.
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static LeaderboardEntry Entry(string initials, int score, int minutesAfterStart = 0)
        {
            return new LeaderboardEntry(initials, score, Start.AddMinutes(minutesAfterStart));
        }

        /// <summary>A full list: scores 10,000 down to 1,000.</summary>
        private static List<LeaderboardEntry> FullList()
        {
            var entries = new List<LeaderboardEntry>();
            for (int i = 0; i < Leaderboard.Capacity; i++)
            {
                entries.Add(Entry("AAA", (Leaderboard.Capacity - i) * 1000, i));
            }
            return entries;
        }

        [Test]
        public void Sort_PutsHigherScoresFirst()
        {
            var entries = new List<LeaderboardEntry> { Entry("LOW", 1000), Entry("TOP", 9000), Entry("MID", 5000) };

            Leaderboard.Sort(entries);

            Assert.That(entries.ConvertAll(e => e.Initials), Is.EqualTo(new[] { "TOP", "MID", "LOW" }));
        }

        [Test]
        public void Sort_BreaksTiesByEarlierTime()
        {
            var entries = new List<LeaderboardEntry> { Entry("NEW", 5000, 10), Entry("OLD", 5000, 0) };

            Leaderboard.Sort(entries);

            Assert.That(entries[0].Initials, Is.EqualTo("OLD"));
        }

        [Test]
        public void Qualifies_AnyPositiveScore_WhenListHasRoom()
        {
            var entries = new List<LeaderboardEntry> { Entry("AAA", 9000) };

            Assert.That(Leaderboard.Qualifies(entries, 1000), Is.True);
            Assert.That(Leaderboard.Qualifies(new List<LeaderboardEntry>(), 1), Is.True);
        }

        [Test]
        public void Qualifies_NotForZero()
        {
            Assert.That(Leaderboard.Qualifies(new List<LeaderboardEntry>(), 0), Is.False);
        }

        [Test]
        public void Qualifies_WhenFull_OnlyIfAboveTenthPlace()
        {
            List<LeaderboardEntry> entries = FullList(); // 10th place is 1,000

            Assert.That(Leaderboard.Qualifies(entries, 1001), Is.True);
            Assert.That(Leaderboard.Qualifies(entries, 999), Is.False);
            // Ties go to the earlier entry, so matching 10th place isn't enough.
            Assert.That(Leaderboard.Qualifies(entries, 1000), Is.False);
        }

        [Test]
        public void Insert_ReturnsRank_AndKeepsOrder()
        {
            var entries = new List<LeaderboardEntry> { Entry("TOP", 9000), Entry("LOW", 1000) };
            LeaderboardEntry added = Entry("MID", 5000, 1);

            int rank = Leaderboard.Insert(entries, added);

            Assert.That(rank, Is.EqualTo(1));
            Assert.That(entries[1], Is.SameAs(added));
            Assert.That(entries.Count, Is.EqualTo(3));
        }

        [Test]
        public void Insert_TiedScore_GoesAfterEarlierEntry()
        {
            var entries = new List<LeaderboardEntry> { Entry("OLD", 5000, 0) };

            int rank = Leaderboard.Insert(entries, Entry("NEW", 5000, 1));

            Assert.That(rank, Is.EqualTo(1));
        }

        [Test]
        public void Insert_IntoFullList_DropsTheLowest()
        {
            List<LeaderboardEntry> entries = FullList();
            LeaderboardEntry added = Entry("NEW", 5500, 100);

            int rank = Leaderboard.Insert(entries, added);

            Assert.That(rank, Is.EqualTo(5)); // below 10k, 9k, 8k, 7k, 6k
            Assert.That(entries.Count, Is.EqualTo(Leaderboard.Capacity));
            Assert.That(entries[entries.Count - 1].Score, Is.EqualTo(2000)); // 1,000 fell off
        }

        [Test]
        public void Insert_NonQualifyingScore_ReturnsMinusOne_AndLeavesListAlone()
        {
            List<LeaderboardEntry> entries = FullList();
            LeaderboardEntry added = Entry("NOP", 500, 100);

            int rank = Leaderboard.Insert(entries, added);

            Assert.That(rank, Is.EqualTo(-1));
            Assert.That(entries, Has.No.Member(added));
            Assert.That(entries.Count, Is.EqualTo(Leaderboard.Capacity));
        }

        [Test]
        public void Insert_ZeroScore_ReturnsMinusOne_EvenWithRoom()
        {
            var entries = new List<LeaderboardEntry> { Entry("AAA", 9000) };

            int rank = Leaderboard.Insert(entries, Entry("ZER", 0, 1));

            Assert.That(rank, Is.EqualTo(-1));
            Assert.That(entries.Count, Is.EqualTo(1));
        }

        [TestCase('a', 'A')]
        [TestCase('Z', 'Z')]
        [TestCase('5', '\0')]
        [TestCase(' ', '\0')]
        [TestCase('-', '\0')]
        [TestCase('é', '\0')]
        public void ValidateInitial_UppercasesLetters_RejectsTheRest(char typed, char expected)
        {
            Assert.That(Leaderboard.ValidateInitial(typed), Is.EqualTo(expected));
        }

        [TestCase("cjg", "CJG")]
        [TestCase("c.j-g!", "CJG")]
        [TestCase("abcdef", "ABC")]
        [TestCase("a1", "A")]
        [TestCase("123", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void CleanInitials_KeepsUpToThreeUppercaseLetters(string text, string expected)
        {
            Assert.That(Leaderboard.CleanInitials(text), Is.EqualTo(expected));
        }
    }
}
