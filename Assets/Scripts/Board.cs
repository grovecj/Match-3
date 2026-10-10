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
        [Tooltip("How long, in seconds, a dragged token takes to spring back when let go without swapping.")]
        [SerializeField, Min(0.01f)] private float springBackDuration = 0.2f;

        [Header("Resolving")]
        [Tooltip("Pause, in seconds, between matched tokens being cleared and the rest falling. The pop plays during it.")]
        [SerializeField, Min(0f)] private float clearDelay = 0.25f;
        [Tooltip("How fast falling tokens move, in cells per second.")]
        [SerializeField, Min(0.1f)] private float fallSpeed = 10f;

        [Header("Clearing Effects")]
        [Tooltip("How long, in seconds, a cleared token takes to pop (swell, then shrink away).")]
        [SerializeField, Min(0f)] private float popDuration = 0.12f;
        [Tooltip("Particle burst spawned on every cleared cell. Optional: without one, tokens just pop.")]
        [SerializeField] private Explosion explosionPrefab;

        [Header("Reshuffling")]
        [Tooltip("Pause, in seconds, between running out of moves and the tokens shuffling, so the player sees why.")]
        [SerializeField, Min(0f)] private float noMovesDelay = 0.5f;
        [Tooltip("How long, in seconds, the tokens take to slide to their new cells in a shuffle.")]
        [SerializeField, Min(0.01f)] private float shuffleDuration = 0.4f;

        /// <summary>How big a popping token gets at its largest, relative to its normal size.</summary>
        public const float PopPeakScale = 1.2f;

        // How far through the pop (0 to 1) the token is at its largest. Swelling is quick and
        // shrinking takes the rest, so it reads as a "pop" rather than a slow pulse.
        private const float PopPeakTime = 0.3f;

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

        /// <summary>
        /// Fired when the board has settled with no swap left that would make a match, just
        /// before it reshuffles itself.
        /// </summary>
        public event System.Action NoMovesLeft;

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

        // The token the player has picked up, if any. `Vector2Int?` is a "nullable" Vector2Int:
        // it can hold a cell or null, and HasValue says which. A plain struct can't be null.
        private Vector2Int? heldCell;
        // Where the held token has been dragged: one of the four directions (or zero), and how
        // far, in cells. The neighbor in that direction slides the other way to make room.
        private Vector2Int heldDirection;
        private float heldDistance;

        // The two tokens currently pulsing as a hint, or null when no hint is showing.
        private Token[] hintedTokens;

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

        /// <summary>
        /// True from the moment the board announces it has no moves left until its tokens have
        /// finished shuffling. The round clock stops for this: running out of moves is bad luck,
        /// and shouldn't also cost the player time.
        /// </summary>
        // `{ get; private set; }` is an "auto-property": C# makes the hidden field for us. Anyone
        // can read it, but only Board can change it.
        public bool IsReshuffling { get; private set; }

        /// <summary>True while two tokens are pulsing to suggest a move.</summary>
        public bool IsHintShowing => hintedTokens != null;

        /// <summary>True while the player has a token picked up (held or selected).</summary>
        public bool IsHolding => heldCell.HasValue;

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
            // The reshuffle coroutine was just stopped, so it will never get to clear this itself.
            IsReshuffling = false;
            heldCell = null;
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
        /// avoiding any 3-in-a-row so the game doesn't start with free matches, and making sure
        /// there's at least one move to play.
        /// </summary>
        private void FillWithRandomTokens()
        {
            if (!ResetTokens())
            {
                return;
            }
            if (!model.FillWithValidMove())
            {
                Debug.LogWarning($"Board: a {width}x{height} board with these token types has no valid moves.", this);
            }
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
            // The hinted tokens were in the old container, so they're gone too.
            hintedTokens = null;

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

        // ---------------------------------------------------------------- Holding and dragging
        //
        // The player picks a token up (TryPickUp), may drag it toward a neighbor (DragHeld), and
        // then either swaps it (TrySwap) or lets go (DropHeld). BoardInput decides which; the
        // Board just moves the tokens and keeps track of which one is held.

        /// <summary>
        /// Picks up the token in <paramref name="cell"/>: it grows a little and draws above its
        /// neighbors. Any token already held is put down first. Returns false (and does nothing)
        /// while the board is resolving, or if there's no token there.
        /// </summary>
        public bool TryPickUp(Vector2Int cell)
        {
            if (state != BoardState.Idle || GetToken(cell.x, cell.y) == null)
            {
                return false;
            }

            DropHeld();
            // DropHeld may have started a spring-back, which locks the board.
            if (state != BoardState.Idle)
            {
                return false;
            }

            // The player has found something to do; the hint has done its job (or been ignored).
            ClearHint();
            heldCell = cell;
            heldDirection = Vector2Int.zero;
            heldDistance = 0f;
            tokens[cell.x, cell.y].SetHighlight(Token.Highlight.Held);
            return true;
        }

        // ---------------------------------------------------------------- Hints
        //
        // BoardInput decides *when* the player could use a hint (they haven't touched the board
        // for a while); the Board decides *what* to show, because it has the model to ask.

        /// <summary>
        /// Makes the two tokens of a valid move pulse, replacing any hint already showing.
        /// Returns false (and shows nothing) while the board is resolving or a token is picked
        /// up, or if there's no valid move to show.
        /// </summary>
        public bool ShowHint()
        {
            if (state != BoardState.Idle || model == null || heldCell.HasValue)
            {
                return false;
            }

            ClearHint();
            // The same search that decides whether to reshuffle. It scans column by column from
            // the bottom-left, so on a board with several moves the hint is always the one in
            // the leftmost column (and the lowest of those).
            if (!model.TryFindValidMove(out Vector2Int a, out Vector2Int b))
            {
                return false;
            }

            hintedTokens = new[] { tokens[a.x, a.y], tokens[b.x, b.y] };
            foreach (Token token in hintedTokens)
            {
                token.SetHighlight(Token.Highlight.Hint);
            }
            return true;
        }

        /// <summary>Stops the hinted tokens pulsing. Does nothing if no hint is showing.</summary>
        public void ClearHint()
        {
            if (hintedTokens == null)
            {
                return;
            }

            foreach (Token token in hintedTokens)
            {
                // Leave a token alone if it has since been picked up: that's the player's
                // highlight now, not the hint's.
                if (token != null && token.CurrentHighlight == Token.Highlight.Hint)
                {
                    token.SetHighlight(Token.Highlight.None);
                }
            }
            hintedTokens = null;
        }

        /// <summary>
        /// Marks the held token as selected (it pulses, waiting for a click on a neighbor), or
        /// back to just held. Does nothing if no token is held.
        /// </summary>
        public void SetHeldSelected(bool selected)
        {
            if (heldCell is Vector2Int cell)
            {
                tokens[cell.x, cell.y].SetHighlight(selected ? Token.Highlight.Selected : Token.Highlight.Held);
            }
        }

        /// <summary>
        /// Moves the held token <paramref name="distance"/> cells in <paramref name="direction"/>
        /// (one of up/down/left/right, or zero to put it back on its cell). The neighbor it moves
        /// toward slides the opposite way by the same amount, up to a whole cell. With no
        /// neighbor there (the edge of the board) only the held token moves.
        /// </summary>
        public void DragHeld(Vector2Int direction, float distance)
        {
            if (!(heldCell is Vector2Int cell))
            {
                return;
            }

            // Changing direction (say, from right to up) leaves the old neighbor out of place,
            // so put it back before moving the new one.
            if (direction != heldDirection)
            {
                PutNeighborBack();
            }
            heldDirection = direction;
            heldDistance = direction == Vector2Int.zero ? 0f : Mathf.Max(0f, distance);

            // Vector3 * float scales a vector; (Vector3)(Vector2) turns a direction into a 3D offset.
            Vector3 offset = (Vector3)(Vector2)heldDirection * (heldDistance * cellSize);
            tokens[cell.x, cell.y].transform.position = GridToWorld(cell.x, cell.y) + offset;

            Vector2Int neighbor = cell + heldDirection;
            Token neighborToken = heldDirection == Vector2Int.zero ? null : GetToken(neighbor.x, neighbor.y);
            if (neighborToken != null)
            {
                // The neighbor never goes past the held token's cell.
                Vector3 neighborOffset = (Vector3)(Vector2)heldDirection * (Mathf.Min(heldDistance, 1f) * cellSize);
                neighborToken.transform.position = GridToWorld(neighbor.x, neighbor.y) - neighborOffset;
            }
        }

        /// <summary>
        /// Puts the held token down. If it was dragged, it (and the neighbor it pushed) spring
        /// back onto their cells, and the board is locked until they're home. Does nothing if
        /// no token is held.
        /// </summary>
        public void DropHeld()
        {
            if (!(heldCell is Vector2Int cell))
            {
                return;
            }

            heldCell = null;
            Token token = tokens[cell.x, cell.y];
            token.SetHighlight(Token.Highlight.None);

            if (heldDistance == 0f)
            {
                // Never moved off its cell, so there's nothing to animate.
                return;
            }

            var cells = new List<Vector2Int> { cell };
            Vector2Int neighbor = cell + heldDirection;
            if (GetToken(neighbor.x, neighbor.y) != null)
            {
                cells.Add(neighbor);
            }
            state = BoardState.Resolving;
            StartCoroutine(SpringBack(cells));
        }

        /// <summary>Lets go of the held token without moving it, e.g. because it's about to swap.</summary>
        private void ReleaseHeld()
        {
            if (heldCell is Vector2Int cell)
            {
                tokens[cell.x, cell.y].SetHighlight(Token.Highlight.None);
                heldCell = null;
            }
        }

        /// <summary>Snaps the neighbor the held token was pushing back onto its own cell.</summary>
        private void PutNeighborBack()
        {
            if (!(heldCell is Vector2Int cell) || heldDirection == Vector2Int.zero)
            {
                return;
            }
            Vector2Int neighbor = cell + heldDirection;
            Token neighborToken = GetToken(neighbor.x, neighbor.y);
            if (neighborToken != null)
            {
                neighborToken.transform.position = GridToWorld(neighbor.x, neighbor.y);
            }
        }

        private IEnumerator SpringBack(List<Vector2Int> cells)
        {
            // OutBack overshoots a little and settles, so the tokens bounce into place.
            yield return StartCoroutine(SlideHome(cells, springBackDuration, Easing.OutBack));
            state = BoardState.Idle;
        }

        /// <summary>
        /// Swaps the tokens in two orthogonally adjacent cells and resolves the result: if the
        /// swap makes a match, matches are cleared and the board refills (with cascades);
        /// otherwise the tokens swap back. Returns false (and does nothing) if the cells aren't
        /// valid neighbors or the board is still resolving a previous swap.
        ///
        /// The tokens slide from wherever they are, so a swap can pick up where a drag left off.
        /// A held token is put down as the swap starts.
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

            // BoardInput only ever swaps a held token with the neighbor it was dragged toward,
            // and those two slide on from where the drag left them. Anything else the drag moved
            // would be left off its cell, so snap it back first.
            if (heldCell is Vector2Int held)
            {
                Vector2Int pushed = held + heldDirection;
                bool swapIsTheDrag = (held == a && pushed == b) || (held == b && pushed == a);
                if (!swapIsTheDrag)
                {
                    PutNeighborBack();
                }
                if (held != a && held != b)
                {
                    tokens[held.x, held.y].transform.position = GridToWorld(held.x, held.y);
                }
            }
            ReleaseHeld();
            ClearHint();

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
        /// repeating for as long as the falling tokens create new matches (cascades). If the
        /// board ends up with no valid moves, it's reshuffled before the turn ends.
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
                List<Token> cleared = ClearMatches(runs, cascadeIndex);
                // The pop is part of the clear delay, not extra time on top of it; waiting for it
                // here also means every popped token is gone before anything falls.
                yield return StartCoroutine(AnimatePops(cleared));
                float remainingDelay = clearDelay - popDuration;
                if (remainingDelay > 0f)
                {
                    yield return new WaitForSeconds(remainingDelay);
                }

                CollapseAndRefill();
                yield return StartCoroutine(AnimateFalls());

                runs = model.FindMatches();
            }

            // The board has settled. If that left the player with nothing to play, fix it
            // before handing control back, so input stays locked while the tokens move.
            yield return StartCoroutine(ReshuffleIfStuck());

            state = BoardState.Idle;
        }

        /// <summary>
        /// If no swap on the board would make a match, announces it through
        /// <see cref="NoMovesLeft"/> and shuffles the tokens into a layout that has one (and no
        /// matches). Does nothing if the board is playable.
        /// </summary>
        private IEnumerator ReshuffleIfStuck()
        {
            if (model.HasValidMove())
            {
                // `yield break` is a coroutine's `return`: it ends the coroutine right here.
                yield break;
            }

            // Set before the event fires, so a listener that asks already gets "yes".
            IsReshuffling = true;
            NoMovesLeft?.Invoke();
            if (noMovesDelay > 0f)
            {
                yield return new WaitForSeconds(noMovesDelay);
            }

            List<BoardModel.Move> moves = model.Shuffle();
            if (moves != null)
            {
                // As with a swap: the model has already changed, so file each token under its
                // new cell, then let the animation catch the screen up. The copy is needed
                // because one token's new cell is usually another token's old one.
                var before = (Token[,])tokens.Clone();
                var shuffled = new List<Vector2Int>();
                foreach (BoardModel.Move move in moves)
                {
                    PlaceToken(before[move.From.x, move.From.y], move.To.x, move.To.y);
                    shuffled.Add(move.To);
                }
                yield return StartCoroutine(SlideHome(shuffled, shuffleDuration, Easing.OutCubic));
                IsReshuffling = false;
                yield break;
            }

            // Very rarely the tokens on the board can't be arranged into a playable layout at
            // all (say, nearly all of them are one type). Then deal new types instead. No tokens
            // are created or destroyed: each one just changes what it shows.
            if (!model.FillWithValidMove())
            {
                Debug.LogWarning($"Board: a {width}x{height} board with these token types has no valid moves.", this);
            }
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    tokens[x, y].SetType(model.TypeAt(x, y));
                }
            }
            IsReshuffling = false;
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

        /// <summary>
        /// Slides the tokens the model says are in cells a and b from wherever they are onto
        /// those cells' centers.
        /// </summary>
        private IEnumerator AnimateSwap(Vector2Int a, Vector2Int b)
        {
            // Ease-out (fast, then slowing) rather than easing in too: after a drag the tokens are
            // already moving, and a slow start would feel like they stalled when let go.
            return SlideHome(new List<Vector2Int> { a, b }, swapDuration, Easing.OutCubic);
        }

        // `yield return null` means "wait until the next frame", so the loop below runs one
        // step per frame, which is how we spread movement over time without blocking the game.
        /// <summary>
        /// Slides the tokens in <paramref name="cells"/> from wherever they are onto their cells'
        /// centers over <paramref name="duration"/> seconds, shaping the motion with
        /// <paramref name="ease"/>.
        /// </summary>
        // System.Func<float, float> is a delegate type: "a method that takes a float and returns
        // a float". Passing Easing.OutCubic (without calling it) hands over the method itself,
        // so one slide loop can be reused with any easing curve.
        private IEnumerator SlideHome(List<Vector2Int> cells, float duration, System.Func<float, float> ease)
        {
            var movers = new List<Transform>();
            var starts = new List<Vector3>();
            var targets = new List<Vector3>();
            foreach (Vector2Int cell in cells)
            {
                movers.Add(tokens[cell.x, cell.y].transform);
                starts.Add(tokens[cell.x, cell.y].transform.position);
                targets.Add(GridToWorld(cell.x, cell.y));
            }

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                // t goes 0 -> 1 over the duration; the easing curve reshapes it. LerpUnclamped
                // (unlike Lerp) follows t past 1, which is what lets OutBack overshoot.
                float t = ease(elapsed / duration);
                for (int i = 0; i < movers.Count; i++)
                {
                    movers[i].position = Vector3.LerpUnclamped(starts[i], targets[i], t);
                }
                yield return null;
            }

            // Snap exactly onto the targets; the loop may stop slightly short of t = 1.
            for (int i = 0; i < movers.Count; i++)
            {
                movers[i].position = targets[i];
            }
        }

        /// <summary>
        /// Empties every cell in <paramref name="runs"/> in the model and the view array, spawns
        /// a burst on each, and reports each run through <see cref="MatchCleared"/>, then the
        /// step as a whole through <see cref="ClearStepResolved"/>. Returns the tokens that were
        /// removed: they're no longer on the board, but they're still on screen, for
        /// <see cref="AnimatePops"/> to pop and then destroy.
        /// </summary>
        private List<Token> ClearMatches(List<List<Vector2Int>> runs, int cascadeIndex)
        {
            // Worked out before clearing, so each cell's burst can be sized by its run. A cell an
            // L or T shape's two runs share gets the longer one.
            Dictionary<Vector2Int, int> runLengths = BoardModel.LongestRunPerCell(runs);
            var cleared = new List<Token>();

            // The model hands back each cleared cell once, even where two runs share it, so no
            // cell pops or bursts twice.
            foreach (Vector2Int cell in model.Clear(runs))
            {
                Token token = tokens[cell.x, cell.y];
                SpawnExplosion(cell, token.Type, runLengths[cell]);
                // The pop sets the token's scale from here on. Disabling the Token component
                // stops its Update, so a token still shrinking back from being held can't fight it.
                token.enabled = false;
                cleared.Add(token);
                // Null the view entry right away so it agrees with the model immediately; only
                // the visuals linger for the pop.
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
            return cleared;
        }

        /// <summary>
        /// Spawns a burst on <paramref name="cell"/> in <paramref name="type"/>'s color, sized for
        /// a run of <paramref name="runLength"/>.
        /// </summary>
        private void SpawnExplosion(Vector2Int cell, TokenType type, int runLength)
        {
            if (explosionPrefab == null)
            {
                return;
            }

            // Parented to the Board (just to keep the Hierarchy tidy), never to the token: the
            // token is about to be destroyed, and its children would go with it. The burst
            // destroys itself when it's done (see Explosion).
            Explosion explosion = Instantiate(explosionPrefab, GridToWorld(cell.x, cell.y), Quaternion.identity, transform);
            // The prefab is built for a 1-unit cell; scaling it scales the whole burst with it.
            explosion.transform.localScale = Vector3.one * cellSize;
            TokenStyle style = CurrentTokenStyle;
            explosion.Play(style != null ? style.BurstColorFor(type) : Color.white, runLength);
        }

        /// <summary>
        /// Swells <paramref name="popping"/> slightly and then shrinks them to nothing over
        /// <see cref="popDuration"/>, then destroys them.
        /// </summary>
        private IEnumerator AnimatePops(List<Token> popping)
        {
            // Time.deltaTime is scaled time, so while the game is paused (Time.timeScale = 0) it's
            // 0 and the pop freezes along with everything else.
            for (float elapsed = 0f; elapsed < popDuration; elapsed += Time.deltaTime)
            {
                Vector3 scale = Vector3.one * (cellSize * PopScale(elapsed / popDuration));
                foreach (Token token in popping)
                {
                    token.transform.localScale = scale;
                }
                yield return null;
            }

            // Destroy removes each GameObject at the end of the current frame.
            foreach (Token token in popping)
            {
                Destroy(token.gameObject);
            }
        }

        /// <summary>
        /// A popping token's size (1 = normal) at <paramref name="t"/> of the way through the pop:
        /// it grows from 1 to <see cref="PopPeakScale"/>, then shrinks to 0 at t = 1.
        /// </summary>
        public static float PopScale(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < PopPeakTime)
            {
                return Mathf.Lerp(1f, PopPeakScale, t / PopPeakTime);
            }
            return Mathf.Lerp(PopPeakScale, 0f, (t - PopPeakTime) / (1f - PopPeakTime));
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
            float swapDuration, float clearDelay, float fallSpeed,
            float popDuration = 0f, Explosion explosionPrefab = null, float springBackDuration = 0.01f,
            float noMovesDelay = 0f, float shuffleDuration = 0.01f)
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
            this.popDuration = popDuration;
            this.explosionPrefab = explosionPrefab;
            this.springBackDuration = springBackDuration;
            this.noMovesDelay = noMovesDelay;
            this.shuffleDuration = shuffleDuration;
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
            IsReshuffling = false;
            heldCell = null;
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
