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
        [Tooltip("Pause, in seconds, between matched tokens being cleared and the rest falling. The pop plays during it. A power gem's blast takes the same again, plus however long a Star's beam travels.")]
        [SerializeField, Min(0f)] private float clearDelay = 0.25f;
        [Tooltip("How fast falling tokens move, in cells per second.")]
        [SerializeField, Min(0.1f)] private float fallSpeed = 10f;

        [Header("Clearing Effects")]
        [Tooltip("How long, in seconds, a cleared token takes to pop (swell, then shrink away).")]
        [SerializeField, Min(0f)] private float popDuration = 0.12f;
        [Tooltip("Particle burst spawned on every cleared cell. Optional: without one, tokens just pop.")]
        [SerializeField] private Explosion explosionPrefab;
        [Tooltip("How long, in seconds, the gems of a match that makes a power gem take to slide into its cell before they pop. 0 skips the slide.")]
        [SerializeField, Min(0f)] private float collapseDuration = 0.12f;
        [Tooltip("How fast a Star's beam travels along its row and column, in cells per second. Each token in its path pops as the beam reaches it.")]
        [SerializeField, Min(0.1f)] private float beamSpeed = 16f;
        [Tooltip("Pause, in seconds, between a Hypercube zapping one gem and the next. With a dozen gems of a colour on the board, the whole sweep takes a dozen times this.")]
        [SerializeField, Min(0f)] private float zapInterval = 0.05f;
        [Tooltip("Extra pause, in seconds, between one wave of a chain reaction and the next, so the player can follow which gem set off which.")]
        [SerializeField, Min(0f)] private float chainDelay = 0.1f;

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

        // While a match's gems slide into a new power gem, they (and the gem's own token) shrink
        // to this size, relative to normal. The gem then springs back up to full size over
        // GemRevealDuration, which is the moment it "appears".
        private const float CollapseEndScale = 0.55f;
        private const float GemRevealDuration = 0.18f;

        /// <summary>
        /// Fired once for every run of matching tokens that gets cleared, with the run's length
        /// (3, 4, 5...). The round stats count runs from this. (The score is worked out per
        /// step instead: see <see cref="ClearStepScored"/>.)
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
        /// Fired once per clear step, when it's completely over (its match has popped and its
        /// chain reaction, if any, has played) and just before anything falls, with a
        /// <see cref="ClearStep"/> describing it: its runs, how many gems its blasts destroyed,
        /// and its cascade index. Scoring listens to this without the Board knowing about score.
        ///
        /// Unlike <see cref="ClearStepResolved"/>, a swapped Hypercube's step fires it too (with
        /// no runs), because the gems it destroys are worth points.
        /// </summary>
        // One event per step rather than one per run or per blast: everything in a step shares
        // one cascade multiplier, so the listener wants it all at once.
        public event System.Action<ClearStep> ClearStepScored;

        /// <summary>
        /// Fired when a match leaves a power gem behind, with which kind and the cell it's in.
        /// Comes during the clear step that made it, before that step's
        /// <see cref="ClearStepResolved"/>.
        /// </summary>
        public event System.Action<PowerKind, Vector2Int> PowerGemCreated;

        /// <summary>
        /// Fired when a power gem goes off, with which kind, the cell it was in, and its wave
        /// index within the chain reaction: 0 for a gem set off directly (by being matched, or
        /// a Hypercube by being swapped), 1 for a gem caught in one of those blasts, 2 for a gem
        /// caught in a wave-1 blast, and so on. Waves are played in order, so the indexes never
        /// go down within a clear step.
        ///
        /// For a matched gem that's the detonation phase: after its step's
        /// <see cref="ClearStepResolved"/> and the matched tokens' pop, before anything falls.
        /// For a swapped Hypercube it's as soon as the swap has finished sliding, and that step
        /// fires no <see cref="MatchCleared"/> or <see cref="ClearStepResolved"/>: no run was
        /// cleared.
        /// </summary>
        // Action<PowerKind, Vector2Int, int>: a method taking those three things, in that order.
        public event System.Action<PowerKind, Vector2Int, int> PowerGemDetonated;

        /// <summary>
        /// Fired just before each <see cref="PowerGemDetonated"/>, with the whole blast described
        /// (see <see cref="PowerGemBlast"/>): the cells it hits and when it hits each one. This is
        /// what blast effects draw from. The Board itself draws nothing for a blast: it only pops
        /// the tokens, at the delays it hands out here.
        /// </summary>
        // A second event rather than more arguments on PowerGemDetonated: most listeners (stats,
        // sound) only care which gem went off, and stay as simple as they were.
        public event System.Action<PowerGemBlast> PowerGemBlastStarted;

        /// <summary>
        /// Fired when the player's swap starts animating, whether or not it will make a match.
        /// Only player swaps fire it: tokens falling during a refill or cascade never do.
        /// </summary>
        public event System.Action SwapStarted;

        /// <summary>
        /// Fired when a swap wasn't a valid move (it made no match and set off no Hypercube) and
        /// the tokens start sliding back. Always comes after a <see cref="SwapStarted"/>.
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

        /// <summary>How fast a Star's beam travels, in cells per second (see <see cref="BlastDelay"/>).</summary>
        public float BeamSpeed => beamSpeed;

        /// <summary>
        /// How long, in seconds, after <see cref="PowerGemCreated"/> the new gem appears: the
        /// matched gems slide into its cell first. 0 if they don't.
        /// </summary>
        public float CollapseDuration => collapseDuration;

        /// <summary>
        /// The renderer of the token prefab, i.e. how tokens are drawn (material, sorting layer
        /// and order). Effects copy it so they're drawn the same way, just in front.
        /// </summary>
        public SpriteRenderer TokenRenderer => tokenPrefab != null ? tokenPrefab.GetComponent<SpriteRenderer>() : null;

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

        /// <summary>
        /// True while power gems are going off: from a clear step's first detonation until its
        /// last wave has finished playing, just before anything falls. The round clock stops
        /// for this too, so a long chain rewards the player instead of eating their time.
        /// An ordinary match (no power gem) never sets it.
        /// </summary>
        public bool IsDetonating { get; private set; }

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
            // The reshuffle and detonation coroutines were just stopped, so they will never get
            // to clear these themselves.
            IsReshuffling = false;
            IsDetonating = false;
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
        /// The colour effects should use for gems of <paramref name="type"/> in the current token
        /// style (the same one their clear bursts use). White if there's no style, or no type
        /// (a Hypercube has none).
        /// </summary>
        public Color GemColorFor(TokenType type)
        {
            TokenStyle style = CurrentTokenStyle;
            return style != null && type != null ? style.BurstColorFor(type) : Color.white;
        }

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

        /// <summary>Spawns a token on every cell's center showing the piece the model has there.</summary>
        private void SpawnTokenInEveryCell()
        {
            // The model decides what goes where; we just spawn a token to show each cell.
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    SpawnToken(x, y, model.PieceAt(x, y), GridToWorld(x, y));
                }
            }
        }

        /// <summary>
        /// Creates a token for <paramref name="piece"/> at <paramref name="position"/> and records it
        /// as occupying cell (x, y). The position can differ from the cell's center (e.g. above
        /// the board) so the token can then animate into place.
        /// </summary>
        private void SpawnToken(int x, int y, Piece piece, Vector3 position)
        {
            // Instantiate clones the prefab into the scene. Passing a component (Token)
            // instead of a GameObject returns the clone's matching component directly.
            Token token = Instantiate(tokenPrefab, position, Quaternion.identity, tokenContainer);
            token.transform.localScale = Vector3.one * cellSize;
            token.SetStyle(CurrentTokenStyle);
            token.SetPiece(piece);
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
        /// swap makes a match or sets off a Hypercube, tokens are cleared and the board refills
        /// (with cascades); otherwise the tokens swap back. Returns false (and does nothing) if the cells aren't
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
        /// One whole turn: swap, then either swap back (not a valid move) or clear -> detonate ->
        /// fall -> refill, repeating for as long as the falling tokens create new matches
        /// (cascades). "Detonate" can be several waves: a power gem caught in a blast goes off
        /// too (see <see cref="PlayWaves"/>). A Hypercube swap takes the place of the first
        /// clear: see <see cref="BoardModel.ActivateHypercube"/>.
        /// If the board ends up with no valid moves, it's reshuffled before the turn ends.
        /// </summary>
        private IEnumerator ResolveSwap(Vector2Int a, Vector2Int b)
        {
            // Update the model (and view array) first, so matching sees the new layout; the
            // animation just catches the screen up to it.
            Swap(a, b);
            SwapStarted?.Invoke();
            yield return StartCoroutine(AnimateSwap(a, b));

            // cascadeIndex counts the clear steps of this turn: 0 is the swap's own, 1+ are
            // cascades. It's local to this turn, so every new swap starts counting from 0 again.
            int cascadeIndex = 0;
            List<MatchGroup> groups;

            // There are two ways a swap can be a valid move, and the Hypercube's is asked about
            // first. It has to be: the gem that changed places with the cube may also have
            // landed in a run, and looking for runs first would clear just that run (maybe even
            // leaving a new power gem) instead of every gem of the colour.
            if (model.IsHypercubeSwap(a, b))
            {
                // Step 0 of this turn is the cube going off rather than a match. After it the
                // board falls and refills exactly as after any clear, and whatever that lines up
                // is an ordinary first cascade.
                // Work out which of the two cells is which. Exactly one holds the cube: that's
                // what IsHypercubeSwap checked.
                Vector2Int cube = model.PieceAt(a).Power == PowerKind.Hypercube ? a : b;
                TokenType colour = model.TypeAt(cube == a ? b : a);

                // As in the cascade loop below: the model clears everything right now (the
                // colour, and whatever chain reaction that starts) and describes what it did.
                // There's no run to report, so no MatchCleared or ClearStepResolved here, but
                // the zapped gems are worth points, so the step is still scored.
                BoardModel.ClearResult zapped = model.ActivateHypercube(cube, colour);
                // The cube wasn't matched, so unlike a matched gem its token is still there.
                yield return StartCoroutine(PlayWaves(zapped.Waves, firstGemsOnBoard: true));
                ClearStepScored?.Invoke(ClearStep.From(zapped, new List<MatchGroup>(), cascadeIndex));
                CollapseAndRefill();
                yield return StartCoroutine(AnimateFalls());
                groups = model.FindMatchGroups();
                cascadeIndex = 1;
            }
            else
            {
                // The swapped cells are passed along so each match the swap made is anchored on
                // the token the player moved.
                groups = model.FindMatchGroups(a, b);
                if (groups.Count == 0)
                {
                    // Not a valid move: put everything back exactly as it was. That includes
                    // swapping two Hypercubes, which does nothing yet.
                    Swap(a, b);
                    SwapRejected?.Invoke();
                    yield return StartCoroutine(AnimateSwap(a, b));
                }
            }

            // Cascade loop: clear the current matches, set off any power gems among them, let
            // tokens fall and refill, then check whether the new layout made more matches.
            // Skipped entirely for an invalid swap.
            // A `for` with nothing before its first `;` just means "the counter is already set
            // up": it was declared above so the Hypercube branch could start it at 1.
            for (; groups.Count > 0; cascadeIndex++)
            {
                // The model does the whole clear step right now, blasts and chain reactions
                // included, and describes what it did. The rest of this pass plays that
                // description back one phase at a time. Until the detonation phase below, the
                // blasted tokens are still on screen (and in the view array) although the model
                // has already emptied their cells.
                BoardModel.ClearResult result = model.ClearMatches(groups);

                // Phase 1: the matched tokens pop, and any new power gems appear. A match that
                // makes a power gem first slides its gems into the gem's cell, so the player sees
                // where the new gem came from.
                List<Token> matched = RemoveMatched(groups, result, cascadeIndex);
                yield return StartCoroutine(CollapseIntoNewGems(matched, result, groups));
                yield return StartCoroutine(PopAndPause(matched));

                // Phase 2: matched power gems go off, and then any power gems they caught, wave
                // after wave. This is a phase of its own, after the match's pop and before
                // gravity, so the player sees "match, then blast, then fall" as three beats.
                // Adding a phase to a coroutine is just adding lines here: the code reads in the
                // order things happen.
                if (result.Waves.Count > 0)
                {
                    // The matched gems' own tokens popped with the match, in phase 1.
                    yield return StartCoroutine(PlayWaves(result.Waves, firstGemsOnBoard: false));
                }

                // The step is over: score all of it at once, with the step's multiplier.
                ClearStepScored?.Invoke(ClearStep.From(result, groups, cascadeIndex));

                // Phase 3: only now, with the whole chain over, does anything fall.
                CollapseAndRefill();
                yield return StartCoroutine(AnimateFalls());

                // A cascade has no swapped cells: nobody moved these tokens, they fell.
                groups = model.FindMatchGroups();
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
            // The redeal only changes colours: a power gem is still a power gem afterwards, in
            // the same cell. (FillWithValidMove would also work, but it makes every piece an
            // ordinary one, silently deleting the player's power gems.)
            if (!model.RedealKeepingPowers())
            {
                Debug.LogWarning($"Board: a {width}x{height} board with these token types has no valid moves.", this);
            }
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    tokens[x, y].SetPiece(model.PieceAt(x, y));
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
        /// Phase 1 of a clear step: takes the matched tokens off the board, spawning a burst on
        /// each, and turns each match's anchor token into the power gem the model made there.
        /// Reports each run through <see cref="MatchCleared"/>, each new gem through
        /// <see cref="PowerGemCreated"/>, then the step as a whole through
        /// <see cref="ClearStepResolved"/>. Returns the tokens that were removed: they're no
        /// longer on the board, but they're still on screen, for <see cref="AnimatePops"/> to pop
        /// and then destroy.
        /// </summary>
        /// <param name="groups">The matches that were cleared.</param>
        /// <param name="result">What the model said clearing them did.</param>
        private List<Token> RemoveMatched(List<MatchGroup> groups, BoardModel.ClearResult result, int cascadeIndex)
        {
            // Bursts and both run events still work run by run, exactly as they did before
            // matches were grouped, so stats and sound don't change.
            var runs = new List<List<Vector2Int>>();
            foreach (MatchGroup group in groups)
            {
                runs.AddRange(group.Runs);
            }

            // Each cell's burst is sized by its run. A cell an L or T shape's two runs share
            // gets the longer one.
            Dictionary<Vector2Int, int> runLengths = BoardModel.LongestRunPerCell(runs);
            var removed = new List<Token>();

            // The model lists each matched cell once, even where two runs share it, so no cell
            // pops or bursts twice.
            foreach (Vector2Int cell in result.Matched)
            {
                SpawnExplosion(cell, tokens[cell.x, cell.y].Type, runLengths[cell]);
                removed.Add(TakeToken(cell));
            }

            // A new power gem isn't a new token: the anchor's token stays where it is and just
            // starts showing the power. A Flame or Star is the same colour as before, so only
            // the overlay changes; a Hypercube loses its colour and gets a sprite of its own.
            foreach (BoardModel.Creation creation in result.Created)
            {
                tokens[creation.Cell.x, creation.Cell.y].SetPiece(creation.Piece);
                // ?.Invoke calls every subscriber, or does nothing if nobody has subscribed.
                PowerGemCreated?.Invoke(creation.Kind, creation.Cell);
            }

            int longestRun = 0;
            foreach (List<Vector2Int> run in runs)
            {
                MatchCleared?.Invoke(run.Count);
                longestRun = Mathf.Max(longestRun, run.Count);
            }

            ClearStepResolved?.Invoke(longestRun, cascadeIndex);
            return removed;
        }

        /// <summary>
        /// The "creation moment": every matched gem of a match that made a power gem slides into
        /// the new gem's cell, shrinking as it goes, while the gem's own token shrinks too. Then
        /// the gem springs back up to full size (in the background, while the matched gems pop
        /// where they ended up). Does nothing if this step made no power gem, or if
        /// <see cref="collapseDuration"/> is 0.
        /// </summary>
        /// <param name="matched">The tokens <see cref="RemoveMatched"/> took off, in the same order as <c>result.Matched</c>.</param>
        // Only the picture moves here. The model, the view array and every event already say
        // the gem exists (RemoveMatched did all that), so nothing waiting on them is held up.
        private IEnumerator CollapseIntoNewGems(List<Token> matched, BoardModel.ClearResult result, List<MatchGroup> groups)
        {
            if (collapseDuration <= 0f || result.Created.Count == 0)
            {
                yield break;
            }

            var newGemCells = new List<Vector2Int>();
            foreach (BoardModel.Creation creation in result.Created)
            {
                newGemCells.Add(creation.Cell);
            }
            Dictionary<Vector2Int, Vector2Int> targets = CollapseTargets(groups, newGemCells);

            // Who moves, and from where to where. Gems from a match that made no power gem
            // aren't in `targets`, and just pop where they are afterwards.
            var movers = new List<Transform>();
            var starts = new List<Vector3>();
            var ends = new List<Vector3>();
            for (int i = 0; i < matched.Count; i++)
            {
                if (targets.TryGetValue(result.Matched[i], out Vector2Int gemCell))
                {
                    movers.Add(matched[i].transform);
                    starts.Add(matched[i].transform.position);
                    ends.Add(GridToWorld(gemCell.x, gemCell.y));
                }
            }
            var newGems = new List<Transform>();
            foreach (Vector2Int cell in newGemCells)
            {
                newGems.Add(tokens[cell.x, cell.y].transform);
            }

            // InCubic starts slow and speeds up, so the gems look pulled in rather than pushed.
            for (float elapsed = 0f; elapsed < collapseDuration; elapsed += Time.deltaTime)
            {
                float t = Easing.InCubic(elapsed / collapseDuration);
                Vector3 size = Vector3.one * (cellSize * Mathf.Lerp(1f, CollapseEndScale, t));
                for (int i = 0; i < movers.Count; i++)
                {
                    movers[i].position = Vector3.Lerp(starts[i], ends[i], t);
                    movers[i].localScale = size;
                }
                foreach (Transform gem in newGems)
                {
                    gem.localScale = size;
                }
                yield return null;
            }
            for (int i = 0; i < movers.Count; i++)
            {
                movers[i].position = ends[i];
            }

            // Not waited for: the new gem grows back while the gems that fed it pop.
            StartCoroutine(RevealNewGems(newGems));
        }

        /// <summary>
        /// Springs the new power gems from <see cref="CollapseEndScale"/> back to full size,
        /// overshooting a little (<see cref="Easing.OutBack"/>) so they land with a bounce.
        /// </summary>
        private IEnumerator RevealNewGems(List<Transform> newGems)
        {
            for (float elapsed = 0f; elapsed < GemRevealDuration; elapsed += Time.deltaTime)
            {
                float size = Mathf.LerpUnclamped(CollapseEndScale, 1f, Easing.OutBack(elapsed / GemRevealDuration));
                foreach (Transform gem in newGems)
                {
                    // A gem can be blasted (and destroyed) while it's still growing.
                    if (gem != null)
                    {
                        gem.localScale = Vector3.one * (cellSize * size);
                    }
                }
                yield return null;
            }
            foreach (Transform gem in newGems)
            {
                if (gem != null)
                {
                    gem.localScale = Vector3.one * cellSize;
                }
            }
        }

        /// <summary>
        /// For a clear step that made power gems in <paramref name="newGemCells"/>: which cell
        /// each matched gem should slide into. Every cell of a match whose anchor is one of
        /// those cells maps to that anchor (the anchor itself excepted: it's the new gem). Cells
        /// of a match that made no gem aren't in the result.
        /// </summary>
        // `public static` so the Edit Mode tests can check it without a scene.
        public static Dictionary<Vector2Int, Vector2Int> CollapseTargets(IReadOnlyList<MatchGroup> groups,
            IReadOnlyList<Vector2Int> newGemCells)
        {
            var newGems = new HashSet<Vector2Int>(newGemCells);
            var targets = new Dictionary<Vector2Int, Vector2Int>();
            foreach (MatchGroup group in groups)
            {
                if (!newGems.Contains(group.Anchor))
                {
                    continue;
                }
                foreach (Vector2Int cell in group.Cells)
                {
                    if (cell != group.Anchor)
                    {
                        targets[cell] = group.Anchor;
                    }
                }
            }
            return targets;
        }

        /// <summary>
        /// Phase 2 of a clear step: plays a chain reaction, one wave of <paramref name="waves"/>
        /// after another. Each wave's blasts are shown and its tokens popped before the next
        /// wave starts, with <see cref="chainDelay"/> between them, so the player can follow
        /// which gem set off which. Finishes once the last wave has popped; nothing has fallen.
        /// </summary>
        /// <param name="waves">The waves, as <see cref="BoardModel.ClearResult.Waves"/> lists them.</param>
        /// <param name="firstGemsOnBoard">
        /// Whether the first wave's gems still have their tokens on the board. False after a
        /// match (they popped with it), true for a swapped Hypercube.
        /// </param>
        // The model worked out the whole chain in one go, before any of this is shown. Pacing it
        // is then just a loop with a `yield return` in it: show a wave, wait for it, show the
        // next. Nothing here decides what goes off, only when the player sees it.
        private IEnumerator PlayWaves(IReadOnlyList<IReadOnlyList<BoardModel.Detonation>> waves, bool firstGemsOnBoard)
        {
            // Covers every wave, and the waits between them, so the clock is held for the whole
            // chain and starts again only as the tokens begin to fall.
            IsDetonating = waves.Count > 0;
            for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
            {
                bool isLastWave = waveIndex == waves.Count - 1;

                // The power gems this wave's blasts catch: exactly the gems of the next wave.
                // Their tokens stay on the board through this wave, so each is still sitting
                // there, visibly a power gem, when its own turn comes.
                var caught = new HashSet<Vector2Int>();
                if (!isLastWave)
                {
                    foreach (BoardModel.Detonation next in waves[waveIndex + 1])
                    {
                        caught.Add(next.Cell);
                    }
                }

                // From the second wave on, every gem going off is one that was left on the board
                // by the wave before.
                bool gemsOnBoard = waveIndex > 0 || firstGemsOnBoard;
                // `out` hands back a second list alongside the returned one: how long each
                // token waits before it pops (see Detonate).
                // Time.time is the game clock, in seconds. It stops while the game is paused, the
                // same as the pops and waits below, so the sums still work out across a pause.
                float waveStarted = Time.time;
                List<Token> popping = Detonate(waves[waveIndex], waveIndex, gemsOnBoard, caught,
                    out List<float> popDelays, out float latestCatch);
                yield return StartCoroutine(PopAndPause(popping, popDelays));

                // PopAndPause only waits for tokens that pop, and a caught gem doesn't: it stays
                // for the next wave. So if a Star's beam still has some way to go before it
                // reaches one, wait for it here, or the gem would go off before anything hit it.
                float stillToCome = latestCatch - (Time.time - waveStarted);
                if (stillToCome > 0f)
                {
                    yield return new WaitForSeconds(stillToCome);
                }

                if (!isLastWave && chainDelay > 0f)
                {
                    yield return new WaitForSeconds(chainDelay);
                }
            }
            IsDetonating = false;
        }

        /// <summary>
        /// One wave of a chain reaction: for each power gem in <paramref name="wave"/>, works
        /// out when its blast reaches each cell, takes the tokens the blast cleared off the
        /// board, and reports it through <see cref="PowerGemBlastStarted"/> (for the effects)
        /// and <see cref="PowerGemDetonated"/>. Returns the removed tokens, as
        /// <see cref="RemoveMatched"/> does.
        /// </summary>
        /// <param name="gemsOnBoard">
        /// Whether the gems going off still have their own tokens on the board. If so those are
        /// removed too; if not they have already popped (they were part of the match).
        /// </param>
        /// <param name="caught">
        /// Cells of power gems this wave's blasts catch. Their tokens are left where they are:
        /// they go off in the next wave.
        /// </param>
        /// <param name="popDelays">
        /// For each returned token, in the same order, how many seconds to wait before popping
        /// it (see <see cref="BlastDelay"/> and <see cref="ZapDelay"/>).
        /// </param>
        /// <param name="latestCatch">
        /// How many seconds into the wave the last of the <paramref name="caught"/> gems is
        /// reached by the blast that caught it. 0 if none were caught.
        /// </param>
        // Blasted gems aren't runs, so no MatchCleared here: they're scored with their whole
        // step, through ClearStepScored.
        private List<Token> Detonate(IReadOnlyList<BoardModel.Detonation> wave, int waveIndex, bool gemsOnBoard,
            HashSet<Vector2Int> caught, out List<float> popDelays, out float latestCatch)
        {
            var removed = new List<Token>();
            popDelays = new List<float>();
            latestCatch = 0f;
            foreach (BoardModel.Detonation detonation in wave)
            {
                bool isCube = detonation.Kind == PowerKind.Hypercube;
                // The gem's token may be gone by now, which is why the model hands back the
                // piece it was: that's where the blast's colour comes from. A Hypercube has no
                // colour of its own, so it flares in the colour it's about to destroy.
                TokenType colour = isCube ? detonation.Target : detonation.Piece.Colour;

                // One number per blasted gem: when the blast reaches it. A Hypercube zaps its
                // gems one after another, in the order the model listed them.
                var delays = new List<float>(detonation.Blasted.Count);
                for (int i = 0; i < detonation.Blasted.Count; i++)
                {
                    Vector2Int cell = detonation.Blasted[i];
                    float delay = isCube
                        ? ZapDelay(i, zapInterval)
                        : BlastDelay(detonation.Kind, detonation.Cell, cell, beamSpeed);
                    delays.Add(delay);

                    // A power gem caught in this blast doesn't pop with the rest: it stays put
                    // until the next wave, when it goes off itself.
                    if (caught.Contains(cell))
                    {
                        latestCatch = Mathf.Max(latestCatch, delay);
                        continue;
                    }

                    // All the other blasted tokens leave the board (the view array) right now.
                    // What's spread over time is only when each one pops.
                    removed.Add(TakeToken(cell));
                    popDelays.Add(delay);
                }

                if (gemsOnBoard)
                {
                    // The gem itself goes as its blast starts. A Hypercube outlasts its last
                    // target by one more beat, then goes too.
                    removed.Add(TakeToken(detonation.Cell));
                    popDelays.Add(isCube ? ZapDelay(detonation.Blasted.Count, zapInterval) : 0f);
                }

                // The pictures (the Flame's burst, the Star's beam, the Hypercube's bolts) and
                // the sounds are all drawn by listeners, from the same delays the pops use.
                PowerGemBlastStarted?.Invoke(new PowerGemBlast(detonation.Kind, detonation.Cell, waveIndex,
                    colour, detonation.Blasted, delays));
                PowerGemDetonated?.Invoke(detonation.Kind, detonation.Cell, waveIndex);
            }
            return removed;
        }

        /// <summary>
        /// How many seconds after a power gem of <paramref name="kind"/> goes off in
        /// <paramref name="gemCell"/> its blast reaches <paramref name="cell"/>. A Flame's blast
        /// hits everything at once (0). A Star's beam travels outward at
        /// <paramref name="beamSpeed"/> cells per second, so a cell 3 away waits 3 times as long
        /// as the one next to the gem.
        /// </summary>
        // This is how an effect is sequenced across cells: the whole blast is decided up front,
        // and each cell is just given its own start time. Nothing has to "step" along the row.
        // `public static` so the Edit Mode tests can check it without a scene.
        public static float BlastDelay(PowerKind kind, Vector2Int gemCell, Vector2Int cell, float beamSpeed)
        {
            if (kind != PowerKind.Star)
            {
                return 0f;
            }

            // A cell in the Star's row or column differs from it in only x or only y, so adding
            // the two differences gives its distance along the beam, in cells.
            int distance = Mathf.Abs(cell.x - gemCell.x) + Mathf.Abs(cell.y - gemCell.y);
            return distance / beamSpeed;
        }

        /// <summary>
        /// How many seconds after a Hypercube goes off it zaps its <paramref name="index"/>th
        /// gem (0 = the first): one <paramref name="zapInterval"/> later for each gem before it.
        /// </summary>
        // A Star's beam reaches cells in order of distance, so BlastDelay works from where a
        // cell is. A Hypercube's gems are scattered anywhere, with no "path" between them, so
        // the order is simply the order the model listed them in, and the delay comes from a
        // gem's place in that list. `public static` so the Edit Mode tests can check it.
        public static float ZapDelay(int index, float zapInterval)
        {
            return index * zapInterval;
        }

        /// <summary>
        /// Takes the token in <paramref name="cell"/> out of the view array and returns it. It
        /// stays on screen (so it can pop) but no longer belongs to any cell.
        /// </summary>
        private Token TakeToken(Vector2Int cell)
        {
            Token token = tokens[cell.x, cell.y];
            // The pop sets the token's scale from here on. Disabling the Token component stops
            // its Update, so a token still shrinking back from being held can't fight it.
            token.enabled = false;
            tokens[cell.x, cell.y] = null;
            return token;
        }

        /// <summary>
        /// Pops <paramref name="popping"/> and then waits out whatever is left of
        /// <see cref="clearDelay"/>. <paramref name="delays"/>, if given, holds each token's wait
        /// before its pop starts (see <see cref="AnimatePops"/>).
        /// </summary>
        // `= null` makes the parameter optional: the match phase leaves it out, and everything
        // pops together.
        private IEnumerator PopAndPause(List<Token> popping, List<float> delays = null)
        {
            // The pop is part of the clear delay, not extra time on top of it; waiting for it
            // here also means every popped token is gone before the next phase starts.
            yield return StartCoroutine(AnimatePops(popping, delays));
            float remainingDelay = clearDelay - popDuration;
            if (remainingDelay > 0f)
            {
                yield return new WaitForSeconds(remainingDelay);
            }
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
            explosion.Play(GemColorFor(type), runLength);
        }

        /// <summary>
        /// Swells <paramref name="popping"/> slightly and then shrinks them to nothing over
        /// <see cref="popDuration"/>, then destroys them. With <paramref name="delays"/>, each
        /// token first waits its own number of seconds (matched to <paramref name="popping"/> by
        /// index), so the pops can run along a row one after another; without, they all go at once.
        /// A token pops from whatever size it is when its turn comes, so the gems that
        /// <see cref="CollapseIntoNewGems"/> shrank stay small instead of jumping back to full size.
        /// </summary>
        private IEnumerator AnimatePops(List<Token> popping, List<float> delays = null)
        {
            // Each token's size when its pop started, as a fraction of a cell. 0 means "hasn't
            // started yet" (a new array of floats is all zeros).
            var startScales = new float[popping.Count];

            // The token that starts last decides how long the whole thing takes.
            float longestDelay = 0f;
            if (delays != null)
            {
                foreach (float delay in delays)
                {
                    longestDelay = Mathf.Max(longestDelay, delay);
                }
            }

            // Time.deltaTime is scaled time, so while the game is paused (Time.timeScale = 0) it's
            // 0 and the pop freezes along with everything else.
            for (float elapsed = 0f; elapsed < longestDelay + popDuration; elapsed += Time.deltaTime)
            {
                for (int i = 0; i < popping.Count; i++)
                {
                    // One shared clock, and each token reads it from its own starting point.
                    float sinceStart = elapsed - (delays != null ? delays[i] : 0f);
                    if (sinceStart < 0f)
                    {
                        // Its turn hasn't come yet: leave it sitting on the board as it is.
                        continue;
                    }

                    // Its first frame: remember the size it starts from. Usually that's a whole
                    // cell (1), but a gem that slid into a new power gem arrives already shrunk.
                    if (startScales[i] == 0f)
                    {
                        startScales[i] = popping[i].transform.localScale.x / cellSize;
                    }

                    // With no pop time at all there's nothing to divide by: go straight to "done".
                    float t = popDuration > 0f ? sinceStart / popDuration : 1f;
                    // PopScale stops at 0 once t passes 1, so a token that has finished stays
                    // invisible while later ones are still going.
                    popping[i].transform.localScale = Vector3.one * (cellSize * startScales[i] * PopScale(t));
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
                SpawnToken(cell.x, cell.y, spawn.Piece, GridToWorld(cell.x, cell.y + spawn.FallDistance));
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
        /// World position of a point given in cells, where (x, y) is the center of cell (x, y)
        /// and in-between values are in between. Handy for the middle of a group of cells.
        /// </summary>
        public Vector3 GridToWorld(Vector2 cell)
        {
            Vector2 bottomLeft = (Vector2)transform.position - WorldSize / 2f;
            return new Vector3(
                bottomLeft.x + (cell.x + 0.5f) * cellSize,
                bottomLeft.y + (cell.y + 0.5f) * cellSize,
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
            float noMovesDelay = 0f, float shuffleDuration = 0.01f, float beamSpeed = 1000f,
            float zapInterval = 0f, float chainDelay = 0f, float collapseDuration = 0f)
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
            this.beamSpeed = beamSpeed;
            this.zapInterval = zapInterval;
            this.chainDelay = chainDelay;
            this.collapseDuration = collapseDuration;
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
            IsDetonating = false;
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

        /// <summary>
        /// Gives the piece in <paramref name="cell"/> a power, in the model and on its token. It
        /// keeps its colour, unless the power is a Hypercube, which has none. Call it after
        /// <see cref="LoadLayoutForTests"/>.
        /// </summary>
        internal void SetPowerForTests(Vector2Int cell, PowerKind power)
        {
            Piece piece = power == PowerKind.Hypercube ? Piece.Hypercube : new Piece(model.TypeAt(cell), power);
            model.SetPieceAt(cell.x, cell.y, piece);
            tokens[cell.x, cell.y].SetPiece(piece);
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
