using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The board's game rules as plain data: which <see cref="TokenType"/> is in each cell, and
    /// how that changes when tokens are swapped, matched, cleared, and replaced. This is the
    /// "model"; <see cref="Board"/> is the "view + orchestration" that turns what changed here
    /// into GameObjects and animations.
    ///
    /// Unlike Board, this is not a MonoBehaviour: it's an ordinary C# class you create with
    /// `new`. It never touches the scene, so tests can build one from a few strings, call a
    /// method, and check the result, all without GameObjects, frames, or Play Mode.
    ///
    /// Grid convention (same as Board): (0, 0) is the bottom-left cell, x grows to the right,
    /// y grows upward. A null cell is empty.
    /// </summary>
    public class BoardModel
    {
        /// <summary>The smallest run of same-type tokens that counts as a match.</summary>
        public const int MinMatchLength = 3;

        /// <summary>
        /// A token moving down its column from <see cref="From"/> to <see cref="To"/> because
        /// the cells below it were cleared.
        /// </summary>
        // A struct is a small value type: it's copied on assignment, like an int or a Vector2Int.
        // `readonly` promises its fields never change after construction.
        public readonly struct Fall
        {
            public readonly Vector2Int From;
            public readonly Vector2Int To;

            public Fall(Vector2Int from, Vector2Int to)
            {
                From = from;
                To = to;
            }
        }

        /// <summary>
        /// A new token of <see cref="Type"/> placed in <see cref="Cell"/> to refill the board.
        /// <see cref="FallDistance"/> is how many cells above <see cref="Cell"/> it should appear
        /// so it can fall into place alongside the other new tokens in its column.
        /// </summary>
        public readonly struct Spawn
        {
            public readonly Vector2Int Cell;
            public readonly TokenType Type;
            public readonly int FallDistance;

            public Spawn(Vector2Int cell, TokenType type, int fallDistance)
            {
                Cell = cell;
                Type = type;
                FallDistance = fallDistance;
            }
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

        // How many random boards FillWithValidMove and Shuffle try before giving up. A try
        // nearly always works on a normal board; the cap is there so a board that can't have a
        // valid move at all (a 2x2, say) ends in "false" rather than an endless loop.
        private const int MaxAttempts = 100;

        // The two neighbors TryFindValidMove swaps each cell with. The left and lower neighbors
        // aren't needed: those swaps were already tried when the search visited those cells.
        private static readonly Vector2Int[] SwapDirections = { Vector2Int.right, Vector2Int.up };

        private readonly TokenType[,] grid;
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

            grid = new TokenType[width, height];
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

        /// <summary>The type in cell (x, y), or null if the cell is empty.</summary>
        public TokenType TypeAt(int x, int y)
        {
            return grid[x, y];
        }

        public TokenType TypeAt(Vector2Int cell)
        {
            return grid[cell.x, cell.y];
        }

        /// <summary>Puts <paramref name="type"/> in cell (x, y). Null empties the cell.</summary>
        public void SetTypeAt(int x, int y, TokenType type)
        {
            grid[x, y] = type;
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
        /// Puts a random type in every cell, avoiding any 3-in-a-row so the game doesn't start
        /// with free matches. Needs at least 3 distinct token types.
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
                    grid[x, y] = PickTypeWithoutMatch(x, y);
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
        /// Picks a random type for cell (x, y) that won't complete a 3-in-a-row with the two
        /// already-placed tokens to its left or the two below it.
        /// </summary>
        private TokenType PickTypeWithoutMatch(int x, int y)
        {
            // A type is forbidden if the two neighbors in a line are both that same type.
            TokenType forbiddenHorizontal = x >= 2 && grid[x - 1, y] == grid[x - 2, y] ? grid[x - 1, y] : null;
            TokenType forbiddenVertical = y >= 2 && grid[x, y - 1] == grid[x, y - 2] ? grid[x, y - 1] : null;

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

        // ------------------------------------------------------------ Swapping

        /// <summary>Exchanges the types in cells a and b. Doesn't check the move is legal.</summary>
        public void Swap(Vector2Int a, Vector2Int b)
        {
            TokenType typeA = grid[a.x, a.y];
            grid[a.x, a.y] = grid[b.x, b.y];
            grid[b.x, b.y] = typeA;
        }

        // ------------------------------------------------------------ Matching

        /// <summary>
        /// Finds every horizontal and vertical run of 3+ same-type tokens. Each run is returned
        /// separately, so an L or T shape comes back as two runs that share a cell. Rows come
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
                    TokenType current = TypeAt(start + step * i);
                    // Empty cells never match anything, not even each other.
                    if (current != null && current == TypeAt(start + step * runStart))
                    {
                        continue; // Same type as the run: it keeps growing.
                    }
                }

                // The run covering indices runStart..i-1 just ended. Keep it if it's long enough.
                // (A run starting on an empty cell always ends after 1 cell, so it never counts.)
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

            TokenType typeA = TypeAt(a);
            TokenType typeB = TypeAt(b);
            // An empty cell can't be swapped, and swapping two of the same type changes nothing.
            if (typeA == null || typeB == null || typeA == typeB)
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
            var original = (TokenType[,])grid.Clone();

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
        private List<Move> DealFromBag(TokenType[,] original)
        {
            // The bag holds cells rather than types, so we know which token (not just which
            // type) lands where, and the Board can slide that very token across.
            var bag = new List<Vector2Int>();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (original[x, y] != null)
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
                    if (original[x, y] == null)
                    {
                        continue;
                    }

                    // Same rule as PickTypeWithoutMatch: the cells to the left and below were
                    // dealt earlier in this attempt, so they're safe to look at.
                    TokenType forbiddenHorizontal = x >= 2 && grid[x - 1, y] == grid[x - 2, y] ? grid[x - 1, y] : null;
                    TokenType forbiddenVertical = y >= 2 && grid[x, y - 1] == grid[x, y - 2] ? grid[x, y - 1] : null;

                    // The places in the bag holding a token that's allowed here.
                    candidates.Clear();
                    for (int i = 0; i < bag.Count; i++)
                    {
                        TokenType type = original[bag[i].x, bag[i].y];
                        if (type != forbiddenHorizontal && type != forbiddenVertical)
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
        /// Empties every cell in <paramref name="runs"/> and returns the set of cells cleared.
        /// Runs can overlap (L/T shapes); a shared cell is only listed once.
        /// </summary>
        public HashSet<Vector2Int> Clear(List<List<Vector2Int>> runs)
        {
            // A HashSet ignores duplicates, which is exactly what overlapping runs need.
            var cleared = new HashSet<Vector2Int>();
            foreach (List<Vector2Int> run in runs)
            {
                cleared.UnionWith(run);
            }

            foreach (Vector2Int cell in cleared)
            {
                grid[cell.x, cell.y] = null;
            }
            return cleared;
        }

        /// <summary>
        /// Applies gravity: in every column, tokens drop into the empty cells below them, so all
        /// the gaps end up at the top. Returns each token that moved, in the order they were
        /// moved (column by column, bottom to top). Replaying the list in that order is always
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
                    TokenType type = grid[x, y];
                    if (type == null)
                    {
                        continue;
                    }

                    if (y != nextFree)
                    {
                        grid[x, nextFree] = type;
                        grid[x, y] = null;
                        falls.Add(new Fall(new Vector2Int(x, y), new Vector2Int(x, nextFree)));
                    }
                    nextFree++;
                }
            }
            return falls;
        }

        /// <summary>
        /// Puts a new random type in every empty cell and returns what was placed. Meant to run
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
                    if (grid[x, y] == null)
                    {
                        empty++;
                    }
                }

                // Every new token in the column falls as far as the gap is tall. Stacked above
                // the board in the same order, they arrive together as a neat column.
                for (int y = 0; y < Height; y++)
                {
                    if (grid[x, y] == null)
                    {
                        grid[x, y] = tokenTypes[random.Next(tokenTypes.Count)];
                        spawns.Add(new Spawn(new Vector2Int(x, y), grid[x, y], empty));
                    }
                }
            }
            return spawns;
        }
    }
}
