using System;

namespace Match3
{
    /// <summary>
    /// What happened in one round, counted as it's played: how many swaps, how many matches of
    /// each size, the longest match and the longest chain, the power gems made and set off, and
    /// how the score was built up. It's sent to the server with the
    /// round's score, and the server works out the player's achievements from it (see
    /// Docs/Achievements.md).
    ///
    /// A plain class with no Unity in it, so the Edit Mode tests can drive it directly. The
    /// <see cref="RoundStatsTracker"/> is what connects it to the <see cref="Board"/>.
    /// </summary>
    // The field names are the JSON keys the server expects: JsonUtility writes public fields
    // under their own names.
    [Serializable]
    public class RoundStats
    {
        /// <summary>
        /// Which scoring rules the round was played by (<see cref="ScoreRules.Version"/>), so the
        /// server checks the score against the right ones.
        /// </summary>
        public int scoringVersion = ScoreRules.Version;
        /// <summary>Swaps the player tried, including ones that made no match and swapped back.</summary>
        public int swaps;
        /// <summary>Matches of exactly 3 tokens.</summary>
        public int runs3;
        /// <summary>Matches of exactly 4 tokens.</summary>
        public int runs4;
        /// <summary>Matches of 5 or more tokens.</summary>
        public int runs5;
        /// <summary>The longest match, or 0 if there were none.</summary>
        public int longestRun;
        /// <summary>
        /// The most clears one swap set off: 1 for a plain match, 2 if the falling tokens then
        /// made another match, and so on. 0 if nothing was matched.
        /// </summary>
        public int bestChain;

        /// <summary>
        /// Gems destroyed by a power gem's blast (a Flame, Star or Hypercube going off) rather
        /// than by being part of a match. Each is worth <see cref="ScoreRules.PointsPerBlastedGem"/>.
        /// </summary>
        public int blastedGems;
        /// <summary>
        /// The points scored at each multiplier, before it was applied: [0] is everything scored
        /// at x1, [1] at x2, up to [4] at x5. With it the server can check the score exactly:
        /// these add up to what the runs and <see cref="blastedGems"/> are worth, and the score
        /// is each one times its multiplier, added up.
        /// </summary>
        public int[] basePointsAtMultiplier = new int[ScoreRules.MaxMultiplier];

        /// <summary>Flames made (by a line of 4).</summary>
        public int flamesCreated;
        /// <summary>Stars made (by an L, T or + shape).</summary>
        public int starsCreated;
        /// <summary>Hypercubes made (by a line of 5 or more).</summary>
        public int hypercubesCreated;
        /// <summary>Flames that went off, whether matched or caught in another blast.</summary>
        public int flamesDetonated;
        /// <summary>Stars that went off.</summary>
        public int starsDetonated;
        /// <summary>Hypercubes that went off, whether swapped or caught in another blast.</summary>
        public int hypercubesDetonated;
        /// <summary>The most gems one swap cleared, counting every cascade it set off.</summary>
        public int mostGemsOneSwap;

        // The gems the current swap has cleared so far. Only needed while counting, so it isn't
        // sent: [NonSerialized] tells JsonUtility to leave a field out. (It would skip a private
        // field anyway; the attribute just says so where it can be seen.)
        [NonSerialized] private int gemsThisSwap;

        /// <summary>Back to all zeros, for a new round.</summary>
        public void Reset()
        {
            scoringVersion = ScoreRules.Version;
            swaps = 0;
            runs3 = 0;
            runs4 = 0;
            runs5 = 0;
            longestRun = 0;
            bestChain = 0;
            blastedGems = 0;
            basePointsAtMultiplier = new int[ScoreRules.MaxMultiplier];
            flamesCreated = 0;
            starsCreated = 0;
            hypercubesCreated = 0;
            flamesDetonated = 0;
            starsDetonated = 0;
            hypercubesDetonated = 0;
            mostGemsOneSwap = 0;
            gemsThisSwap = 0;
        }

        /// <summary>The player swapped two tokens (whether or not it made a match).</summary>
        public void SwapStarted()
        {
            swaps++;
            gemsThisSwap = 0;
        }

        /// <summary>
        /// One match of <paramref name="runLength"/> tokens was cleared. The sizes are grouped
        /// the way the score groups them (3, 4, and 5 or more), which is what lets the server
        /// check that the matches add up to the score.
        /// </summary>
        public void RunCleared(int runLength)
        {
            if (runLength >= 5)
            {
                runs5++;
            }
            else if (runLength == 4)
            {
                runs4++;
            }
            else
            {
                runs3++;
            }
            longestRun = Math.Max(longestRun, runLength);
        }

        /// <summary>
        /// A clear step finished. <paramref name="cascadeIndex"/> is 0 for the player's own
        /// match and counts up for each cascade after it, so the chain so far is one more.
        /// </summary>
        public void StepResolved(int cascadeIndex)
        {
            bestChain = Math.Max(bestChain, cascadeIndex + 1);
        }

        /// <summary>
        /// A clear step was scored (see <see cref="ClearStep"/>): counts its blasted gems, the
        /// points it scored at its multiplier, and the gems it cleared towards this swap's total.
        /// </summary>
        public void StepScored(ClearStep step)
        {
            blastedGems += step.BlastedGems;
            // Multiplier x1 goes in [0], x2 in [1], and so on.
            int slot = ScoreRules.Multiplier(step.CascadeIndex) - 1;
            basePointsAtMultiplier[slot] += ScoreRules.BasePoints(step.RunLengths, step.BlastedGems);
            gemsThisSwap += step.GemsCleared;
            mostGemsOneSwap = Math.Max(mostGemsOneSwap, gemsThisSwap);
        }

        /// <summary>A match made a power gem of <paramref name="kind"/>.</summary>
        public void PowerGemCreated(PowerKind kind)
        {
            // A switch picks one branch by value: tidier than a chain of `if`s on the same thing.
            switch (kind)
            {
                case PowerKind.Flame: flamesCreated++; break;
                case PowerKind.Star: starsCreated++; break;
                case PowerKind.Hypercube: hypercubesCreated++; break;
            }
        }

        /// <summary>A power gem of <paramref name="kind"/> went off.</summary>
        public void PowerGemDetonated(PowerKind kind)
        {
            switch (kind)
            {
                case PowerKind.Flame: flamesDetonated++; break;
                case PowerKind.Star: starsDetonated++; break;
                case PowerKind.Hypercube: hypercubesDetonated++; break;
            }
        }

        /// <summary>A separate copy, which later counting doesn't change.</summary>
        public RoundStats Copy()
        {
            // MemberwiseClone copies every field. For the numbers that's a real copy, but an
            // array is an object: the clone would get a reference to the *same* array, and later
            // counting would change both. So the array is copied on its own.
            var copy = (RoundStats)MemberwiseClone();
            copy.basePointsAtMultiplier = (int[])basePointsAtMultiplier.Clone();
            return copy;
        }
    }
}
