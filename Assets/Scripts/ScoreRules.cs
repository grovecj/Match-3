using System.Collections.Generic;

namespace Match3
{
    /// <summary>
    /// The scoring rules, as plain arithmetic: what a run is worth, what a blasted gem is worth,
    /// and how the cascade multiplier grows. <see cref="ScoreKeeper"/> uses it to add up the
    /// score, and the Edit Mode tests check it directly, with no Board or scene.
    ///
    /// The server (grovecj/match-3-api) checks every posted score against these same numbers,
    /// so they're constants rather than Inspector fields: a value changed in one scene would
    /// make the game and the server disagree. To change a rule, change it here, raise
    /// <see cref="Version"/>, and update the API to match. Docs/Scoring.md lists both sides.
    /// </summary>
    // A `static` class can't be instantiated: it's just a home for constants and methods that
    // need no object. Rules that will be tuned in playtests are easiest to work on as "pure"
    // functions like these: the answer depends only on the arguments, so a test is one line.
    public static class ScoreRules
    {
        /// <summary>
        /// Which version of these rules the game plays by. Sent with every round's stats, so the
        /// server knows which rules to check the score against. Raise it whenever a number or a
        /// rule below changes.
        /// </summary>
        // 1 was the original scoring (runs only, no multiplier), which never sent a version.
        public const int Version = 2;

        /// <summary>Points for a run of exactly 3.</summary>
        public const int PointsForThree = 1000;
        /// <summary>Points for a run of exactly 4.</summary>
        public const int PointsForFour = 2000;
        /// <summary>Points for a run of 5 or more.</summary>
        public const int PointsForFiveOrMore = 5000;
        /// <summary>
        /// Points for each gem a detonation (a Flame, Star or Hypercube going off) destroys
        /// that wasn't part of a run.
        /// </summary>
        public const int PointsPerBlastedGem = 250;
        /// <summary>The cascade multiplier never goes above this.</summary>
        public const int MaxMultiplier = 5;

        /// <summary>Points for one run of <paramref name="runLength"/> gems.</summary>
        public static int PointsForRun(int runLength)
        {
            if (runLength >= 5)
            {
                return PointsForFiveOrMore;
            }
            if (runLength == 4)
            {
                return PointsForFour;
            }
            return PointsForThree;
        }

        /// <summary>
        /// The multiplier for a clear step: x1 for the player's own match (cascade index 0), x2
        /// for the first cascade, and so on, up to <see cref="MaxMultiplier"/>.
        /// </summary>
        public static int Multiplier(int cascadeIndex)
        {
            // Math.Clamp keeps a number between a low and a high limit. A negative index makes
            // no sense, but x1 is the safe answer if one ever turns up.
            return System.Math.Clamp(cascadeIndex + 1, 1, MaxMultiplier);
        }

        /// <summary>
        /// What a clear step is worth before its multiplier: every run it cleared, plus every
        /// gem its blasts destroyed.
        /// </summary>
        /// <param name="runLengths">The length of each run the step cleared (none for a Hypercube swap).</param>
        /// <param name="blastedGems">How many gems the step's detonations destroyed.</param>
        public static int BasePoints(IReadOnlyList<int> runLengths, int blastedGems)
        {
            int points = blastedGems * PointsPerBlastedGem;
            foreach (int runLength in runLengths)
            {
                points += PointsForRun(runLength);
            }
            return points;
        }

        /// <summary>
        /// What a clear step is worth: its <see cref="BasePoints"/> times the
        /// <see cref="Multiplier"/> for its cascade index. Every wave of a chain reaction is
        /// inside one step, so they all share the step's multiplier.
        /// </summary>
        public static int PointsForStep(IReadOnlyList<int> runLengths, int blastedGems, int cascadeIndex)
        {
            return BasePoints(runLengths, blastedGems) * Multiplier(cascadeIndex);
        }
    }
}
