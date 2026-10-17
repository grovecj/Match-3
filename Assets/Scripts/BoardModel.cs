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
        /// A power gem that went off: because it was part of a match, because the player
        /// swapped it (a Hypercube), or because another gem's blast caught it.
        /// <see cref="Piece"/> is the gem as it was (so its colour is still known after it's
        /// gone), <see cref="Cell"/> is where it was, and <see cref="Blasted"/> lists the cells
        /// its blast emptied.
        /// </summary>
        public readonly struct Detonation
        {
            public readonly Vector2Int Cell;
            public readonly Piece Piece;
            /// <summary>
            /// The cells this blast emptied, each one a piece that was NOT part of a match.
            /// Cells the match had already cleared aren't listed, and nor is a cell an earlier
            /// detonation in the same step got to first, so no cell is ever listed twice.
            /// A power gem among them goes off in the next wave: see <see cref="ClearResult.Waves"/>.
            /// </summary>
            public readonly IReadOnlyList<Vector2Int> Blasted;
            /// <summary>
            /// The colour a Hypercube cleared. Null for a Flame or Star, whose blast is a shape
            /// rather than a colour (and for a Hypercube that found no coloured gem to pick).
            /// </summary>
            public readonly TokenType Target;
            /// <summary>
            /// For the gem that carries a combo swap's blast (see <see cref="ActivateCombo"/>), the
            /// combo it set off: its <see cref="Blasted"/> is the combined blast. That's the gem
            /// the player moved, except in a <see cref="PowerCombo.Conversion"/>, where it's
            /// always the Hypercube. None otherwise.
            /// </summary>
            public readonly PowerCombo Combo;
            /// <summary>
            /// True for the other gem of a combo swap: it was used up by the combo, so it blasts
            /// nothing of its own (<see cref="Blasted"/> is empty).
            /// </summary>
            public readonly bool IsComboPartner;

            public Detonation(Vector2Int cell, Piece piece, IReadOnlyList<Vector2Int> blasted, TokenType target = null,
                PowerCombo combo = PowerCombo.None, bool isComboPartner = false)
            {
                Cell = cell;
                Piece = piece;
                Blasted = blasted;
                Target = target;
                Combo = combo;
                IsComboPartner = isComboPartner;
            }

            /// <summary>Which power gem went off: shorthand for <c>Piece.Power</c>.</summary>
            public PowerKind Kind => Piece.Power;
        }

        /// <summary>
        /// Everything one clear step did to the board, split into the three things the
        /// <see cref="Board"/> shows separately: gems that matched, power gems that appeared, and
        /// gems caught in a blast. <see cref="ClearMatches"/> and
        /// <see cref="ActivateHypercube"/> make these, as do <see cref="ActivateCombo"/> and <see cref="DetonatePowerGem"/>.
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

            /// <summary>
            /// The power gems this step set off, grouped into the waves of the chain reaction.
            /// The first wave is every gem the match (or the player's Hypercube swap) set off
            /// directly. Each later wave is every gem caught in a blast of the wave before it.
            /// Empty if nothing went off; never contains an empty wave.
            /// </summary>
            // A list of lists: the outer one is the waves in order, each inner one the
            // detonations of that wave in the order they happened.
            public IReadOnlyList<IReadOnlyList<Detonation>> Waves { get; }

            /// <summary>
            /// Every detonation of every wave as one flat list, in the order they happened. Handy
            /// when only "what went off" matters and not "in which wave".
            /// </summary>
            public IReadOnlyList<Detonation> Detonations { get; }

            /// <summary>
            /// The ordinary gems a <see cref="PowerCombo.Conversion"/> turned into power gems
            /// before anything went off, each with the piece it became. They weren't made by a
            /// match, so they aren't in <see cref="Created"/>: they go off (in the second wave)
            /// without ever counting as made. Empty for every other step.
            /// </summary>
            public IReadOnlyList<Creation> Converted { get; }

            public ClearResult(IReadOnlyList<Vector2Int> matched, IReadOnlyList<Creation> created,
                IReadOnlyList<IReadOnlyList<Detonation>> waves, IReadOnlyList<Creation> converted = null)
            {
                Matched = matched;
                Created = created;
                Waves = waves;
                // `??` means "the left side, unless it's null, then the right side".
                Converted = converted ?? new List<Creation>();

                var detonations = new List<Detonation>();
                foreach (IReadOnlyList<Detonation> wave in waves)
                {
                    detonations.AddRange(wave);
                }
                Detonations = detonations;
            }
        }

        /// <summary>
        /// A power gem that has been set off but hasn't blasted anything yet: it's waiting for
        /// its wave. <see cref="Target"/> is only for a Hypercube the player swapped, whose
        /// colour was chosen by the swap; null means "work it out when the gem goes off".
        /// <see cref="Reach"/>, if set, is the cells its blast covers, worked out by whoever set
        /// it off; null means "the usual blast for its power". <see cref="Combo"/> and
        /// <see cref="IsComboPartner"/> are only for the two gems of a combo swap: see
        /// <see cref="Detonation"/>.
        /// </summary>
        // `private`: only ResolveWaves and its callers need this. By the time a gem is pending
        // it's already off the grid, which is why what it was has to be remembered here.
        private readonly struct PendingGem
        {
            public readonly Vector2Int Cell;
            public readonly Piece Piece;
            public readonly TokenType Target;
            public readonly PowerCombo Combo;
            public readonly bool IsComboPartner;
            public readonly List<Vector2Int> Reach;

            public PendingGem(Vector2Int cell, Piece piece, TokenType target = null,
                PowerCombo combo = PowerCombo.None, bool isComboPartner = false, List<Vector2Int> reach = null)
            {
                Cell = cell;
                Piece = piece;
                Target = target;
                Combo = combo;
                IsComboPartner = isComboPartner;
                Reach = reach;
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
        /// True if some pair of neighboring tokens can be swapped: the swap would make a match,
        /// set off a Hypercube (see <see cref="IsHypercubeSwap"/>), or set off a combo (see
        /// <see cref="IsComboSwap"/>). Meant for a settled board (one with no matches on it already).
        /// </summary>
        public bool HasValidMove()
        {
            // `out _` means "this method hands something back here, but I don't need it".
            return TryFindValidMove(out _, out _);
        }

        /// <summary>
        /// Looks for a swap the player is allowed to make (one that makes a match or sets off a
        /// Hypercube), and if there is one, returns true with the two cells to swap in
        /// <paramref name="a"/> and <paramref name="b"/>. The board is left exactly as it was.
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
                        if (IsValidSwap(cell, cell + direction))
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
        /// True if the player may swap the tokens in <paramref name="a"/> and
        /// <paramref name="b"/>: doing so would leave a match on the board, or it's a Hypercube
        /// swap or a combo swap. Nothing is changed.
        /// </summary>
        // The rule used to be just "a swap is valid only if it makes a run". The Hypercube broke
        // that, so the rule became "a run, OR a Hypercube swap"; combos made it "a run, OR a
        // Hypercube swap, OR a combo swap". Each time the new case was one more line here and
        // nowhere else: everything that asks "can the player move?" (hints, the no-moves check,
        // shuffling) goes through this method, so none of it had to change.
        private bool IsValidSwap(Vector2Int a, Vector2Int b)
        {
            if (!IsInBounds(a) || !IsInBounds(b))
            {
                return false;
            }

            // Checked first: neither of these needs a run, so there's nothing to try out.
            if (IsComboSwap(a, b) || IsHypercubeSwap(a, b))
            {
                return true;
            }

            Piece pieceA = PieceAt(a);
            Piece pieceB = PieceAt(b);
            // An empty cell can't be swapped, and swapping two of the same colour changes
            // nothing. (Two Hypercubes never get this far: they're a combo.)
            if (pieceA.IsEmpty || pieceB.IsEmpty || pieceA.Colour == pieceB.Colour)
            {
                return false;
            }

            Swap(a, b);
            bool matches = FindMatches().Count > 0;
            Swap(a, b);
            return matches;
        }

        /// <summary>
        /// True if exactly one of cells <paramref name="a"/> and <paramref name="b"/> holds a
        /// Hypercube and the other holds a coloured piece. Swapping those two is always a valid
        /// move, even though it makes no run: it sets the Hypercube off (see
        /// <see cref="ActivateHypercube"/>). Two Hypercubes don't count, and nor does an empty
        /// cell. It doesn't matter which way round the cells are given, or whether the swap has
        /// been made yet. Doesn't check that the cells are neighbors.
        ///
        /// A Hypercube and a Flame or Star are a Hypercube swap AND a combo swap: the combo wins
        /// (see <see cref="IsComboSwap"/>), so ask about that first.
        /// </summary>
        public bool IsHypercubeSwap(Vector2Int a, Vector2Int b)
        {
            Piece pieceA = PieceAt(a);
            Piece pieceB = PieceAt(b);
            // A coloured piece can be an ordinary gem or a Flame or Star: all that matters is
            // that it has a colour for the Hypercube to take.
            return (pieceA.Power == PowerKind.Hypercube && pieceB.Colour != null)
                || (pieceB.Power == PowerKind.Hypercube && pieceA.Colour != null);
        }

        /// <summary>
        /// True if cells <paramref name="a"/> and <paramref name="b"/> each hold a power gem
        /// (whatever their colours) and the pair makes a combo: see <see cref="ComboFor"/>.
        /// Swapping those two is always a valid move: it sets off one bigger blast (see
        /// <see cref="ActivateCombo"/>). Like
        /// <see cref="IsHypercubeSwap"/>, the order of the cells doesn't matter, it can be asked
        /// before or after the swap, and it doesn't check that the cells are neighbors.
        /// </summary>
        public bool IsComboSwap(Vector2Int a, Vector2Int b)
        {
            return ComboFor(PieceAt(a).Power, PieceAt(b).Power) != PowerCombo.None;
        }

        /// <summary>
        /// The combo two power gems of <paramref name="a"/> and <paramref name="b"/> make when
        /// swapped together: two Hypercubes make a <see cref="PowerCombo.ClearBoard"/>, a
        /// Hypercube and a Flame or Star a <see cref="PowerCombo.Conversion"/>, two Flames a
        /// <see cref="PowerCombo.BigFlame"/>, and any other pair with a Star in it a
        /// <see cref="PowerCombo.StarCross"/>. Anything with an ordinary gem or an empty cell in
        /// it makes no combo.
        /// </summary>
        // The one place that knows which pairs combine and into what. The Hypercube cases come
        // first because a Hypercube is the strongest partner: with one in the pair, the other
        // gem's kind only decides between "the whole board" and "a whole colour".
        public static PowerCombo ComboFor(PowerKind a, PowerKind b)
        {
            if (a == PowerKind.Hypercube && b == PowerKind.Hypercube)
            {
                return PowerCombo.ClearBoard;
            }
            if (a == PowerKind.Hypercube || b == PowerKind.Hypercube)
            {
                // The one that isn't the Hypercube.
                PowerKind other = a == PowerKind.Hypercube ? b : a;
                return other == PowerKind.Flame || other == PowerKind.Star ? PowerCombo.Conversion : PowerCombo.None;
            }

            bool aCombines = a == PowerKind.Flame || a == PowerKind.Star;
            bool bCombines = b == PowerKind.Flame || b == PowerKind.Star;
            if (!aCombines || !bCombines)
            {
                return PowerCombo.None;
            }
            return a == PowerKind.Flame && b == PowerKind.Flame ? PowerCombo.BigFlame : PowerCombo.StarCross;
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
        /// an L, T or + makes a Star, and anything with a line of 5 or more in it makes a
        /// Hypercube.
        /// </summary>
        public static PowerKind PowerMadeBy(MatchShape shape)
        {
            // A switch picks one branch by comparing a value against each `case`. `default` is
            // the branch for everything not listed.
            switch (shape)
            {
                case MatchShape.Line4: return PowerKind.Flame;
                case MatchShape.Cross: return PowerKind.Star;
                case MatchShape.Line5: return PowerKind.Hypercube;
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
            return SquareCells(centre, 1);
        }

        /// <summary>
        /// The cells a <see cref="PowerCombo.BigFlame"/> centred on <paramref name="centre"/>
        /// blasts: the 5x5 block around it, clipped at the edges like a Flame's 3x3. So 25 cells
        /// in the middle and 9 in a corner. Listed column by column, bottom to top.
        /// </summary>
        public List<Vector2Int> BigFlameBlastCells(Vector2Int centre)
        {
            return SquareCells(centre, 2);
        }

        /// <summary>
        /// The square of cells reaching <paramref name="radius"/> cells out from
        /// <paramref name="centre"/> in every direction, minus whatever is off the board.
        /// Column by column, bottom to top.
        /// </summary>
        private List<Vector2Int> SquareCells(Vector2Int centre, int radius)
        {
            var cells = new List<Vector2Int>();
            for (int x = centre.x - radius; x <= centre.x + radius; x++)
            {
                for (int y = centre.y - radius; y <= centre.y + radius; y++)
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
        /// The cells a <see cref="PowerCombo.StarCross"/> centred on <paramref name="centre"/>
        /// blasts: the three whole rows and three whole columns through the 3x3 around it. A row
        /// or column that would be off the board is simply left out, so at an edge it's two of
        /// each. Every cell is listed once: the rows first (bottom to top, each left to right),
        /// then whatever the columns add (left to right, each bottom to top).
        /// </summary>
        public List<Vector2Int> StarCrossBlastCells(Vector2Int centre)
        {
            var cells = new List<Vector2Int>();
            for (int y = centre.y - 1; y <= centre.y + 1; y++)
            {
                if (y < 0 || y >= Height)
                {
                    continue;
                }
                for (int x = 0; x < Width; x++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }

            for (int x = centre.x - 1; x <= centre.x + 1; x++)
            {
                if (x < 0 || x >= Width)
                {
                    continue;
                }
                for (int y = 0; y < Height; y++)
                {
                    // The rows already listed the cells where this column crosses them.
                    if (Mathf.Abs(y - centre.y) > 1)
                    {
                        cells.Add(new Vector2Int(x, y));
                    }
                }
            }
            return cells;
        }

        /// <summary>
        /// The cells <paramref name="combo"/> blasts around <paramref name="centre"/>: see
        /// <see cref="BigFlameBlastCells"/> and <see cref="StarCrossBlastCells"/>, and every
        /// cell on the board for a <see cref="PowerCombo.ClearBoard"/>. Empty for
        /// <see cref="PowerCombo.None"/>, and for a <see cref="PowerCombo.Conversion"/>, which
        /// clears a colour rather than a shape (see <see cref="CellsOfColour"/>).
        /// </summary>
        // The combo counterpart of BlastCells: the one place that knows each combo's shape.
        public List<Vector2Int> ComboBlastCells(PowerCombo combo, Vector2Int centre)
        {
            switch (combo)
            {
                case PowerCombo.BigFlame: return BigFlameBlastCells(centre);
                case PowerCombo.StarCross: return StarCrossBlastCells(centre);
                // A square big enough to reach every edge from wherever the centre is.
                case PowerCombo.ClearBoard: return SquareCells(centre, Mathf.Max(Width, Height));
                default: return new List<Vector2Int>();
            }
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
        /// <see cref="StarBlastCells"/>. Empty for a Hypercube: what it clears depends on a
        /// colour, not on where it is (see <see cref="CellsOfColour"/>).
        /// </summary>
        // This is the one place that knows which power has which shape of blast. ResolveWaves
        // asks here rather than checking for each kind of gem itself, so a new shape of blast is
        // a new case here and nothing else in the clear step has to change.
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
        /// Every cell holding a piece of <paramref name="colour"/>, power gems included: what a
        /// Hypercube set off on that colour clears. Listed column by column from the left, each
        /// bottom to top. Empty if <paramref name="colour"/> is null.
        /// </summary>
        public List<Vector2Int> CellsOfColour(TokenType colour)
        {
            var cells = new List<Vector2Int>();
            if (colour == null)
            {
                return cells;
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    // Colour, not Matches: there are no runs here, just "is it that colour?".
                    if (grid[x, y].Colour == colour)
                    {
                        cells.Add(new Vector2Int(x, y));
                    }
                }
            }
            return cells;
        }

        /// <summary>
        /// The colour most pieces on the board have right now, or null if no piece has a colour.
        /// This is what a Hypercube caught in a blast goes off on: nobody swapped it with
        /// anything, so it picks for itself. A tie goes to whichever of the tied colours comes
        /// first in the board's list of token types, so the answer is never down to chance.
        /// </summary>
        public TokenType MostCommonColour()
        {
            var counts = new Dictionary<TokenType, int>();
            foreach (Piece piece in grid)
            {
                if (piece.Colour != null)
                {
                    counts.TryGetValue(piece.Colour, out int soFar);
                    counts[piece.Colour] = soFar + 1;
                }
            }

            // Walking the types in their fixed order and only switching on a strictly bigger
            // count is what breaks ties: the earliest type to reach the top count keeps it.
            TokenType best = null;
            int bestCount = 0;
            foreach (TokenType type in tokenTypes)
            {
                if (counts.TryGetValue(type, out int count) && count > bestCount)
                {
                    best = type;
                    bestCount = count;
                }
            }
            return best;
        }

        /// <summary>
        /// One whole clear step, done in a fixed order:
        ///  1. every cell of every group in <paramref name="groups"/> is emptied, except that a
        ///     group whose shape makes a power gem (see <see cref="PowerMadeBy"/>) gets that gem
        ///     in its anchor cell instead: in the group's colour, unless it's a Hypercube, which
        ///     has none;
        ///  2. every power gem that was among the matched cells then detonates, emptying the
        ///     cells its blast covers (see <see cref="BlastCells"/>): the 3x3 around a Flame, the
        ///     row and column of a Star;
        ///  3. every power gem one of those blasts caught detonates too, then every gem those
        ///     blasts caught, and so on until a round of blasts catches no more: a chain
        ///     reaction (see <see cref="ClearResult.Waves"/>).
        /// Gravity is not part of it: call <see cref="Collapse"/> afterwards, so nothing falls
        /// until the whole chain is over.
        ///
        /// A gem made in step 1 is never touched by steps 2 and 3: it wasn't on the board when
        /// the match happened, so it neither goes off nor gets blasted away. That holds even
        /// when a power gem is itself the anchor of a match that makes one: the old gem goes off
        /// and the new one is left in its place.
        ///
        /// A Hypercube caught in a blast has no neighbour to take a colour from, so it goes off
        /// on the <see cref="MostCommonColour"/> at that moment.
        /// </summary>
        /// <param name="groups">The matches to clear, as <see cref="FindMatchGroups"/> returned them.</param>
        public ClearResult ClearMatches(IReadOnlyList<MatchGroup> groups)
        {
            var matched = new List<Vector2Int>();
            var created = new List<Creation>();
            // Where this step's new gems are, so the blasts below can leave them alone.
            var createdCells = new HashSet<Vector2Int>();
            // The power gems found among the matched cells: the chain's first wave. They're only
            // noted here and set off after every group has been cleared, so the result doesn't
            // depend on which group happened to be looked at first.
            var firstWave = new List<PendingGem>();

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
                        firstWave.Add(new PendingGem(cell, piece));
                    }

                    if (made != PowerKind.None && cell == group.Anchor)
                    {
                        // A Flame or Star keeps the colour of the match that made it. A Hypercube
                        // doesn't: the five gems are gone and what's left has no colour at all.
                        Piece gem = made == PowerKind.Hypercube ? Piece.Hypercube : new Piece(group.Colour, made);
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

            return new ClearResult(matched, created, ResolveWaves(firstWave, createdCells));
        }

        /// <summary>
        /// Sets off the Hypercube in <paramref name="cube"/> on <paramref name="colour"/>: the
        /// Hypercube itself and every piece of that colour, wherever it is on the board, are
        /// emptied. A Flame or Star of that colour goes off as it's cleared, and so does anything
        /// its blast catches: the same chain reaction as in <see cref="ClearMatches"/>.
        ///
        /// This is a clear step of its own, in place of <see cref="ClearMatches"/>: no runs are
        /// looked for, so nothing is matched and no power gem is made. The result's first wave
        /// is the Hypercube alone, and its <see cref="Detonation.Blasted"/> lists the coloured
        /// cells it cleared (not <paramref name="cube"/>), column by column from the left, each
        /// bottom to top. As after any clear step, call <see cref="Collapse"/> next.
        /// </summary>
        /// <param name="cube">The cell the Hypercube is in now (after the swap, if it was swapped).</param>
        /// <param name="colour">The colour of the gem it was swapped with.</param>
        public ClearResult ActivateHypercube(Vector2Int cube, TokenType colour)
        {
            return ActivateHypercube(cube, colour, PowerCombo.None, null);
        }

        /// <summary>
        /// <see cref="ActivateHypercube(Vector2Int, TokenType)"/>, with the cube's detonation
        /// tagged as <paramref name="combo"/> and <paramref name="converted"/> passed through to
        /// the result: what a <see cref="PowerCombo.Conversion"/> needs once it has changed the
        /// colour's gems.
        /// </summary>
        // Two methods with the same name and different parameters are "overloads": C# picks the
        // one whose parameters fit the call. The public one stays as simple as it always was.
        private ClearResult ActivateHypercube(Vector2Int cube, TokenType colour, PowerCombo combo,
            List<Creation> converted)
        {
            // Throwing on a call that makes no sense is better than quietly clearing the wrong
            // thing: the mistake shows up where it was made.
            if (PieceAt(cube).Power != PowerKind.Hypercube)
            {
                throw new System.ArgumentException($"There is no Hypercube in cell ({cube.x},{cube.y}).");
            }
            if (colour == null)
            {
                throw new System.ArgumentNullException(nameof(colour), "A Hypercube needs a colour to clear.");
            }

            // The cube comes off the board here, as a matched gem does in ClearMatches, and
            // becomes a first wave of one. Unlike a cube caught in a blast, its colour is given.
            grid[cube.x, cube.y] = Piece.Empty;
            var firstWave = new List<PendingGem> { new PendingGem(cube, Piece.Hypercube, colour, combo) };

            // No match, so nothing was matched or made, and no cell needs protecting.
            return new ClearResult(new List<Vector2Int>(), new List<Creation>(),
                ResolveWaves(firstWave, new HashSet<Vector2Int>()), converted);
        }

        /// <summary>
        /// Sets off the combo of the two power gems the player just swapped (see
        /// <see cref="IsComboSwap"/>): both gems are emptied, and one blast of the combo's shape
        /// (see <see cref="ComboBlastCells"/>), centred on <paramref name="moved"/>, empties
        /// everything else it covers. Power gems it catches go off in the next wave, the same
        /// chain reaction as in <see cref="ClearMatches"/>.
        ///
        /// Like <see cref="ActivateHypercube"/> this is a clear step of its own: nothing is
        /// matched or made. The result's first wave is two detonations: the moved gem, carrying
        /// the whole blast (its <see cref="Detonation.Combo"/> says which), then the partner,
        /// which blasts nothing of its own (<see cref="Detonation.IsComboPartner"/>). Neither
        /// gem's own cell is in any <see cref="Detonation.Blasted"/> list, so neither is scored
        /// as a blasted gem. Call <see cref="Collapse"/> afterwards.
        ///
        /// Two Hypercubes (<see cref="PowerCombo.ClearBoard"/>) work the same way, with every
        /// cell of the board as the blast: every other piece is destroyed, and any power gem
        /// among them goes off in the next wave with nothing left to hit.
        ///
        /// A Hypercube and a Flame or Star (<see cref="PowerCombo.Conversion"/>) work
        /// differently: see <see cref="ActivateConversion"/>.
        /// </summary>
        /// <param name="moved">
        /// Where the gem the player moved is now, after the swap. The blast is centred here.
        /// </param>
        /// <param name="partner">Where the gem it was swapped with is now.</param>
        public ClearResult ActivateCombo(Vector2Int moved, Vector2Int partner)
        {
            Piece movedGem = PieceAt(moved);
            Piece partnerGem = PieceAt(partner);
            PowerCombo combo = ComboFor(movedGem.Power, partnerGem.Power);
            if (combo == PowerCombo.None)
            {
                throw new System.ArgumentException(
                    $"Cells ({moved.x},{moved.y}) and ({partner.x},{partner.y}) don't hold a pair of power gems that combine.");
            }
            if (combo == PowerCombo.Conversion)
            {
                // Which gem the player moved makes no difference here: the cube always goes off.
                return movedGem.Power == PowerKind.Hypercube
                    ? ActivateConversion(moved, partner)
                    : ActivateConversion(partner, moved);
            }

            // Both gems come off the board before anything blasts, so the blast passes over
            // their cells (they're empty) and neither is counted as a blasted gem.
            grid[moved.x, moved.y] = Piece.Empty;
            grid[partner.x, partner.y] = Piece.Empty;
            var firstWave = new List<PendingGem>
            {
                // The moved gem carries the whole combined blast; the partner's is part of it,
                // so it reaches nothing of its own.
                new PendingGem(moved, movedGem, combo: combo, reach: ComboBlastCells(combo, moved)),
                new PendingGem(partner, partnerGem, isComboPartner: true, reach: new List<Vector2Int>()),
            };
            return new ClearResult(new List<Vector2Int>(), new List<Creation>(),
                ResolveWaves(firstWave, new HashSet<Vector2Int>()));
        }

        /// <summary>
        /// A Hypercube swapped with a Flame or Star (<see cref="PowerCombo.Conversion"/>). Two
        /// steps, in this order:
        ///  1. every ordinary gem of the partner's colour becomes a gem of the partner's power
        ///     (see <see cref="ConvertColour"/>); a Flame or Star of that colour keeps its own;
        ///  2. the Hypercube goes off on that colour, exactly as <see cref="ActivateHypercube"/>
        ///     does: the cube is the first wave (tagged with the combo), every gem of the colour,
        ///     partner included, is a blasted gem, and they all go off in the next wave.
        /// The result's <see cref="ClearResult.Converted"/> lists the gems step 1 changed.
        /// </summary>
        // The whole combo is "change the board, then run something that already exists". Nothing
        // in ResolveWaves knows about conversions: by the time the cube goes off, the colour's
        // gems simply ARE Flames (or Stars), and a Flame caught in a blast goes off. That's the
        // pipeline doing what it always did.
        //
        // Does it end, however many gems turn into power gems? Yes, for the reason ResolveWaves
        // gives: every gem that goes off is taken off the board first, so there can never be
        // more detonations than cells.
        private ClearResult ActivateConversion(Vector2Int cube, Vector2Int partner)
        {
            Piece partnerGem = PieceAt(partner);
            List<Creation> converted = ConvertColour(partnerGem.Colour, partnerGem.Power);
            return ActivateHypercube(cube, partnerGem.Colour, PowerCombo.Conversion, converted);
        }

        /// <summary>
        /// Gives every ordinary piece of <paramref name="colour"/> the power
        /// <paramref name="power"/>, and returns what changed (column by column from the left,
        /// each bottom to top). A piece that already has a power keeps it and isn't listed.
        /// </summary>
        private List<Creation> ConvertColour(TokenType colour, PowerKind power)
        {
            var converted = new List<Creation>();
            foreach (Vector2Int cell in CellsOfColour(colour))
            {
                if (grid[cell.x, cell.y].Power != PowerKind.None)
                {
                    continue;
                }
                var piece = new Piece(colour, power);
                grid[cell.x, cell.y] = piece;
                converted.Add(new Creation(cell, piece));
            }
            return converted;
        }

        /// <summary>
        /// Looks for a power gem on the board, row by row from the bottom, each row left to
        /// right: so the bottom-left corner is looked at first and the top-right corner last.
        /// Returns false (and <paramref name="cell"/> is meaningless) if there isn't one.
        /// The Last Hurrah at the end of a round sets the gems off in this order.
        /// </summary>
        // A fixed order, rather than "whichever the loop happens to find", is what makes the
        // end of a round the same every time for the same board, and so testable.
        public bool TryFindPowerGem(out Vector2Int cell)
        {
            // y is the outer loop here (unlike most loops in this class) because the order is
            // by row: all of row 0, then all of row 1, and so on.
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (grid[x, y].Power != PowerKind.None)
                    {
                        cell = new Vector2Int(x, y);
                        return true;
                    }
                }
            }

            // An `out` parameter must be given a value on every path, even when it isn't used.
            cell = default;
            return false;
        }

        /// <summary>
        /// Sets off the power gem in <paramref name="cell"/> where it sits, with no match and no
        /// swap: what the Last Hurrah does to every gem still on the board when time runs out.
        /// The gem is emptied and blasts as it always does, chain reaction included (see
        /// <see cref="ClearMatches"/>). A Hypercube has nothing to take a colour from, so, like
        /// one caught in a blast, it goes off on the <see cref="MostCommonColour"/>.
        ///
        /// Like <see cref="ActivateHypercube"/> this is a clear step of its own: nothing is
        /// matched or made, and the result's first wave is this gem alone. Call
        /// <see cref="Collapse"/> afterwards.
        /// </summary>
        public ClearResult DetonatePowerGem(Vector2Int cell)
        {
            Piece gem = PieceAt(cell);
            if (gem.Power == PowerKind.None)
            {
                throw new System.ArgumentException($"There is no power gem in cell ({cell.x},{cell.y}).");
            }

            // The same three lines as ActivateHypercube, minus the colour: leaving the pending
            // gem's Target out is what tells ResolveWaves "pick the most common one".
            grid[cell.x, cell.y] = Piece.Empty;
            var firstWave = new List<PendingGem> { new PendingGem(cell, gem) };
            return new ClearResult(new List<Vector2Int>(), new List<Creation>(),
                ResolveWaves(firstWave, new HashSet<Vector2Int>()));
        }

        /// <summary>
        /// Sets off the power gems in <paramref name="firstWave"/>, then every power gem their
        /// blasts caught, then every gem those blasts caught, until a wave catches none. Returns
        /// what went off, wave by wave. Cells in <paramref name="protectedCells"/> are never
        /// blasted.
        /// </summary>
        // This is a "breadth-first" walk: everything one step away from the start is dealt with
        // before anything two steps away. `current` is the queue of gems to set off now, and
        // each gem they catch joins `next`, the queue for the wave after.
        //
        // Two things have to be true of a loop like this, or a board full of power gems would
        // be trouble:
        //  - No gem goes off twice. A walk like this normally keeps a "visited" set for that.
        //    Here the grid itself does the job: a gem is taken off the board at the moment it's
        //    caught, so a second blast reaching the same cell finds it empty and passes over it.
        //  - It ends. Every pending gem came from a cell that is empty from then on, and nothing
        //    refills a cell in here, so there can never be more detonations than there are cells.
        private List<List<Detonation>> ResolveWaves(List<PendingGem> firstWave, HashSet<Vector2Int> protectedCells)
        {
            var waves = new List<List<Detonation>>();
            List<PendingGem> current = firstWave;
            while (current.Count > 0)
            {
                var wave = new List<Detonation>();
                var next = new List<PendingGem>();
                foreach (PendingGem gem in current)
                {
                    // A Hypercube clears a colour; a Flame or Star clears a shape around itself.
                    TokenType target = null;
                    List<Vector2Int> reach;
                    if (gem.Reach != null)
                    {
                        // Whoever set this gem off already said what it blasts (a combo).
                        reach = gem.Reach;
                    }
                    else if (gem.Piece.Power == PowerKind.Hypercube)
                    {
                        // `??` means "the left side, unless it's null, then the right side". The
                        // colour is counted now, when the cube goes off, not when it was caught:
                        // the gems that went off before it have changed what's left.
                        target = gem.Target ?? MostCommonColour();
                        reach = CellsOfColour(target);
                    }
                    else
                    {
                        reach = BlastCells(gem.Piece.Power, gem.Cell);
                    }

                    var blasted = new List<Vector2Int>();
                    foreach (Vector2Int cell in reach)
                    {
                        Piece hit = grid[cell.x, cell.y];
                        // Already empty means the match (or an earlier blast) cleared it: there's
                        // nothing left there for this blast to remove.
                        if (hit.IsEmpty || protectedCells.Contains(cell))
                        {
                            continue;
                        }

                        grid[cell.x, cell.y] = Piece.Empty;
                        blasted.Add(cell);
                        if (hit.Power != PowerKind.None)
                        {
                            next.Add(new PendingGem(cell, hit));
                        }
                    }
                    wave.Add(new Detonation(gem.Cell, gem.Piece, blasted, target, gem.Combo, gem.IsComboPartner));
                }

                waves.Add(wave);
                current = next;
            }
            return waves;
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
