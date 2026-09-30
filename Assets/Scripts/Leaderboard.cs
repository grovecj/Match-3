using System;
using System.Collections.Generic;

namespace Match3
{
    /// <summary>
    /// The leaderboard's rules, kept apart from saving and from the UI so they're easy to read
    /// and to test: how entries are ordered, which scores make the list, and what counts as
    /// valid initials.
    ///
    /// It's a static class (no instances, no Unity components) because the rules don't need any
    /// state of their own; every method works only on what it's given.
    /// </summary>
    public static class Leaderboard
    {
        /// <summary>How many entries the leaderboard keeps.</summary>
        public const int Capacity = 10;

        /// <summary>The most letters a player's initials can have.</summary>
        public const int MaxInitials = 3;

        /// <summary>
        /// The scores a brand-new leaderboard starts with, best first: 150,000 in 1st place down
        /// to 50,000 in 10th. They give a new player something to aim for instead of an empty
        /// list, and they're the same in every build (Windows and web) because they come from
        /// code rather than from a save file.
        /// </summary>
        private static readonly (string initials, int score)[] Defaults =
        {
            ("GEM", 150000),
            ("ACE", 135000),
            ("MAX", 120000),
            ("ZAP", 110000),
            ("JOY", 100000),
            ("SKY", 90000),
            ("BOP", 80000),
            ("FIZ", 70000),
            ("DOT", 60000),
            ("ELF", 50000),
        };

        /// <summary>
        /// All default entries share this timestamp. It's earlier than any real game, so a
        /// player who exactly ties a default score loses the tie (see <see cref="Compare"/>).
        /// </summary>
        private static readonly DateTime DefaultsTimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// A new list of the default entries (see <see cref="Defaults"/>). It's a fresh list
        /// every call, so whoever gets it can add to it without changing the defaults.
        /// </summary>
        public static List<LeaderboardEntry> DefaultEntries()
        {
            var entries = new List<LeaderboardEntry>(Defaults.Length);
            foreach ((string initials, int score) in Defaults)
            {
                entries.Add(new LeaderboardEntry(initials, score, DefaultsTimestampUtc));
            }
            return entries;
        }

        /// <summary>
        /// Orders two entries for sorting: higher score first, and for equal scores the earlier
        /// entry first. Like every comparison used with List.Sort, it returns a negative number
        /// if <paramref name="a"/> goes first, positive if <paramref name="b"/> does, 0 if tied.
        /// </summary>
        public static int Compare(LeaderboardEntry a, LeaderboardEntry b)
        {
            // Comparing b to a (not a to b) flips the usual smallest-first order to
            // biggest-first.
            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
            {
                return byScore;
            }
            return a.TimestampUtc.CompareTo(b.TimestampUtc);
        }

        /// <summary>
        /// Would a new <paramref name="score"/> make the list? It needs to be above 0, and fewer
        /// than <see cref="Capacity"/> existing entries may be at least as high. (A score that
        /// ties the 10th place loses the tie, since the existing entry is earlier.)
        /// </summary>
        public static bool Qualifies(IReadOnlyList<LeaderboardEntry> entries, int score)
        {
            if (score <= 0)
            {
                return false;
            }

            int atLeastAsHigh = 0;
            foreach (LeaderboardEntry entry in entries)
            {
                if (entry.Score >= score)
                {
                    atLeastAsHigh++;
                }
            }
            return atLeastAsHigh < Capacity;
        }

        /// <summary>
        /// Adds <paramref name="entry"/> to <paramref name="entries"/>, sorts the list, and cuts
        /// it back to <see cref="Capacity"/>. Returns the new entry's 0-based rank, or -1 if it
        /// doesn't <see cref="Qualifies"/> (in which case the list is left unchanged).
        /// </summary>
        public static int Insert(List<LeaderboardEntry> entries, LeaderboardEntry entry)
        {
            if (!Qualifies(entries, entry.Score))
            {
                return -1;
            }

            entries.Add(entry);
            Sort(entries);

            if (entries.Count > Capacity)
            {
                entries.RemoveRange(Capacity, entries.Count - Capacity);
            }
            // IndexOf looks for this exact object, so an older entry with the same initials and
            // score can't be mistaken for it.
            return entries.IndexOf(entry);
        }

        /// <summary>Sorts best first, using <see cref="Compare"/>.</summary>
        public static void Sort(List<LeaderboardEntry> entries)
        {
            // List.Sort takes a method that compares two items; it does the rest.
            entries.Sort(Compare);
        }

        /// <summary>
        /// Checks one typed character: a letter comes back uppercased, anything else comes back
        /// as '\0', which is how TMP_InputField's onValidateInput says "reject this character".
        /// Only A-Z count as letters here (not accented or other alphabets' letters).
        /// </summary>
        public static char ValidateInitial(char c)
        {
            char upper = char.ToUpperInvariant(c);
            return upper >= 'A' && upper <= 'Z' ? upper : '\0';
        }

        /// <summary>
        /// Keeps only the valid letters of <paramref name="text"/>, uppercased, and at most
        /// <see cref="MaxInitials"/> of them. "c.j-g!x" becomes "CJG". Returns "" for null.
        /// </summary>
        public static string CleanInitials(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var cleaned = new System.Text.StringBuilder(MaxInitials);
            foreach (char c in text)
            {
                char valid = ValidateInitial(c);
                if (valid != '\0')
                {
                    cleaned.Append(valid);
                    if (cleaned.Length == MaxInitials)
                    {
                        break;
                    }
                }
            }
            return cleaned.ToString();
        }
    }
}
