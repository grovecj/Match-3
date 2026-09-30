using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Owns the play area on screen: its dimensions, the cell background tiles, the Token
    /// GameObjects, the animations for a swap and everything it causes, and the math for
    /// converting between grid coordinates (column, row) and world positions.
    ///
    /// The rules themselves (matching, clearing, gravity, refilling) live in
    /// <see cref="BoardModel"/>. Board asks the model what changed, then makes the scene match.
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

        [Header("Theme")]
        [Tooltip("Supplies the cell colors and token style. The board redraws itself when the theme changes.")]
        [SerializeField] private ThemeManager themeManager;

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

        /// <summary>
        /// Fired once per clear step (each pass of the cascade loop), after that step's
        /// <see cref="MatchCleared"/> events, with the step's longest run and its cascade index
        /// (0 = the player's own swap, 1 = the first cascade, and so on).
        /// </summary>
        // MatchCleared fires once per *run*, so an L or T shape (two runs that share a cell)
        // fires it twice. That's right for scoring, but a sound played per run would play twice
        // on top of itself. Listeners that care about "something was cleared" rather than "how
        // many runs" (like MatchSounds) want exactly one call per step, so they get their own event.
        public event System.Action<int, int> ClearStepResolved;

        /// <summary>
        /// Fired when the player's swap starts animating, whether or not it will make a match.
        /// Only player swaps fire it: tokens falling during a refill or cascade never do.
        /// </summary>
        public event System.Action SwapStarted;

        /// <summary>
        /// Fired when a swap made no match and the tokens start sliding back. Always comes
        /// after a <see cref="SwapStarted"/>.
        /// </summary>
        public event System.Action SwapRejected;

        // Idle: waiting for the player. Resolving: a swap and everything it caused (clearing,
        // falling, refilling, cascades) is still playing out, so input has to wait.
        private enum BoardState { Idle, Resolving }

        private Transform cellContainer;
        // The cell background tiles, indexed [x, y], kept so a theme change can recolor them
        // in place instead of rebuilding the board.
        private SpriteRenderer[,] cells;
        private Transform tokenContainer;
        private bool needsRebuild;
        private BoardState state = BoardState.Idle;

        // The "model": which type of token is in which cell, plus the rules for changing that.
        // It's the source of truth for game logic. Null until the board has been filled.
        private BoardModel model;

        // The "view": the Token GameObject displaying each cell, indexed [x, y]. Every change
        // to the model is mirrored here so the two always agree about which cell holds what.
        private Token[,] tokens;

        // Every random choice the model makes comes from here. System.Random is plain C#, so
        // unlike most Unity APIs it's fine to create in a field initializer. Not `readonly`
        // only so tests can swap in a seeded one (see ConfigureForTests).
        private System.Random random = new System.Random();

        /// <summary>The kinds of token that can appear on the board (e.g. for previews in menus).</summary>
        public IReadOnlyList<TokenType> TokenTypes => tokenTypes;

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
            if (themeManager == null)
            {
                Debug.LogError("Board: no Theme Manager assigned, so cells and tokens have no colors or sprites.", this);
            }
            ValidateTokenStyle();
            Rebuild();
        }

        // OnEnable/OnDisable run when the component is switched on/off (including when the
        // object is created and destroyed). Subscribing in one and unsubscribing in the other
        // means the ThemeManager never calls back into a Board that no longer exists.
        private void OnEnable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged += HandleThemeChanged;
            }

            // While disabled we weren't listening, so the theme may have changed in the
            // meantime. If the board is already built, catch its visuals up now. (The very
            // first time, Start builds the board with the current theme instead.)
            if (cells != null)
            {
                HandleThemeChanged();
            }
        }

        private void OnDisable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged -= HandleThemeChanged;
            }
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

        /// <summary>
        /// Throws away the current board and deals a fresh one with no starting matches
        /// (e.g. when a new round starts).
        /// </summary>
        public void Regenerate()
        {
            Rebuild();
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
            cells = new SpriteRenderer[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    SpriteRenderer cell = Instantiate(cellPrefab, GridToWorld(x, y), Quaternion.identity, cellContainer);
                    cell.name = $"Cell ({x}, {y})";
                    cell.transform.localScale = Vector3.one * cellSize;
                    cells[x, y] = cell;
                }
            }
            ApplyCellColors();
        }

        /// <summary>
        /// Called by the ThemeManager when the style or theme changes. Only the visuals are
        /// updated: every token keeps its type and cell, so the game state is untouched.
        /// </summary>
        private void HandleThemeChanged()
        {
            ValidateTokenStyle();
            ApplyCellColors();

            if (tokens == null)
            {
                return;
            }
            TokenStyle style = CurrentTokenStyle;
            foreach (Token token in tokens)
            {
                // Cells are briefly empty between a clear and the refill.
                if (token != null)
                {
                    token.SetStyle(style);
                }
            }
        }

        /// <summary>Colors the cell tiles in a checkerboard using the current board theme.</summary>
        private void ApplyCellColors()
        {
            if (cells == null)
            {
                return;
            }

            BoardTheme theme = themeManager != null ? themeManager.BoardTheme : null;
            // GetLength(0) is the array's size in x, GetLength(1) its size in y.
            for (int x = 0; x < cells.GetLength(0); x++)
            {
                for (int y = 0; y < cells.GetLength(1); y++)
                {
                    if (cells[x, y] == null)
                    {
                        continue;
                    }
                    if (theme == null)
                    {
                        cells[x, y].color = Color.white;
                        continue;
                    }
                    // Checkerboard: alternate colors like a chess board.
                    cells[x, y].color = (x + y) % 2 == 0 ? theme.DarkCellColor : theme.LightCellColor;
                }
            }
        }

        /// <summary>The token style currently selected, or null if there's no ThemeManager.</summary>
        private TokenStyle CurrentTokenStyle => themeManager != null ? themeManager.TokenStyle : null;

        /// <summary>
        /// Logs an error if the current token style is missing a sprite for any type this board
        /// can spawn, since those tokens would be invisible.
        /// </summary>
        private void ValidateTokenStyle()
        {
            TokenStyle style = CurrentTokenStyle;
            if (style == null || tokenTypes == null)
            {
                return;
            }

            List<TokenType> missing = style.FindMissingTypes(tokenTypes);
            if (missing.Count > 0)
            {
                // ConvertAll builds a list of names; string.Join glues them together with commas.
                string names = string.Join(", ", missing.ConvertAll(type => type.name));
                Debug.LogError($"Board: token style '{style.name}' has no sprite for: {names}. " +
                               "Add an entry for each to the style asset.", style);
            }
        }

        /// <summary>
        /// Destroys any existing tokens and puts a new token of a random type in every cell,
        /// avoiding any 3-in-a-row so the game doesn't start with free matches.
        /// </summary>
        private void FillWithRandomTokens()
        {
            if (!ResetTokens())
            {
                return;
            }
            model.FillWithoutMatches();
            SpawnTokenInEveryCell();
        }

        /// <summary>
        /// Destroys any existing tokens and creates a new, empty model. Returns false (leaving
        /// no model) if the board isn't configured well enough to hold tokens.
        /// </summary>
        private bool ResetTokens()
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
            model = null;

            if (tokenPrefab == null || BoardModel.CountDistinctTypes(tokenTypes) < 3)
            {
                Debug.LogError("Board: assign a Token Prefab and at least 3 distinct Token Types.", this);
                return false;
            }

            model = new BoardModel(width, height, tokenTypes, random);
            return true;
        }

        /// <summary>Spawns a token on every cell's center showing the type the model has there.</summary>
        private void SpawnTokenInEveryCell()
        {
            // The model decides what goes where; we just spawn a token to show each cell.
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    SpawnToken(x, y, model.TypeAt(x, y), GridToWorld(x, y));
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
            token.SetStyle(CurrentTokenStyle);
            token.SetType(type);
            PlaceToken(token, x, y);
        }

        /// <summary>
        /// Records <paramref name="token"/> as occupying cell (x, y) in the view array. Its
        /// position on screen is left to the animations.
        /// </summary>
        private void PlaceToken(Token token, int x, int y)
        {
            tokens[x, y] = token;
            // Naming tokens after their cell makes the Hierarchy a handy live view of the model.
            token.name = $"Token ({x}, {y})";
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
            // Update the model (and view array) first, so matching sees the new layout; the
            // animation just catches the screen up to it.
            Swap(a, b);
            SwapStarted?.Invoke();
            yield return StartCoroutine(AnimateSwap(a, b));

            List<List<Vector2Int>> runs = model.FindMatches();
            if (runs.Count == 0)
            {
                // Not a valid move: put everything back exactly as it was.
                Swap(a, b);
                SwapRejected?.Invoke();
                yield return StartCoroutine(AnimateSwap(a, b));
            }

            // Cascade loop: clear the current matches, let tokens fall and refill, then check
            // whether the new layout made more matches. Skipped entirely for an invalid swap.
            // cascadeIndex counts the passes: 0 is the swap's own match, 1+ are cascades. It's
            // local to this turn, so every new swap starts counting from 0 again.
            for (int cascadeIndex = 0; runs.Count > 0; cascadeIndex++)
            {
                ClearMatches(runs, cascadeIndex);
                if (clearDelay > 0f)
                {
                    yield return new WaitForSeconds(clearDelay);
                }

                CollapseAndRefill();
                yield return StartCoroutine(AnimateFalls());

                runs = model.FindMatches();
            }

            state = BoardState.Idle;
        }

        /// <summary>Exchanges the contents of cells a and b in both the model and the view array.</summary>
        private void Swap(Vector2Int a, Vector2Int b)
        {
            model.Swap(a, b);
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
        /// Empties every cell in <paramref name="runs"/> in the model, destroys the tokens that
        /// were there, and reports each run through <see cref="MatchCleared"/>, then the step as
        /// a whole through <see cref="ClearStepResolved"/>.
        /// </summary>
        private void ClearMatches(List<List<Vector2Int>> runs, int cascadeIndex)
        {
            // The model hands back each cleared cell once, even where an L or T shape's two runs
            // share a cell, so no token gets destroyed twice.
            foreach (Vector2Int cell in model.Clear(runs))
            {
                // Destroy removes the GameObject at the end of the current frame. We null the
                // view entry right away so it agrees with the model immediately.
                Destroy(tokens[cell.x, cell.y].gameObject);
                tokens[cell.x, cell.y] = null;
            }

            int longestRun = 0;
            foreach (List<Vector2Int> run in runs)
            {
                // ?.Invoke calls every subscriber, or does nothing if nobody has subscribed.
                MatchCleared?.Invoke(run.Count);
                longestRun = Mathf.Max(longestRun, run.Count);
            }

            ClearStepResolved?.Invoke(longestRun, cascadeIndex);
        }

        /// <summary>
        /// Has the model apply gravity and refill, then mirrors that in the view: falling tokens
        /// are re-filed under their new cells, and new tokens are spawned above the board.
        /// <see cref="AnimateFalls"/> then moves everything into place on screen.
        /// </summary>
        private void CollapseAndRefill()
        {
            // Replaying the falls in the order the model made them is safe: each token's
            // destination cell has already been vacated by the time it gets there.
            foreach (BoardModel.Fall fall in model.Collapse())
            {
                Token token = tokens[fall.From.x, fall.From.y];
                tokens[fall.From.x, fall.From.y] = null;
                PlaceToken(token, fall.To.x, fall.To.y);
            }

            // Each new token starts FallDistance cells above its cell, so the new tokens in a
            // column are stacked just above the board and arrive together as a neat column.
            foreach (BoardModel.Spawn spawn in model.Refill())
            {
                Vector2Int cell = spawn.Cell;
                SpawnToken(cell.x, cell.y, spawn.Type, GridToWorld(cell.x, cell.y + spawn.FallDistance));
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

        // ---------------------------------------------------------------- Test hooks

        // `internal` means "visible only inside this assembly (Match3)". AssemblyInfo.cs adds
        // one exception with [InternalsVisibleTo]: the Play Mode tests, which need to build a
        // Board in code (no scene, no Inspector) and give it a known layout. The rest of the
        // game can't see these, so they don't clutter Board's public API.

        /// <summary>
        /// Sets everything a scene would normally assign in the Inspector. Call it while the
        /// Board's GameObject is still inactive, so it's in place before Start builds the board.
        /// </summary>
        internal void ConfigureForTests(int width, int height, SpriteRenderer cellPrefab, Token tokenPrefab,
            ThemeManager themeManager, TokenType[] tokenTypes, int seed,
            float swapDuration, float clearDelay, float fallSpeed)
        {
            this.width = width;
            this.height = height;
            this.cellPrefab = cellPrefab;
            this.tokenPrefab = tokenPrefab;
            this.themeManager = themeManager;
            this.tokenTypes = tokenTypes;
            // A seeded Random makes the same "random" choices (refills) on every run.
            random = new System.Random(seed);
            this.swapDuration = swapDuration;
            this.clearDelay = clearDelay;
            this.fallSpeed = fallSpeed;
        }

        /// <summary>
        /// Replaces the current tokens with <paramref name="layout"/> (indexed [x, y], the same
        /// size as the board), cancelling anything in progress. Call it after Start has run.
        /// </summary>
        internal void LoadLayoutForTests(TokenType[,] layout)
        {
            if (layout.GetLength(0) != width || layout.GetLength(1) != height)
            {
                throw new System.ArgumentException(
                    $"Layout is {layout.GetLength(0)}x{layout.GetLength(1)} but the board is {width}x{height}.");
            }

            StopAllCoroutines();
            state = BoardState.Idle;
            if (!ResetTokens())
            {
                return;
            }
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    model.SetTypeAt(x, y, layout[x, y]);
                }
            }
            SpawnTokenInEveryCell();
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
