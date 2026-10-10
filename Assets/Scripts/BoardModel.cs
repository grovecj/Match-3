using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The board's game rules as plain data: which <see cref="Piece"/> is in each cell, and
    /// how that changes when tokens are swapped, matched, cleared, and replaced. This is the
    /// "model"; <see cref="Board"/> is the "view + orchestration" that turns what changed here
    /// into GameObjects and animations.
    ///
    /// Unlike Board, this is not a MonoBehaviour: it's an ordinary C# class you create with
    /// `new`. It never touches the scene, so tests can build one from a few strings, call a
    /// method, and check the result, all without GameObjects, frames, or Play Mode.
    ///
    /// Grid convention (same as Board): (0, 0) is the bottom-left cell, x grows to the right,
    /// y grows upward. A cell with nothing in it holds <see cref="Piece.Empty"/>.
    /// </summary>
    public class BoardModel
    {
        /// <summary>The smallest run of same-type tokens that counts as a match.</summary>
        public const int MinMatchLength = 3;

        /// <summary>
        /// A token moving down its column from <see cref="From"/> to <see cref="To"/> because
        /// the cells below it were cleared. <see cref="Piece"/> is what it is: falling moves a
        /// piece whole, so a power gem lands with its power.
        /// </summary>
        // A struct is a small value type: it's copied on assignment, like an int or a Vector2Int.
        // `readonly` promises its fields never change after construction.
        public readonly struct Fall
        {
            public readonly Vector2Int From;
            public readonly Vector2Int To;
            public readonly Piece Piece;

            public Fall(Vector2Int from, Vector2Int to, Piece piece)
            {
                From = from;
                To = to;
                Piece = piece;
            }
        }

        /// <summary>
        /// A new <see cref="Piece"/> placed in <see cref="Cell"/> to refill the board.
        /// <see cref="FallDistance"/> is how many cells above <see cref="Cell"/> it should appear
        /// so it can fall into place alongside the other new tokens in its column.
        /// </summary>
        public readonly struct Spawn
        {
            public readonly Vector2Int Cell;
            public readonly Piece Piece;
            public readonly int FallDistance;

            public Spawn(Vector2Int cell, Piece piece, int fallDistance)
            {
                Cell = cell;
                Piece = piece;
                FallDistance = fallDistance;
            }

            /// <summary>The new piece's colour: shorthand for <c>Piece.Colour</c>.</summary>
            public TokenType Type => Piece.Colour;
        }

        /// <summary>
        /// A token moved from <see cref="From"/> to <see cref="To"/> by <see cref="Shuffle"/>.
        /// Unlike a <see cref="Fall"/>, the two cells can be anywhere on the board.
        /// </summary>
        public readonly struct Move
        {
            public readonly Vector2Int From;
            public readonly Vector2Int To;

            public Move(Vector2Int from, Vector2Int to)
            {
                From = from;
                To = to;
            }
        }

        /// <summary>
        /// A power gem a match left behind: <see cref="Piece"/> now sits in <see cref="Cell"/>,
        /// which was one of the matched cells (the match's anchor).
        /// </summary>
        public readonly struct Creation
        {
            public readonly Vector2Int Cell;
            public readonly Piece Piece;

            public Creation(Vector2Int cell, Piece piece)
            {
                Cell = cell;
                Piece = piece;
            }

            /// <summary>Which power gem was made: shorthand for <c>Piece.Power</c>.</summary>
            public PowerKind Kind => Piece.Power;
        }

        /// <summary>
        /// A power gem that went off because it was part of a match. <see cref="Piece"/> is the
        /// gem as it was (so its colour is still known after it's gone), <see cref="Cell"/> is
        /// where it was, and <see cref="Blasted"/> lists the cells its blast emptied.
        /// </summary>
        public readonly struct Detonation
        {
            public readonly Vector2Int Cell;
            public readonly Piece Piece;
            /// <summary>
            /// The cells this blast emptied, each one a piece that was NOT part of a match.
            /// Cells the match had already cleared aren't listed, and nor is a cell an earlier
            /// detonation in the same step got to first, so no cell is ever listed twice.
            /// </summary>
            public readonly IReadOnlyList<Vector2Int> Blasted;

            public Detonation(Vector2Int cell, Piece piece, IReadOnlyList<Vector2Int> blasted)
            {
                Cell = cell;
                Piece = piece;
                Blasted = blasted;
            }

            /// <summary>Which power gem went off: shorthand for <c>Piece.Power</c>.</summary>
            public PowerKind Kind => Piece.Power;
        }

        /// <summary>
        /// Everything one clear step did to the board, split into the three things the
        /// <see cref="Board"/> shows separately: gems that matched, power gems that appeared, and
        /// gems caught in a blast. <see cref="ClearMatches"/> makes these.
        /// </summary>
        // The model does the whole step at once and hands back a description of it, the same way
        // Collapse hands back a list of Falls. The Board can then take as long as it likes
        // playing that description back, phase by phase, without asking the model anything else.
        public class ClearResult
        {
            /// <summary>
            /// The matched cells that were emptied, each listed once. A match's anchor is left
            /// out when a power gem was made there: that cell is in <see cref="Created"/> instead.
            /// </summary>
            public IReadOnlyList<Vector2Int> Matched { get; }

            /// <summary>The power gems this step made, in the order of the matches that made them.</summary>
            public IReadOnlyList<Creation> Created { get; }

            /// <summary>The power gems this step set off, with what each one's blast cleared.</summary>
            public IReadOnlyList<Detonation> Detonations { get; }

            public ClearResult(IReadOnlyList<Vector2Int> matched, IReadOnlyList<Creation> created,
                IReadOnlyList<Detonation> detonations)
            {
                Matched = matched;
                Created = created;
                Detonations = detonations;
            }
        }

        // How many random boards FillWithValidMove and Shuffle try before giving up. A try
        // nearly always works on a normal board; the cap is there so a board that can't have a
        // valid move at all (a 2x2, say) ends in "false" rather than an endless loop.
        private const int MaxAttempts = 100;

        // The two neighbors TryFindValidMove swaps each cell with. The left and lower neighbors
        // aren't needed: those swaps were already tried when the search visited those cells.
        private static readonly Vector2Int[] SwapDirections = { Vector2Int.right, Vector2Int.up };

        // A 2D array of structs: every cell holds a whole Piece (not a reference to one), and
        // a new array starts out full of Piece.Empty.
        private readonly Piece[,] grid;
        // The types new tokens are picked from, with nulls filtered out. Duplicates are kept, so
        // listing a type twice makes it twice as likely, same as in the Inspector array.
        private readonly List<TokenType> tokenTypes = new List<TokenType>();
        private readonly System.Random random;

        /// <summary>
        /// Creates an empty <paramref name="width"/> x <paramref name="height"/> board.
        /// </summary>
        /// <param name="tokenTypes">Types that new tokens are picked from. Nulls are ignored.</param>
        /// <param name="random">
        /// Source of every random choice the model makes. Passing it in (rather than calling
        /// UnityEngine.Random directly) is "dependency injection": the game passes an unseeded
        /// one for variety, while a test can pass `new System.Random(42)` and get the exact same
        /// board every run.
        /// </param>
        public BoardModel(int width, int height, IEnumerable<TokenType> tokenTypes, System.Random random)
        {
            if (width < 1 || height < 1)
            {
                throw new System.ArgumentException($"Board must be at least 1x1, got {width}x{height}.");
            }

            grid = new Piece[width, height];
            this.random = random ?? throw new System.ArgumentNullException(nameof(random));
            if (tokenTypes != null)
            {
                foreach (TokenType type in tokenTypes)
                {
                    if (type != null)
                    {
                        this.tokenTypes.Add(type);
                    }
                }
            }
        }

        // GetLength(0) is the array's size in x, GetLength(1) its size in y.
        public int Width => grid.GetLength(0);
        public int Height => grid.GetLength(1);

        public bool IsInBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
        }

        /// <summary>The piece in cell (x, y): its colour and its power. <see cref="Piece.Empty"/> if there's none.</summary>
        public Piece PieceAt(int x, int y)
        {
            return grid[x, y];
        }

        public Piece PieceAt(Vector2Int cell)
        {
            return grid[cell.x, cell.y];
        }

        /// <summary>Puts <paramref name="piece"/> in cell (x, y). <see cref="Piece.Empty"/> empties the cell.</summary>
        public void SetPieceAt(int x, int y, Piece piece)
        {
            grid[x, y] = piece;
        }

        // TypeAt and SetTypeAt are shorthand for code that only cares about colour, which is
        // most of it: it reads better than going through PieceAt(...).Colour everywhere.

        /// <summary>
        /// The colour of the piece in cell (x, y), or null if it has none: the cell is empty, or
        /// holds a colourless power gem. Use <see cref="PieceAt(int, int)"/> to tell those apart.
        /// </summary>
        public TokenType TypeAt(int x, int y)
        {
            return grid[x, y].Colour;
        }

        public TokenType TypeAt(Vector2Int cell)
        {
            return grid[cell.x, cell.y].Colour;
        }

        /// <summary>Puts an ordinary (no power) piece of <paramref name="type"/> in cell (x, y). Null empties the cell.</summary>
        public void SetTypeAt(int x, int y, TokenType type)
        {
            grid[x, y] = new Piece(type);
        }

        /// <summary>Number of distinct, non-null entries in <paramref name="types"/>.</summary>
        public static int CountDistinctTypes(IEnumerable<TokenType> types)
        {
            if (types == null)
            {
                return 0;
            }

            var distinct = new HashSet<TokenType>();
            foreach (TokenType type in types)
            {
                if (type != null)
                {
                    distinct.Add(type);
                }
            }
            return distinct.Count;
        }

        // ------------------------------------------------------------ Starting fill

        /// <summary>
        /// Puts an ordinary piece of a random type in every cell, avoiding any 3-in-a-row so the
        /// game doesn't start with free matches. Needs at least 3 distinct token types.
        /// Whatever was on the board before is replaced, so any power gems are lost: that's right
        /// for a new round, and <see cref="RedealKeepingPowers"/> is the one to use mid-round.
        /// </summary>
        public void FillWithoutMatches()
        {
            if (CountDistinctTypes(tokenTypes) < 3)
            {
                throw new System.InvalidOperationException("Filling without matches needs at least 3 distinct token types.");
            }

            // Fill column by column, bottom to top. That order matters: by the time we reach
            // (x, y), the two cells to its left and the two cells below it are already filled,
            // which is exactly what PickTypeWithoutMatch needs to look at.
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    grid[x, y] = new Piece(PickTypeWithoutMatch(x, y));
                }
            }
        }

        /// <summary>
        /// <see cref="FillWithoutMatches"/>, repeated until the board also has at least one
        /// valid move, so the player is never dealt a board they can't play. Returns false if
        /// no such board turned up (only on boards too small to ever have a move); the board is
        /// still filled without matches.
        /// </summary>
        public bool FillWithValidMove()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                FillWithoutMatches();
                if (HasValidMove())
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gives every piece on the board a new random colour while keeping its power, with no
        /// 3-in-a-row and at least one valid move: a Flame is still a Flame in the same cell,
        /// just maybe a different colour. A piece with a power but no colour stays colourless,
        /// and an empty cell gets an ordinary piece. Needs at least 3 distinct token types.
        ///
        /// This is for the rare dead board whose pieces can't be rearranged into a playable one
        /// (<see cref="Shuffle"/> returned null). Returns false if no playable board turned up
        /// (only on boards too small to ever have a move); the board is still recoloured without
        /// matches.
        /// </summary>
        public bool RedealKeepingPowers()
        {
            if (CountDistinctTypes(tokenTypes) < 3)
            {
                throw new System.InvalidOperationException("Redealing without matches needs at least 3 distinct token types.");
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                // The same column-by-column order as FillWithoutMatches, for the same reason: the
                // cells PickTypeWithoutMatch looks at have already been given their new colour.
                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        Piece piece = grid[x, y];
                        // A power with no colour: leave it exactly as it is. It can't be part of
                        // a run, so it never needs to change to avoid one.
                        if (piece.Colour == null && piece.Power != PowerKind.None)
                        {
                            continue;
                        }
                        grid[x, y] = new Piece(PickTypeWithoutMatch(x, y), piece.Power);
                    }
                }

                if (HasValidMove())
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Picks a random type for cell (x, y) that won't complete a 3-in-a-row with the two
        /// already-placed tokens to its left or the two below it.
        /// </summary>
        private TokenType PickTypeWithoutMatch(int x, int y)
        {
            // A type is forbidden if the two neighbors in a line are both that same type.
            TokenType forbiddenHorizontal = ColourOfPair(x - 1, y, x - 2, y);
            TokenType forbiddenVertical = ColourOfPair(x, y - 1, x, y - 2);

            // Collect the types that are still allowed, then pick one of them at random.
            // At most two types are forbidden, and FillWithoutMatches guarantees at least
            // 3 distinct types, so there's always at least one candidate left.
            var candidates = new List<TokenType>();
            foreach (TokenType type in tokenTypes)
            {
                if (type != forbiddenHorizontal && type != forbiddenVertical)
                {
                    candidates.Add(type);
                }
            }

            // Next(max) excludes the max, so this returns 0..Count-1: a valid index.
            return candidates[random.Next(candidates.Count)];
        }

        /// <summary>
        /// The colour that cells (x1, y1) and (x2, y2) share, or null if they don't share one
        /// (or the second is off the board's left or bottom edge). A third piece of that colour
        /// next to them would make a 3-in-a-row.
        /// </summary>
        private TokenType ColourOfPair(int x1, int y1, int x2, int y2)
        {
            if (x2 < 0 || y2 < 0)
            {
                return null;
            }
            return grid[x1, y1].Matches(grid[x2, y2]) ? grid[x1, y1].Colour : null;
        }

        // ------------------------------------------------------------ Swapping

        /// <summary>Exchanges the pieces in cells a and b. Doesn't check the move is legal.</summary>
        public void Swap(Vector2Int a, Vector2Int b)
        {
            Piece pieceA = grid[a.x, a.y];
            grid[a.x, a.y] = grid[b.x, b.y];
            grid[b.x, b.y] = pieceA;
        }

        // ------------------------------------------------------------ Matching

        /// <summary>
        /// Finds every horizontal and vertical run of 3+ same-colour pieces. A piece's power makes
        /// no difference, and a piece with no colour is never part of a run. Each run is returned
        /// separately, so an L or T shape comes back as two runs that share a cell (see
        /// <see cref="FindMatchGroups"/> for the shapes put back together). Rows come
        /// first (bottom to top), then columns (left to right); within a run, cells are in order
        /// left to right or bottom to top.
        /// </summary>
        public List<List<Vector2Int>> FindMatches()
        {
            var runs = new List<List<Vector2Int>>();

            // Every row, scanned left to right...
            for (int y = 0; y < Height; y++)
            {
                FindRunsInLine(new Vector2Int(0, y), Vector2Int.right, Width, runs);
            }

            // ...and every column, scanned bottom to top.
            for (int x = 0; x < Width; x++)
            {
                FindRunsInLine(new Vector2Int(x, 0), Vector2Int.up, Height, runs);
            }

            return runs;
        }

        /// <summary>
        /// Walks one row or column (starting at <paramref name="start"/> and moving by
        /// <paramref name="step"/> for <paramref name="length"/> cells) and adds each run of
        /// 3+ same-type tokens to <paramref name="runs"/>.
        /// </summary>
        private void FindRunsInLine(Vector2Int start, Vector2Int step, int length, List<List<Vector2Int>> runs)
        {
            // runStart is the index where the current run of one type began. We go one step past
            // the end (i == length) so the last run in the line gets checked too.
            int runStart = 0;
            for (int i = 1; i <= length; i++)
            {
                if (i < length)
                {
                    // Empty and colourless cells never match anything, not even each other.
                    if (PieceAt(start + step * i).Matches(PieceAt(start + step * runStart)))
                    {
                        continue; // Same type as the run: it keeps growing.
                    }
                }

                // The run covering indices runStart..i-1 just ended. Keep it if it's long enough.
                // (A run starting on an empty or colourless cell always ends after 1 cell, so it
                // never counts.)
                if (i - runStart >= MinMatchLength)
                {
                    var run = new List<Vector2Int>();
                    for (int j = runStart; j < i; j++)
                    {
                        run.Add(start + step * j);
                    }
                    runs.Add(run);
                }
                runStart = i;
            }
        }

        /// <summary>
        /// Finds every match on the board as a whole shape: the runs from
        /// <see cref="FindMatches"/>, with runs that share a cell (an L, T or +) merged into one
        /// <see cref="MatchGroup"/>. Runs that merely sit next to each other stay separate.
        /// Each group knows its shape and its anchor cell. The board isn't changed.
        ///
        /// Groups are in the order of their first run, and each group's runs are in the order
        /// <see cref="FindMatches"/> found them.
        /// </summary>
        /// <param name="swappedA">One of the two cells the player just swapped. Leave both null for a cascade.</param>
        /// <param name="swappedB">The other swapped cell.</param>
        public List<MatchGroup> FindMatchGroups(Vector2Int? swappedA = null, Vector2Int? swappedB = null)
        {
            List<List<Vector2Int>> runs = FindMatches();

            // groupOf[i] is the label of the group run i belongs to. Every run starts out alone,
            // labelled with its own index. When two runs turn out to share a cell, everything
            // carrying one label is relabelled with the other, joining the two groups. The
            // smaller label is the one kept, so a group's label is always the index of its
            // first run.
            var groupOf = new int[runs.Count];
            for (int i = 0; i < runs.Count; i++)
            {
                groupOf[i] = i;
            }

            // The first run found on each cell. A cell can be in at most two runs (one along
            // its row, one up its column), so one entry per cell is enough to spot a crossing.
            var runAt = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < runs.Count; i++)
            {
                foreach (Vector2Int cell in runs[i])
                {
                    if (!runAt.TryGetValue(cell, out int other))
                    {
                        runAt[cell] = i;
                        continue;
                    }

                    int keep = Mathf.Min(groupOf[i], groupOf[other]);
                    int drop = Mathf.Max(groupOf[i], groupOf[other]);
                    for (int j = 0; j < runs.Count; j++)
                    {
                        if (groupOf[j] == drop)
                        {
                            groupOf[j] = keep;
                        }
                    }
                }
            }

            var groups = new List<MatchGroup>();
            for (int first = 0; first < runs.Count; first++)
            {
                // Only a group's first run still has its own index as its label.
                if (groupOf[first] != first)
                {
                    continue;
                }

                var groupRuns = new List<List<Vector2Int>>();
                for (int i = first; i < runs.Count; i++)
                {
                    if (groupOf[i] == first)
                    {
                        groupRuns.Add(runs[i]);
                    }
                }
                groups.Add(new MatchGroup(TypeAt(runs[first][0]), groupRuns, swappedA, swappedB));
            }
            return groups;
        }

        // ------------------------------------------------------------ Valid moves

        /// <summary>
        /// True if swapping some pair of neighboring tokens would make a match. Meant for a
        /// settled board (one with no matches on it already).
        /// </summary>
        public bool HasValidMove()
        {
            // `out _` means "this method hands something back here, but I don't need it".
            return TryFindValidMove(out _, out _);
        }

        /// <summary>
        /// Looks for a swap that would make a match, and if there is one, returns true with the
        /// two cells to swap in <paramref name="a"/> and <paramref name="b"/>. The board is left
        /// exactly as it was.
        /// </summary>
        // This is a "brute-force" search: rather than being clever about which swaps could
        // work, try every one of them. Each swap is a "what if": make it, ask FindMatches (the
        // same code the real game uses, so the two can never disagree), then undo it. An 8x8
        // board only has 112 possible swaps, so trying them all takes no time at all.
        //
        // `out` parameters are extra return values: the method must assign them before it
        // returns, and the caller gets them back.
        public bool TryFindValidMove(out Vector2Int a, out Vector2Int b)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    foreach (Vector2Int direction in SwapDirections)
                    {
                        if (SwapWouldMatch(cell, cell + direction))
                        {
                            a = cell;
                            b = cell + direction;
                            return true;
                        }
                    }
                }
            }

            // `default` is a type's "zero" value: (0, 0) for a Vector2Int.
            a = default;
            b = default;
            return false;
        }

        /// <summary>
        /// True if swapping the tokens in <paramref name="a"/> and <paramref name="b"/> would
        /// leave a match on the board. Nothing is changed.
        /// </summary>
        private bool SwapWouldMatch(Vector2Int a, Vector2Int b)
        {
            if (!IsInBounds(a) || !IsInBounds(b))
            {
                return false;
            }

            Piece pieceA = PieceAt(a);
            Piece pieceB = PieceAt(b);
            // An empty cell can't be swapped, and swapping two of the same colour changes nothing.
            if (pieceA.IsEmpty || pieceB.IsEmpty || pieceA.Colour == pieceB.Colour)
            {
                return false;
            }

            Swap(a, b);
            bool matches = FindMatches().Count > 0;
            Swap(a, b);
            return matches;
        }

        // ------------------------------------------------------------ Shuffling

        /// <summary>
        /// Rearranges the tokens already on the board (the same number of each type, in new
        /// cells) so that there are no matches and at least one valid move. Returns where every
        /// token went, or null, with the board untouched, if no such arrangement was found.
        /// </summary>
        public List<Move> Shuffle()
        {
            // Remember the board as it is now: it's where the tokens come from, and what to put
            // back if this doesn't work out. Clone copies the array (a plain assignment would
            // just be a second name for the same one).
            var original = (Piece[,])grid.Clone();

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                List<Move> moves = DealFromBag(original);
                if (moves != null && HasValidMove())
                {
                    return moves;
                }
            }

            System.Array.Copy(original, grid, original.Length);
            return null;
        }

        /// <summary>
        /// One shuffle attempt: puts every token of <paramref name="original"/> into "the bag",
        /// then fills the board the way <see cref="FillWithoutMatches"/> does, except each cell's
        /// token is drawn from the bag instead of being any type we like. Returns null if it gets
        /// stuck: a cell where everything left in the bag would make a match.
        /// </summary>
        private List<Move> DealFromBag(Piece[,] original)
        {
            // The bag holds cells rather than types, so we know which token (not just which
            // type) lands where, and the Board can slide that very token across.
            var bag = new List<Vector2Int>();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (!original[x, y].IsEmpty)
                    {
                        bag.Add(new Vector2Int(x, y));
                    }
                }
            }

            var moves = new List<Move>();
            var candidates = new List<int>();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    // Empty cells stay empty.
                    if (original[x, y].IsEmpty)
                    {
                        continue;
                    }

                    // Same rule as PickTypeWithoutMatch: the cells to the left and below were
                    // dealt earlier in this attempt, so they're safe to look at.
                    TokenType forbiddenHorizontal = ColourOfPair(x - 1, y, x - 2, y);
                    TokenType forbiddenVertical = ColourOfPair(x, y - 1, x, y - 2);

                    // The places in the bag holding a token that's allowed here. One with no
                    // colour can't make a match, so it's allowed anywhere.
                    candidates.Clear();
                    for (int i = 0; i < bag.Count; i++)
                    {
                        TokenType type = original[bag[i].x, bag[i].y].Colour;
                        if (type == null || (type != forbiddenHorizontal && type != forbiddenVertical))
                        {
                            candidates.Add(i);
                        }
                    }
                    if (candidates.Count == 0)
                    {
                        return null;
                    }

                    int picked = candidates[random.Next(candidates.Count)];
                    Vector2Int from = bag[picked];
                    bag.RemoveAt(picked);
                    grid[x, y] = original[from.x, from.y];
                    moves.Add(new Move(from, new Vector2Int(x, y)));
                }
            }
            return moves;
        }

        // ------------------------------------------------------------ Clearing, gravity, refill

        /// <summary>
        /// Every cell in <paramref name="runs"/>, with the length of the longest run it's part
        /// of. A cell shared by an L or T shape's two runs appears once, with the longer length.
        /// </summary>
        // `static` because it only looks at the runs it's given, not at this board's grid.
        public static Dictionary<Vector2Int, int> LongestRunPerCell(List<List<Vector2Int>> runs)
        {
            // A Dictionary maps keys (cells) to values (lengths), with each key stored only once.
            var lengths = new Dictionary<Vector2Int, int>();
            foreach (List<Vector2Int> run in runs)
            {
                foreach (Vector2Int cell in run)
                {
                    // TryGetValue returns false (and 0) if the cell isn't in the dictionary yet.
                    lengths.TryGetValue(cell, out int longestSoFar);
                    lengths[cell] = Mathf.Max(longestSoFar, run.Count);
                }
            }
            return lengths;
        }

        /// <summary>
        /// The power gem a match of <paramref name="shape"/> leaves behind at its anchor, or
        /// <see cref="PowerKind.None"/> if it leaves nothing. A line of exactly 4 makes a Flame,
        /// and an L, T or + makes a Star.
        /// </summary>
        public static PowerKind PowerMadeBy(MatchShape shape)
        {
            // A switch picks one branch by comparing a value against each `case`. `default` is
            // the branch for everything not listed.
            switch (shape)
            {
                case MatchShape.Line4: return PowerKind.Flame;
                case MatchShape.Cross: return PowerKind.Star;
                default: return PowerKind.None;
            }
        }

        /// <summary>
        /// The cells a Flame in <paramref name="centre"/> blasts: the 3x3 block around it,
        /// including <paramref name="centre"/> itself, minus whatever falls off the edge of the
        /// board. So 9 cells in the middle, 6 against an edge and 4 in a corner. Listed column by
        /// column, bottom to top.
        /// </summary>
        public List<Vector2Int> FlameBlastCells(Vector2Int centre)
        {
            var cells = new List<Vector2Int>();
            for (int x = centre.x - 1; x <= centre.x + 1; x++)
            {
                for (int y = centre.y - 1; y <= centre.y + 1; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (IsInBounds(cell))
                    {
                        cells.Add(cell);
                    }
                }
            }
            return cells;
        }

        /// <summary>
        /// The cells a Star in <paramref name="centre"/> blasts: its whole row and its whole
        /// column, including <paramref name="centre"/> itself (once). That is always
        /// Width + Height - 1 cells, wherever the Star is: unlike a Flame's blast, nothing is
        /// lost at an edge. The row is listed first, left to right, then the rest of the column,
        /// bottom to top.
        /// </summary>
        public List<Vector2Int> StarBlastCells(Vector2Int centre)
        {
            var cells = new List<Vector2Int>();
            for (int x = 0; x < Width; x++)
            {
                cells.Add(new Vector2Int(x, centre.y));
            }
            for (int y = 0; y < Height; y++)
            {
                // The row already listed the centre.
                if (y != centre.y)
                {
                    cells.Add(new Vector2Int(centre.x, y));
                }
            }
            return cells;
        }

        /// <summary>
        /// The cells a gem with <paramref name="power"/> blasts when it goes off in
        /// <paramref name="cell"/>: see <see cref="FlameBlastCells"/> and
        /// <see cref="StarBlastCells"/>. Empty for a power that has no blast.
        /// </summary>
        // This is the one place that knows which power has which blast. ClearMatches asks here
        // rather than checking for each kind of gem itself, so a new kind of blast is a new
        // case here and nothing else in the clear step has to change.
        public List<Vector2Int> BlastCells(PowerKind power, Vector2Int cell)
        {
            switch (power)
            {
                case PowerKind.Flame: return FlameBlastCells(cell);
                case PowerKind.Star: return StarBlastCells(cell);
                default: return new List<Vector2Int>();
            }
        }

        /// <summary>
        /// One whole clear step, done in a fixed order:
        ///  1. every cell of every group in <paramref name="groups"/> is emptied, except that a
        ///     group whose shape makes a power gem (see <see cref="PowerMadeBy"/>) gets that gem,
        ///     in the group's colour, in its anchor cell instead;
        ///  2. every power gem that was among the matched cells then detonates, emptying the
        ///     cells its blast covers (see <see cref="BlastCells"/>): the 3x3 around a Flame, the
        ///     row and column of a Star.
        /// Gravity is not part of it: call <see cref="Collapse"/> afterwards, so nothing falls
        /// between a match and its blast.
        ///
        /// A gem made in step 1 is never touched by step 2: it wasn't on the board when the
        /// match happened, so it neither goes off nor gets blasted away. That holds even when a
        /// power gem is itself the anchor of a match that makes one: the old gem goes off and
        /// the new one is left in its place.
        ///
        /// A blast only removes what it hits. A power gem caught in one is simply cleared, it
        /// doesn't go off in turn.
        /// </summary>
        /// <param name="groups">The matches to clear, as <see cref="FindMatchGroups"/> returned them.</param>
        public ClearResult ClearMatches(IReadOnlyList<MatchGroup> groups)
        {
            var matched = new List<Vector2Int>();
            var created = new List<Creation>();
            // Where this step's new gems are, so the blasts below can leave them alone.
            var createdCells = new HashSet<Vector2Int>();
            // The power gems found among the matched cells, and what each one was. They're only
            // noted here and set off after every group has been cleared, so the result doesn't
            // depend on which group happened to be looked at first.
            var gemCells = new List<Vector2Int>();
            var gemPieces = new List<Piece>();

            // Two groups never share a cell (FindMatchGroups would have merged them), so each
            // matched cell is visited exactly once.
            foreach (MatchGroup group in groups)
            {
                PowerKind made = PowerMadeBy(group.Shape);
                foreach (Vector2Int cell in group.Cells)
                {
                    Piece piece = grid[cell.x, cell.y];
                    // Any power at all: a matched gem always goes off. (A Hypercube can't get
                    // here, because a piece with no colour is never part of a match.)
                    if (piece.Power != PowerKind.None)
                    {
                        gemCells.Add(cell);
                        gemPieces.Add(piece);
                    }

                    if (made != PowerKind.None && cell == group.Anchor)
                    {
                        var gem = new Piece(group.Colour, made);
                        grid[cell.x, cell.y] = gem;
                        created.Add(new Creation(cell, gem));
                        createdCells.Add(cell);
                    }
                    else
                    {
                        grid[cell.x, cell.y] = Piece.Empty;
                        matched.Add(cell);
                    }
                }
            }

            var detonations = new List<Detonation>();
            for (int i = 0; i < gemCells.Count; i++)
            {
                var blasted = new List<Vector2Int>();
                foreach (Vector2Int cell in BlastCells(gemPieces[i].Power, gemCells[i]))
                {
                    // Already empty means the match (or an earlier blast) cleared it: there's
                    // nothing left there for this blast to remove.
                    if (grid[cell.x, cell.y].IsEmpty || createdCells.Contains(cell))
                    {
                        continue;
                    }
                    grid[cell.x, cell.y] = Piece.Empty;
                    blasted.Add(cell);
                }
                detonations.Add(new Detonation(gemCells[i], gemPieces[i], blasted));
            }

            return new ClearResult(matched, created, detonations);
        }

        /// <summary>
        /// Applies gravity: in every column, tokens drop into the empty cells below them, so all
        /// the gaps end up at the top. A token moves whole, so its power goes with it. Returns
        /// each token that moved, in the order they were moved (column by column, bottom to top). Replaying the list in that order is always
        /// safe: each destination is already empty when its token arrives.
        /// </summary>
        public List<Fall> Collapse()
        {
            var falls = new List<Fall>();
            for (int x = 0; x < Width; x++)
            {
                // Walk up the column moving each token down to the lowest free cell. `nextFree`
                // is that cell; it only moves up once a token has landed in it.
                int nextFree = 0;
                for (int y = 0; y < Height; y++)
                {
                    Piece piece = grid[x, y];
                    if (piece.IsEmpty)
                    {
                        continue;
                    }

                    if (y != nextFree)
                    {
                        grid[x, nextFree] = piece;
                        grid[x, y] = Piece.Empty;
                        falls.Add(new Fall(new Vector2Int(x, y), new Vector2Int(x, nextFree), piece));
                    }
                    nextFree++;
                }
            }
            return falls;
        }

        /// <summary>
        /// Puts a new ordinary piece of a random type in every empty cell and returns what was placed. Meant to run
        /// right after <see cref="Collapse"/>, when each column's gaps are all at the top. Unlike
        /// the starting fill, refills are allowed to create matches: those become cascades,
        /// which is part of the fun.
        /// </summary>
        public List<Spawn> Refill()
        {
            var spawns = new List<Spawn>();
            for (int x = 0; x < Width; x++)
            {
                int empty = 0;
                for (int y = 0; y < Height; y++)
                {
                    if (grid[x, y].IsEmpty)
                    {
                        empty++;
                    }
                }

                // Every new token in the column falls as far as the gap is tall. Stacked above
                // the board in the same order, they arrive together as a neat column.
                for (int y = 0; y < Height; y++)
                {
                    if (grid[x, y].IsEmpty)
                    {
                        grid[x, y] = new Piece(tokenTypes[random.Next(tokenTypes.Count)]);
                        spawns.Add(new Spawn(new Vector2Int(x, y), grid[x, y], empty));
                    }
                }
            }
            return spawns;
        }
    }
}
