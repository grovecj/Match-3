using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Owns the play area: its dimensions, the cell background tiles, the tokens in each cell,
    /// and the math for converting between grid coordinates (column, row) and world positions.
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

        private Transform cellContainer;
        private Transform tokenContainer;
        private bool needsRebuild;

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
                    // Instantiate clones the prefab into the scene. Passing a component (Token)
                    // instead of a GameObject returns the clone's matching component directly.
                    Token token = Instantiate(tokenPrefab, GridToWorld(x, y), Quaternion.identity, tokenContainer);
                    token.name = $"Token ({x}, {y})";
                    token.transform.localScale = Vector3.one * cellSize;
                    token.SetType(PickTypeWithoutMatch(x, y));
                    tokens[x, y] = token;
                }
            }
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
                if (type != forbiddenHorizontal && type != forbiddenVertical)
                {
                    candidates.Add(type);
                }
            }

            // Random.Range with ints excludes the max, so this returns 0..Count-1: a valid index.
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
