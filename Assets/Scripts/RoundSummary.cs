using System.Collections.Generic;
using System.Globalization;

namespace Match3
{
    /// <summary>
    /// What the round-over screen says about a finished round (issue #107): the score, how it
    /// compares with the personal best, and a few stat lines from the round's
    /// <see cref="RoundStats"/>.
    ///
    /// <see cref="Create"/> is a pure function: the same score, best and stats always give the
    /// same lines, and it shows nothing itself. Keeping the wording here, away from the UI,
    /// is what lets the Edit Mode tests check every message without running a round. The
    /// <see cref="RoundSummaryView"/> puts the lines on screen.
    /// </summary>
    public class RoundSummary
    {
        public const string Headline = "Time's up!";

        private RoundSummary(string scoreLine, bool isNewBest, List<string> statLines)
        {
            ScoreLine = scoreLine;
            IsNewBest = isNewBest;
            StatLines = statLines;
        }

        /// <summary>
        /// "84,200: New best!", "71,800, 12,400 short of your best", "84,000: equals your best",
        /// or just "84,200" for the first round ever.
        /// </summary>
        public string ScoreLine { get; }

        /// <summary>True if the round beat the previous best (a tie doesn't).</summary>
        public bool IsNewBest { get; }

        /// <summary>
        /// One line per stat: the power gems made, the best chain, and the most gems one swap
        /// cleared. New stats (e.g. from the power gem achievements) can simply be added here.
        /// </summary>
        // IReadOnlyList: callers can read the lines but not add to or change the list.
        public IReadOnlyList<string> StatLines { get; }

        /// <summary>Every line in order, one per row: the headline, the score line, then the stats.</summary>
        public string AllLines()
        {
            return Headline + "\n" + ScoreLine + "\n" + string.Join("\n", StatLines);
        }

        /// <summary>
        /// The summary for a round that scored <paramref name="score"/>, given the best
        /// <i>before</i> this round (null if this is the first round) and its stats.
        /// </summary>
        public static RoundSummary Create(int score, int? previousBest, RoundStats stats)
        {
            bool isNewBest = PersonalBest.Beats(score, previousBest);
            return new RoundSummary(ScoreLineFor(score, previousBest), isNewBest, StatLinesFor(stats));
        }

        private static string ScoreLineFor(int score, int? previousBest)
        {
            string scoreText = Format(score);
            if (!previousBest.HasValue)
            {
                return scoreText;
            }
            int best = previousBest.Value;
            if (score > best)
            {
                return $"{scoreText}: New best!";
            }
            if (score == best)
            {
                return $"{scoreText}: equals your best";
            }
            return $"{scoreText}, {Format(best - score)} short of your best";
        }

        private static List<string> StatLinesFor(RoundStats stats)
        {
            return new List<string>
            {
                // "·" is the middle dot (·). Written as an escape so it can't be mangled by
                // a text editor saving the file in a different encoding.
                $"Flames {stats.flamesCreated} · Stars {stats.starsCreated} · Hypercubes {stats.hypercubesCreated}",
                $"Best chain: {stats.bestChain}",
                $"Most gems from one swap: {stats.mostGemsOneSwap}",
            };
        }

        // "N0" = thousands separators, no decimals; the invariant culture always uses commas
        // (see ScoreView).
        private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
