using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Owns the play area: its dimensions, the cell background tiles, the tokens in each cell,
    /// the rules for resolving a swap (matching, clearing, gravity, refilling), and the math
    /// for converting between grid coordinates (column, row) and world positions.
    ///
    /// Grid convention: (0, 0) is the bottom-left cell, x grows to the right, y grows upward.
    /// The board is centered on this GameObject's position.
    /// </summary>
    public class Board : MonoBehaviour
    {
        // [SerializeField] exposes a private field in the Inspector without making it public to other code.
        [Header("Dimensions")]
        [SerializeField, Min(1)] private int width = 8;
        [SerializeField, Min(1)] private int height = 8;
        [SerializeField, Min(0.1f)] private float cellSize = 1f;

        [Header("Cell Visuals")]
        [Tooltip("Prefab with a SpriteRenderer, spawned once per cell as the background tile.")]
        [SerializeField] private SpriteRenderer cellPrefab;
        [SerializeField] private Color lightCellColor = new Color(0.28f, 0.28f, 0.36f);
        [SerializeField] private Color darkCellColor = new Color(0.20f, 0.20f, 0.27f);

        [Header("Tokens")]
        [Tooltip("Token prefab spawned into each cell. The Board assigns its type at runtime.")]
        [SerializeField] private Token tokenPrefab;
        [Tooltip("The kinds of token that can appear on the board. Needs at least 3 distinct types to avoid starting matches.")]
        [SerializeField] private TokenType[] tokenTypes;

        [Header("Swapping")]
        [Tooltip("How long, in seconds, two tokens take to trade places.")]
        [SerializeField, Min(0.01f)] private float swapDuration = 0.15f;

        [Header("Resolving")]
        [Tooltip("Pause, in seconds, after matched tokens disappear and before the rest fall.")]
        [SerializeField, Min(0f)] private float clearDelay = 0.1f;
        [Tooltip("How fast falling tokens move, in cells per second.")]
        [SerializeField, Min(0.1f)] private float fallSpeed = 10f;

        /// <summary>
        /// Fired once for every run of matching tokens that gets cleared, with the run's length
        /// (3, 4, 5...). Scoring can listen to this without the Board knowing about score.
        /// </summary>
        // An event is a list of callbacks other scripts can subscribe to with `+=` (and
        // unsubscribe from with `-=`). Action<int> means "a method taking one int and returning
        // nothing". It's written System.Action because adding `using System;` would make
        // `Random` ambiguous between System.Random and UnityEngine.Random.
        public event System.Action<int> MatchCleared;

        // The smallest run of same-type tokens that counts as a match.
        private const int MinMatchLength = 3;

        // Idle: waiting for the player. Resolving: a swap and everything it caused (clearing,
        // falling, refilling, cascades) is still playing out, so input has to wait.
        private enum BoardState { Idle, Resolving }

        private Transform cellContainer;
        private Transform tokenContainer;
        private bool needsRebuild;
        private BoardState state = BoardState.Idle;

        // The "model": which token is in which cell, indexed [x, y]. This array is the source of
        // truth for game logic. The Token GameObjects are the "view": they just display it.
        // Keeping the two separate means rules like matching can work on plain data without
        // asking the scene where things are.
        private Token[,] tokens;

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;

        /// <summary>World-space size of the whole board.</summary>
        public Vector2 WorldSize => new Vector2(width * cellSize, height * cellSize);

        /// <summary>
        /// True while a swap and its consequences (clears, falls, cascades) are playing out.
        /// Input should be ignored until it's false.
        /// </summary>
        public bool IsResolving => state == BoardState.Resolving;

        /// <summary>The token in cell (x, y), or null if the cell is empty or out of bounds.</summary>
        public Token GetToken(int x, int y)
        {
            if (tokens == null || !IsInBounds(new Vector2Int(x, y)))
            {
                return null;
            }
            return tokens[x, y];
        }

        // Start runs once, before the first frame, after every object's Awake has run.
        private void Start()
        {
            Rebuild();
        }

        // OnValidate runs in the Editor whenever a value changes in the Inspector.
        // Unity doesn't allow creating/destroying objects inside OnValidate, so we just
        // set a flag and do the rebuild on the next Update.
        private void OnValidate()
        {
            needsRebuild = true;
        }

        private void Update()
        {
            if (needsRebuild && Application.isPlaying)
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            needsRebuild = false;
            // A swap in progress would keep moving tokens we're about to destroy, so cancel it.
            StopAllCoroutines();
            state = BoardState.Idle;
            BuildCells();
            FillWithRandomTokens();
        }

        /// <summary>Destroys any existing cell tiles and spawns a fresh set for the current dimensions.</summary>
        private void BuildCells()
        {
            if (cellPrefab == null)
            {
                Debug.LogError("Board: no Cell Prefab assigned.", this);
                return;
            }

            // Keep tiles grouped under one child object so the Hierarchy stays tidy
            // and rebuilding is just "destroy the container".
            if (cellContainer != null)
            {
                Destroy(cellContainer.gameObject);
            }
            cellContainer = new GameObject("Cells").transform;
            cellContainer.SetParent(transform, false);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    SpriteRenderer cell = Instantiate(cellPrefab, GridToWorld(x, y), Quaternion.identity, cellContainer);
                    cell.name = $"Cell ({x}, {y})";
                    cell.transform.localScale = Vector3.one * cellSize;
                    // Checkerboard: alternate colors like a chess board.
                    cell.color = (x + y) % 2 == 0 ? darkCellColor : lightCellColor;
                }
            }
        }

        /// <summary>
        /// Destroys any existing tokens and puts a new token of a random type in every cell,
        /// avoiding any 3-in-a-row so the game doesn't start with free matches.
        /// </summary>
        private void FillWithRandomTokens()
        {
            // Same "container" trick as the cells: one child object to hold (and clear) all tokens.
            // Clear the old container and rebuild the `tokens` array before validating the
            // replacement configuration below, so a bad config still leaves `tokens` correctly
            // sized for the current dimensions (all null) instead of a stale, wrongly-sized array
            // from before a dimension change.
            if (tokenContainer != null)
            {
                Destroy(tokenContainer.gameObject);
            }
            tokenContainer = new GameObject("Tokens").transform;
            tokenContainer.SetParent(transform, false);
            tokens = new Token[width, height];

            if (tokenPrefab == null || CountDistinctTypes(tokenTypes) < 3)
            {
                Debug.LogError("Board: assign a Token Prefab and at least 3 distinct Token Types.", this);
                return;
            }

            // Fill column by column, bottom to top. That order matters: by the time we reach
            // (x, y), the two cells to its left and the two cells below it are already filled,
            // which is exactly what PickTypeWithoutMatch needs to look at.
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    SpawnToken(x, y, PickTypeWithoutMatch(x, y), GridToWorld(x, y));
                }
            }
        }

        /// <summary>
        /// Creates a token of <paramref name="type"/> at <paramref name="position"/> and records it
        /// as occupying cell (x, y). The position can differ from the cell's center (e.g. above
        /// the board) so the token can then animate into place.
        /// </summary>
        private void SpawnToken(int x, int y, TokenType type, Vector3 position)
        {
            // Instantiate clones the prefab into the scene. Passing a component (Token)
            // instead of a GameObject returns the clone's matching component directly.
            Token token = Instantiate(tokenPrefab, position, Quaternion.identity, tokenContainer);
            token.transform.localScale = Vector3.one * cellSize;
            token.SetType(type);
            PlaceToken(token, x, y);
        }

        /// <summary>
        /// Records <paramref name="token"/> as occupying cell (x, y) in the model. Only the model
        /// changes; moving the token on screen is left to the animations.
        /// </summary>
        private void PlaceToken(Token token, int x, int y)
        {
            tokens[x, y] = token;
            // Naming tokens after their cell makes the Hierarchy a handy live view of the model.
            token.name = $"Token ({x}, {y})";
        }

        /// <summary>
        /// Picks a random type for cell (x, y) that won't complete a 3-in-a-row with the two
        /// already-placed tokens to its left or the two below it.
        /// </summary>
        private TokenType PickTypeWithoutMatch(int x, int y)
        {
            // A type is forbidden if the two neighbors in a line are both that same type.
            TokenType forbiddenHorizontal = x >= 2 && tokens[x - 1, y].Type == tokens[x - 2, y].Type
                ? tokens[x - 1, y].Type
                : null;
            TokenType forbiddenVertical = y >= 2 && tokens[x, y - 1].Type == tokens[x, y - 2].Type
                ? tokens[x, y - 1].Type
                : null;

            // Collect the types that are still allowed, then pick one of them at random.
            // At most two types are forbidden, and FillWithRandomTokens guarantees at least
            // 3 distinct types, so there's always at least one candidate left.
            var candidates = new List<TokenType>();
            foreach (TokenType type in tokenTypes)
            {
                if (type != null && type != forbiddenHorizontal && type != forbiddenVertical)
                {
                    candidates.Add(type);
                }
            }

            // Random.Range with ints excludes the max, so this returns 0..Count-1: a valid index.
            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// A random type for refilling the board. Unlike the starting fill, refills are allowed
        /// to create matches: those become cascades, which is part of the fun.
        /// </summary>
        private TokenType PickRandomType()
        {
            var candidates = new List<TokenType>();
            foreach (TokenType type in tokenTypes)
            {
                if (type != null)
                {
                    candidates.Add(type);
                }
            }
            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>Number of distinct, non-null entries in <paramref name="types"/>.</summary>
        private static int CountDistinctTypes(TokenType[] types)
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

        /// <summary>
        /// Swaps the tokens in two orthogonally adjacent cells and resolves the result: if the
        /// swap makes a match, matches are cleared and the board refills (with cascades);
        /// otherwise the tokens swap back. Returns false (and does nothing) if the cells aren't
        /// valid neighbors or the board is still resolving a previous swap.
        /// </summary>
        public bool TrySwap(Vector2Int a, Vector2Int b)
        {
            // Manhattan distance of exactly 1 means "one step up/down/left/right", never diagonal.
            bool areNeighbors = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
            if (state != BoardState.Idle || !areNeighbors || !IsInBounds(a) || !IsInBounds(b))
            {
                return false;
            }

            if (tokens[a.x, a.y] == null || tokens[b.x, b.y] == null)
            {
                return false;
            }

            // Lock the board here, not inside the coroutine, so there's no gap in which a
            // second swap could sneak in.
            state = BoardState.Resolving;
            StartCoroutine(ResolveSwap(a, b));
            return true;
        }

        // A coroutine is a method that can pause itself with `yield return` and resume later.
        // `yield return StartCoroutine(...)` runs another coroutine and waits for it to finish
        // before continuing, which lets us write a sequence of animations top to bottom like
        // ordinary code instead of juggling timers or callbacks.
        /// <summary>
        /// One whole turn: swap, then either swap back (no match) or clear -> fall -> refill,
        /// repeating for as long as the falling tokens create new matches (cascades).
        /// </summary>
        private IEnumerator ResolveSwap(Vector2Int a, Vector2Int b)
        {
            // Update the model first, so matching sees the new layout; the animation just
            // catches the view up to it.
            SwapInModel(a, b);
            yield return StartCoroutine(AnimateSwap(a, b));

            List<List<Vector2Int>> runs = FindMatches();
            if (runs.Count == 0)
            {
                // Not a valid move: put everything back exactly as it was.
                SwapInModel(a, b);
                yield return StartCoroutine(AnimateSwap(a, b));
            }

            // Cascade loop: clear the current matches, let tokens fall and refill, then check
            // whether the new layout made more matches. Skipped entirely for an invalid swap.
            while (runs.Count > 0)
            {
                ClearMatches(runs);
                if (clearDelay > 0f)
                {
                    yield return new WaitForSeconds(clearDelay);
                }

                CollapseAndRefill();
                yield return StartCoroutine(AnimateFalls());

                runs = FindMatches();
            }

            state = BoardState.Idle;
        }

        /// <summary>Exchanges the tokens in cells a and b in the model.</summary>
        private void SwapInModel(Vector2Int a, Vector2Int b)
        {
            Token tokenA = tokens[a.x, a.y];
            Token tokenB = tokens[b.x, b.y];
            PlaceToken(tokenB, a.x, a.y);
            PlaceToken(tokenA, b.x, b.y);
        }

        // `yield return null` means "wait until the next frame", so the loop below runs one
        // step per frame, which is how we spread movement over time without blocking the game.
        /// <summary>
        /// Slides the tokens the model says are in cells a and b from wherever they are onto
        /// those cells' centers.
        /// </summary>
        private IEnumerator AnimateSwap(Vector2Int a, Vector2Int b)
        {
            Transform tokenA = tokens[a.x, a.y].transform;
            Transform tokenB = tokens[b.x, b.y].transform;
            Vector3 startA = tokenA.position;
            Vector3 startB = tokenB.position;
            Vector3 targetA = GridToWorld(a.x, a.y);
            Vector3 targetB = GridToWorld(b.x, b.y);

            for (float elapsed = 0f; elapsed < swapDuration; elapsed += Time.deltaTime)
            {
                // t goes 0 -> 1 over the duration. SmoothStep eases it so the motion starts
                // and ends gently instead of at constant speed.
                float t = Mathf.SmoothStep(0f, 1f, elapsed / swapDuration);
                tokenA.position = Vector3.Lerp(startA, targetA, t);
                tokenB.position = Vector3.Lerp(startB, targetB, t);
                yield return null;
            }

            // Snap exactly onto the targets; the loop may stop slightly short of t = 1.
            tokenA.position = targetA;
            tokenB.position = targetB;
        }

        /// <summary>
        /// Finds every horizontal and vertical run of 3+ same-type tokens. Each run is returned
        /// separately, so an L or T shape comes back as two runs that share a cell.
        /// </summary>
        private List<List<Vector2Int>> FindMatches()
        {
            var runs = new List<List<Vector2Int>>();

            // Every row, scanned left to right...
            for (int y = 0; y < height; y++)
            {
                FindRunsInLine(new Vector2Int(0, y), Vector2Int.right, width, runs);
            }

            // ...and every column, scanned bottom to top.
            for (int x = 0; x < width; x++)
            {
                FindRunsInLine(new Vector2Int(x, 0), Vector2Int.up, height, runs);
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
                    if (current != null && current == TypeAt(start + step * runStart))
                    {
                        continue; // Same type as the run: it keeps growing.
                    }
                }

                // The run covering indices runStart..i-1 just ended. Keep it if it's long enough.
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

        /// <summary>The type of the token in <paramref name="cell"/>, or null if the cell is empty.</summary>
        private TokenType TypeAt(Vector2Int cell)
        {
            Token token = tokens[cell.x, cell.y];
            return token != null ? token.Type : null;
        }

        /// <summary>
        /// Destroys every token in <paramref name="runs"/>, empties their cells, and reports each
        /// run through <see cref="MatchCleared"/>.
        /// </summary>
        private void ClearMatches(List<List<Vector2Int>> runs)
        {
            // Runs can overlap (L/T shapes), so gather their cells into a set first. A HashSet
            // ignores duplicates, which guarantees a shared cell is only cleared once.
            var cellsToClear = new HashSet<Vector2Int>();
            foreach (List<Vector2Int> run in runs)
            {
                cellsToClear.UnionWith(run);
            }

            foreach (Vector2Int cell in cellsToClear)
            {
                // Destroy removes the GameObject at the end of the current frame. We null the
                // model entry right away so game logic treats the cell as empty immediately.
                Destroy(tokens[cell.x, cell.y].gameObject);
                tokens[cell.x, cell.y] = null;
            }

            foreach (List<Vector2Int> run in runs)
            {
                // ?.Invoke calls every subscriber, or does nothing if nobody has subscribed.
                MatchCleared?.Invoke(run.Count);
            }
        }

        /// <summary>
        /// Updates the model so every column is full again: tokens drop into the empty cells
        /// below them, and new random tokens fill the gap left at the top. New tokens are spawned
        /// above the board; <see cref="AnimateFalls"/> then moves everything into place.
        /// </summary>
        private void CollapseAndRefill()
        {
            for (int x = 0; x < width; x++)
            {
                // Walk up the column moving each token down to the lowest free cell. `nextFree`
                // is that cell; it only moves up once a token has landed in it.
                int nextFree = 0;
                for (int y = 0; y < height; y++)
                {
                    Token token = tokens[x, y];
                    if (token == null)
                    {
                        continue;
                    }

                    if (y != nextFree)
                    {
                        tokens[x, y] = null;
                        PlaceToken(token, x, nextFree);
                    }
                    nextFree++;
                }

                // Cells nextFree..height-1 are now empty. Spawn a token for each one, stacked
                // above the board in the same order, so every new token falls exactly as far
                // as the gap is tall and they arrive together as a neat column.
                int missing = height - nextFree;
                for (int y = nextFree; y < height; y++)
                {
                    SpawnToken(x, y, PickRandomType(), GridToWorld(x, y + missing));
                }
            }
        }

        /// <summary>
        /// Moves every token toward its cell's center (per the model) at a constant speed, and
        /// finishes once they've all arrived.
        /// </summary>
        private IEnumerator AnimateFalls()
        {
            bool anyMoving = true;
            while (anyMoving)
            {
                anyMoving = false;
                // Distance to cover this frame. Multiplying by Time.deltaTime (seconds since the
                // last frame) keeps the speed the same regardless of frame rate.
                float stepDistance = fallSpeed * cellSize * Time.deltaTime;

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Transform token = tokens[x, y].transform;
                        Vector3 target = GridToWorld(x, y);
                        // MoveTowards steps toward the target but never overshoots, so tokens land
                        // exactly on their cell. Tokens that are already in place don't move.
                        token.position = Vector3.MoveTowards(token.position, target, stepDistance);
                        if (token.position != target)
                        {
                            anyMoving = true;
                        }
                    }
                }

                if (anyMoving)
                {
                    yield return null;
                }
            }
        }

        /// <summary>World position of the center of cell (x, y).</summary>
        public Vector3 GridToWorld(int x, int y)
        {
            // Offset from the board's bottom-left corner, then shift by half a cell to reach the center.
            Vector2 bottomLeft = (Vector2)transform.position - WorldSize / 2f;
            return new Vector3(
                bottomLeft.x + (x + 0.5f) * cellSize,
                bottomLeft.y + (y + 0.5f) * cellSize,
                transform.position.z);
        }

        /// <summary>
        /// Converts a world position to the grid cell containing it.
        /// Returns false if the position is outside the board.
        /// </summary>
        public bool TryWorldToGrid(Vector3 worldPosition, out Vector2Int cell)
        {
            Vector2 bottomLeft = (Vector2)transform.position - WorldSize / 2f;
            Vector2 local = (Vector2)worldPosition - bottomLeft;
            cell = new Vector2Int(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(local.y / cellSize));
            return IsInBounds(cell);
        }

        public bool IsInBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
        }

        // Gizmos are drawn only in the Scene view (not in the game). This outline lets you
        // see the board's footprint while editing, before pressing Play.
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, WorldSize);
        }
    }
}
