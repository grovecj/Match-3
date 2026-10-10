using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// A summary of one whole clear step (one pass of the Board's cascade loop, or a swapped
    /// Hypercube going off): which runs it cleared, how many gems its blasts destroyed, and
    /// where it falls in the turn. The <see cref="Board"/> hands one to
    /// <see cref="Board.ClearStepScored"/> once the step's chain reaction has finished, and
    /// that's what the score is worked out from (see <see cref="ScoreRules"/>).
    /// </summary>
    // Scoring per step rather than per run is what makes the multiplier simple: everything in
    // a step (its runs, and every wave of its blasts) shares one multiplier.
    public class ClearStep
    {
        /// <summary>0 for the player's own swap, 1 for the first cascade after it, and so on.</summary>
        public int CascadeIndex { get; }

        /// <summary>The length of each run the step cleared. Empty for a swapped Hypercube.</summary>
        public IReadOnlyList<int> RunLengths { get; }

        /// <summary>
        /// How many gems the step's detonations destroyed that weren't part of a run. A power
        /// gem caught in a blast counts here too: it was destroyed, even though it went off.
        /// </summary>
        public int BlastedGems { get; }

        /// <summary>
        /// Every gem that left the board in this step: the matched ones, the blasted ones, and
        /// any power gem that went off without being either (a swapped Hypercube). A power gem
        /// made by a match stays on the board, so it isn't counted.
        /// </summary>
        public int GemsCleared { get; }

        public ClearStep(int cascadeIndex, IReadOnlyList<int> runLengths, int blastedGems, int gemsCleared)
        {
            CascadeIndex = cascadeIndex;
            RunLengths = runLengths;
            BlastedGems = blastedGems;
            GemsCleared = gemsCleared;
        }

        /// <summary>What the step is worth, multiplier included.</summary>
        public int Points => ScoreRules.PointsForStep(RunLengths, BlastedGems, CascadeIndex);

        /// <summary>
        /// Builds the summary of a step from what the model said it did.
        /// </summary>
        /// <param name="result">What <see cref="BoardModel.ClearMatches"/> or <see cref="BoardModel.ActivateHypercube"/> returned.</param>
        /// <param name="groups">The matches that were cleared (none for a Hypercube swap).</param>
        /// <param name="cascadeIndex">Which step of the turn this is.</param>
        public static ClearStep From(BoardModel.ClearResult result, IReadOnlyList<MatchGroup> groups, int cascadeIndex)
        {
            var runLengths = new List<int>();
            foreach (MatchGroup group in groups)
            {
                foreach (List<Vector2Int> run in group.Runs)
                {
                    runLengths.Add(run.Count);
                }
            }

            // The model never lists a cell twice across a step's blasts, so adding them up
            // counts every blasted gem exactly once.
            int blasted = 0;
            foreach (BoardModel.Detonation detonation in result.Detonations)
            {
                blasted += detonation.Blasted.Count;
            }

            // Every gem that went off left the board. Those in later waves were caught in a
            // blast, so they're already in `blasted`; a first-wave gem is usually a matched one,
            // already in Matched. What's left is a gem that was neither: a swapped Hypercube, or
            // a power gem that was the anchor of a match making a new one (the old gem went off,
            // the new one took its cell).
            var matched = new HashSet<Vector2Int>(result.Matched);
            int wentOffUncounted = 0;
            if (result.Waves.Count > 0)
            {
                foreach (BoardModel.Detonation detonation in result.Waves[0])
                {
                    if (!matched.Contains(detonation.Cell))
                    {
                        wentOffUncounted++;
                    }
                }
            }

            return new ClearStep(cascadeIndex, runLengths, blasted,
                result.Matched.Count + blasted + wentOffUncounted);
        }
    }
}
