using System;

namespace Match3
{
    /// <summary>
    /// What happened in one round, counted as it's played: how many swaps, how many matches of
    /// each size, the longest match and the longest chain. It's sent to the server with the
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

        /// <summary>Back to all zeros, for a new round.</summary>
        public void Reset()
        {
            swaps = 0;
            runs3 = 0;
            runs4 = 0;
            runs5 = 0;
            longestRun = 0;
            bestChain = 0;
        }

        /// <summary>The player swapped two tokens (whether or not it made a match).</summary>
        public void SwapStarted()
        {
            swaps++;
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

        /// <summary>A separate copy, which later counting doesn't change.</summary>
        public RoundStats Copy()
        {
            // MemberwiseClone copies every field. That's all a copy needs here, because the
            // fields are plain numbers.
            return (RoundStats)MemberwiseClone();
        }
    }
}
