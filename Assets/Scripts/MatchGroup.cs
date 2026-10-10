using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// What kind of match a <see cref="MatchGroup"/> is. Later this decides which power gem the
    /// match leaves behind; for now it is only worked out, not used.
    /// </summary>
    // Listed weakest first, so a bigger number is a stronger shape.
    public enum MatchShape
    {
        /// <summary>A single run of 3.</summary>
        Line3,
        /// <summary>A single run of exactly 4.</summary>
        Line4,
        /// <summary>Two or more runs that cross: an L, a T or a +.</summary>
        Cross,
        /// <summary>Anything containing a run of 5 or more, even if another run crosses it.</summary>
        Line5,
    }

    /// <summary>
    /// One whole match: every run of one colour that is joined to the others by a shared cell.
    /// A plain line is a group of one run; an L, T or + is a group of two or more.
    /// <see cref="BoardModel.FindMatchGroups"/> makes these.
    /// </summary>
    // A class rather than a struct (unlike Piece): it holds lists, and it's handed around as one
    // thing rather than stored by the hundred in an array.
    public class MatchGroup
    {
        /// <summary>The colour every piece in the group shares.</summary>
        // `{ get; }` with no setter is a read-only property: it can only be assigned in the
        // constructor, so a group never changes after it's made.
        public TokenType Colour { get; }

        /// <summary>
        /// The runs that make up the group, in the order <see cref="BoardModel.FindMatches"/>
        /// found them (rows before columns). Runs that cross both list the cell they share.
        /// </summary>
        // IReadOnlyList lets other code read and loop over the list but not add to or remove from it.
        public IReadOnlyList<List<Vector2Int>> Runs { get; }

        /// <summary>Every cell in the group, each listed once.</summary>
        public IReadOnlyList<Vector2Int> Cells { get; }

        public MatchShape Shape { get; }

        /// <summary>
        /// The cell where a power gem made by this match would appear: the cell the player
        /// swapped into the group, or failing that a fixed cell worked out from the shape.
        /// See <see cref="ChooseAnchor"/>.
        /// </summary>
        public Vector2Int Anchor { get; }

        /// <param name="runs">The group's runs: one or more, all of <paramref name="colour"/>, joined by shared cells.</param>
        /// <param name="swappedA">One of the two cells the player just swapped, or null for a cascade.</param>
        /// <param name="swappedB">The other swapped cell, or null for a cascade.</param>
        public MatchGroup(TokenType colour, List<List<Vector2Int>> runs, Vector2Int? swappedA = null, Vector2Int? swappedB = null)
        {
            Colour = colour;
            Runs = runs;

            // Walk the runs collecting each cell once. A cell we meet a second time is where
            // two runs cross; remember the first of those for the anchor.
            var cells = new List<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            Vector2Int? crossing = null;
            foreach (List<Vector2Int> run in runs)
            {
                foreach (Vector2Int cell in run)
                {
                    // HashSet.Add returns false if the cell was already in the set.
                    if (seen.Add(cell))
                    {
                        cells.Add(cell);
                    }
                    else if (!crossing.HasValue)
                    {
                        crossing = cell;
                    }
                }
            }
            Cells = cells;

            Shape = Classify(runs);
            Anchor = ChooseAnchor(runs, seen, crossing, Shape, swappedA, swappedB);
        }

        /// <summary>
        /// The shape a group made of <paramref name="runs"/> is. The rules are tried strongest
        /// first, so a run of 5 that another run crosses is a <see cref="MatchShape.Line5"/>,
        /// not a <see cref="MatchShape.Cross"/>.
        /// </summary>
        public static MatchShape Classify(List<List<Vector2Int>> runs)
        {
            int longest = LongestRun(runs).Count;
            if (longest >= 5)
            {
                return MatchShape.Line5;
            }
            // Runs only end up in the same group by sharing a cell, and two runs in the same
            // row or column can't do that, so more than one run always means a crossing.
            if (runs.Count > 1)
            {
                return MatchShape.Cross;
            }
            return longest == 4 ? MatchShape.Line4 : MatchShape.Line3;
        }

        /// <summary>
        /// Picks the group's anchor cell. Every rule gives one answer for a given board, never
        /// a random or "whichever came first in memory" one, so tests can say exactly where it
        /// should be:
        ///  - a swapped cell that is in the group wins (<paramref name="swappedA"/> is checked
        ///    first; in play the two swapped cells are different colours, so only one can be in);
        ///  - otherwise, for a <see cref="MatchShape.Cross"/>, the cell where two runs cross.
        ///    If there are several crossings, it's the lowest one in the leftmost column that
        ///    has any;
        ///  - otherwise the middle cell of the longest run, or the lower/left of the two middle
        ///    cells when its length is even.
        /// </summary>
        private static Vector2Int ChooseAnchor(List<List<Vector2Int>> runs, HashSet<Vector2Int> cells,
            Vector2Int? crossing, MatchShape shape, Vector2Int? swappedA, Vector2Int? swappedB)
        {
            // `is Vector2Int a` is true when the nullable holds a value, and puts that value in `a`.
            if (swappedA is Vector2Int a && cells.Contains(a))
            {
                return a;
            }
            if (swappedB is Vector2Int b && cells.Contains(b))
            {
                return b;
            }

            if (shape == MatchShape.Cross && crossing is Vector2Int crossingCell)
            {
                return crossingCell;
            }

            // A run's cells are in order along the line, so the middle one is at the middle
            // index. Integer division rounds down: (4 - 1) / 2 = 1, the lower of the two middles.
            List<Vector2Int> line = LongestRun(runs);
            return line[(line.Count - 1) / 2];
        }

        /// <summary>The longest of <paramref name="runs"/>; the earliest one if several tie.</summary>
        private static List<Vector2Int> LongestRun(List<List<Vector2Int>> runs)
        {
            List<Vector2Int> longest = runs[0];
            foreach (List<Vector2Int> run in runs)
            {
                if (run.Count > longest.Count)
                {
                    longest = run;
                }
            }
            return longest;
        }
    }
}
