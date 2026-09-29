using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="Board"/>. Edit Mode tests run inside the Editor without
    /// entering Play Mode, so they're fast, but Unity's per-frame callbacks (Start, Update...)
    /// never run. That makes them a good fit for plain logic like bounds checks and grid math.
    /// </summary>
    public class BoardTests
    {
        // Floats pick up tiny rounding errors (0.1 + 0.2 isn't exactly 0.3), so positions are
        // compared "within a tolerance" instead of with ==. 0.0001 world units is far smaller
        // than anything that would matter on screen.
        private const float Tolerance = 0.0001f;

        // Vector3EqualityComparer ships with Unity's Test Framework. Used with NUnit's
        // Is.EqualTo(...).Using(...), it compares each axis within the given tolerance and
        // prints both vectors in the failure message.
        private static readonly Vector3EqualityComparer Vector3Comparer = new Vector3EqualityComparer(Tolerance);

        private GameObject boardObject;
        private Board board;

        // [SetUp] runs before every [Test], so each test gets a brand-new Board and
        // one test can't leave behind state that affects another.
        [SetUp]
        public void SetUp()
        {
            boardObject = new GameObject("Board");
            board = boardObject.AddComponent<Board>();
        }

        // [TearDown] runs after every [Test], even a failing one. Outside Play Mode, objects
        // have to be removed with DestroyImmediate: Destroy only schedules removal for the end
        // of the current frame, and in Edit Mode there's no game loop to reach that point
        // (Unity logs an error if you try). Destroying the parent also destroys its components.
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(boardObject);
        }

        /// <summary>
        /// Sets the Board's private width, height and cell size.
        /// </summary>
        // Those fields are private [SerializeField]s, so tests can't assign them directly. Two
        // options: add a test-only public setter to Board, or go through SerializedObject. We use
        // SerializedObject because it's exactly how the Inspector edits the fields, so Board's
        // public API stays unchanged. The catch is that fields are looked up by name as strings,
        // so renaming one in Board would silently break this; the Assert.IsNotNull guards turn
        // that into a clear failure instead of a NullReferenceException.
        // SerializedObject lives in the UnityEditor namespace, which is fine here: this test
        // assembly only compiles for the Editor (see Match3.Tests.EditMode.asmdef).
        private void ConfigureBoard(int width, int height, float cellSize)
        {
            var serialized = new SerializedObject(board);
            SerializedProperty widthProperty = serialized.FindProperty("width");
            SerializedProperty heightProperty = serialized.FindProperty("height");
            SerializedProperty cellSizeProperty = serialized.FindProperty("cellSize");
            Assert.IsNotNull(widthProperty, "Board has no serialized field named 'width'.");
            Assert.IsNotNull(heightProperty, "Board has no serialized field named 'height'.");
            Assert.IsNotNull(cellSizeProperty, "Board has no serialized field named 'cellSize'.");

            widthProperty.intValue = width;
            heightProperty.intValue = height;
            cellSizeProperty.floatValue = cellSize;
            // Writes the changed values back onto the Board. "WithoutUndo" skips recording an
            // Undo step, which a test has no use for.
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- IsInBounds

        [Test]
        public void IsInBounds_CornersOfDefaultBoard_AreInBounds()
        {
            // A fresh Board uses its default 8x8 size, so valid cells run from (0,0) to (7,7).
            Assert.IsTrue(board.IsInBounds(new Vector2Int(0, 0)));
            Assert.IsTrue(board.IsInBounds(new Vector2Int(board.Width - 1, 0)));
            Assert.IsTrue(board.IsInBounds(new Vector2Int(0, board.Height - 1)));
            Assert.IsTrue(board.IsInBounds(new Vector2Int(board.Width - 1, board.Height - 1)));
        }

        [Test]
        public void IsInBounds_CellsJustOutsideBoard_AreOutOfBounds()
        {
            Assert.IsFalse(board.IsInBounds(new Vector2Int(-1, 0)));
            Assert.IsFalse(board.IsInBounds(new Vector2Int(0, -1)));
            Assert.IsFalse(board.IsInBounds(new Vector2Int(board.Width, 0)));
            Assert.IsFalse(board.IsInBounds(new Vector2Int(0, board.Height)));
        }

        // [TestCase] turns one test method into several: NUnit runs it once per attribute,
        // passing the listed values as arguments, and shows each run separately in the
        // Test Runner. Here every case uses a non-default 6x4 board.
        [TestCase(0, 0, true, TestName = "IsInBounds_6x4_BottomLeftCorner")]
        [TestCase(5, 0, true, TestName = "IsInBounds_6x4_BottomRightCorner")]
        [TestCase(0, 3, true, TestName = "IsInBounds_6x4_TopLeftCorner")]
        [TestCase(5, 3, true, TestName = "IsInBounds_6x4_TopRightCorner")]
        [TestCase(-1, 2, false, TestName = "IsInBounds_6x4_JustLeft")]
        [TestCase(6, 2, false, TestName = "IsInBounds_6x4_JustRight")]
        [TestCase(2, -1, false, TestName = "IsInBounds_6x4_JustBelow")]
        [TestCase(2, 4, false, TestName = "IsInBounds_6x4_JustAbove")]
        [TestCase(-5, -5, false, TestName = "IsInBounds_6x4_FarNegative")]
        [TestCase(4, 5, false, TestName = "IsInBounds_6x4_ValidColumnButRowTooHigh")]
        public void IsInBounds_NonSquareBoard(int x, int y, bool expected)
        {
            ConfigureBoard(width: 6, height: 4, cellSize: 1f);

            Assert.AreEqual(expected, board.IsInBounds(new Vector2Int(x, y)));
        }

        // ---------------------------------------------------------------- GridToWorld

        [Test]
        public void GridToWorld_DefaultBoardAtOrigin_CornerCellsAreHalfACellInFromTheEdges()
        {
            // 8x8 cells of size 1, centered on (0,0): the board spans -4..4 on both axes,
            // so the corner cells' centers are half a cell in from the edges.
            Assert.That(board.GridToWorld(0, 0), Is.EqualTo(new Vector3(-3.5f, -3.5f, 0f)).Using(Vector3Comparer));
            Assert.That(board.GridToWorld(7, 7), Is.EqualTo(new Vector3(3.5f, 3.5f, 0f)).Using(Vector3Comparer));
        }

        [Test]
        public void GridToWorld_MovedResizedBoard_CornerCellsFollowPositionAndCellSize()
        {
            // 6x4 cells of 0.5 units = a 3x2 board. Centered on (3, -2, 1), its bottom-left
            // corner is at (1.5, -3) and its top-right corner at (4.5, -1).
            ConfigureBoard(width: 6, height: 4, cellSize: 0.5f);
            boardObject.transform.position = new Vector3(3f, -2f, 1f);

            // Half a cell (0.25) in from each corner. The z always matches the board's z.
            Assert.That(board.GridToWorld(0, 0), Is.EqualTo(new Vector3(1.75f, -2.75f, 1f)).Using(Vector3Comparer));
            Assert.That(board.GridToWorld(5, 3), Is.EqualTo(new Vector3(4.25f, -1.25f, 1f)).Using(Vector3Comparer));
        }

        [Test]
        public void GridToWorld_MovingTheBoard_ShiftsEveryCellByTheSameAmount()
        {
            Vector3 before = board.GridToWorld(2, 5);
            Vector3 offset = new Vector3(10f, -7f, 2f);
            boardObject.transform.position += offset;

            Assert.That(board.GridToWorld(2, 5), Is.EqualTo(before + offset).Using(Vector3Comparer));
        }

        [TestCase(0.5f)]
        [TestCase(1f)]
        [TestCase(1.25f)]
        [TestCase(2f)]
        public void GridToWorld_NeighboringCells_AreOneCellSizeApart(float cellSize)
        {
            ConfigureBoard(width: 5, height: 5, cellSize: cellSize);

            Vector3 cell = board.GridToWorld(2, 2);
            // Assert.AreEqual(expected, actual, delta) is NUnit's float-with-tolerance check.
            Assert.AreEqual(cellSize, board.GridToWorld(3, 2).x - cell.x, Tolerance);
            Assert.AreEqual(cellSize, board.GridToWorld(2, 3).y - cell.y, Tolerance);
        }

        // ---------------------------------------------------------------- TryWorldToGrid

        // Round trip: the center of every cell should convert back to that same cell.
        // Covers the default board plus non-default sizes, cell sizes and positions.
        [TestCase(8, 8, 1f, 0f, 0f)]
        [TestCase(6, 4, 0.5f, 3f, -2f)]
        [TestCase(5, 7, 1.25f, -10.3f, 4.7f)]
        public void TryWorldToGrid_CenterOfEveryCell_RoundTripsWithGridToWorld(
            int width, int height, float cellSize, float boardX, float boardY)
        {
            ConfigureBoard(width, height, cellSize);
            boardObject.transform.position = new Vector3(boardX, boardY, 0f);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool found = board.TryWorldToGrid(board.GridToWorld(x, y), out Vector2Int cell);

                    // The message is shown only on failure, so we know which cell broke.
                    Assert.IsTrue(found, $"Center of cell ({x}, {y}) was reported as off the board.");
                    Assert.AreEqual(new Vector2Int(x, y), cell);
                }
            }
        }

        // Points exactly on a line between cells. TryWorldToGrid rounds down (FloorToInt),
        // so a point on a shared edge belongs to the cell above/right of it. The board used
        // here (6x4, cell size 0.5, centered on (3, -2)) spans x 1.5..4.5 and y -3..-1. These
        // numbers are all exact in binary floating point, so the edges land exactly on the
        // line with no rounding error to tip them either way.
        [TestCase(1.5f, -3f, 0, 0, TestName = "TryWorldToGrid_BottomLeftCornerOfBoard_IsFirstCell")]
        [TestCase(2f, -2.9f, 1, 0, TestName = "TryWorldToGrid_VerticalEdgeBetweenColumns_BelongsToRightColumn")]
        [TestCase(1.6f, -2.5f, 0, 1, TestName = "TryWorldToGrid_HorizontalEdgeBetweenRows_BelongsToUpperRow")]
        [TestCase(3f, -2f, 3, 2, TestName = "TryWorldToGrid_BoardCenter_IsCellUpRightOfCenter")]
        [TestCase(4.4f, -1.1f, 5, 3, TestName = "TryWorldToGrid_JustInsideTopRightCorner_IsLastCell")]
        public void TryWorldToGrid_PointsOnCellEdges(float worldX, float worldY, int expectedX, int expectedY)
        {
            ConfigureBoard(width: 6, height: 4, cellSize: 0.5f);
            boardObject.transform.position = new Vector3(3f, -2f, 0f);

            bool found = board.TryWorldToGrid(new Vector3(worldX, worldY, 0f), out Vector2Int cell);

            Assert.IsTrue(found);
            Assert.AreEqual(new Vector2Int(expectedX, expectedY), cell);
        }

        // Same 6x4 board spanning x 1.5..4.5, y -3..-1. The right and top edges are *outside*:
        // rounding down puts them in column 6 / row 4, which don't exist.
        [TestCase(1.4f, -2f, TestName = "TryWorldToGrid_LeftOfBoard_ReturnsFalse")]
        [TestCase(4.5f, -2f, TestName = "TryWorldToGrid_OnRightEdge_ReturnsFalse")]
        [TestCase(3f, -3.1f, TestName = "TryWorldToGrid_BelowBoard_ReturnsFalse")]
        [TestCase(3f, -1f, TestName = "TryWorldToGrid_OnTopEdge_ReturnsFalse")]
        [TestCase(0f, 0f, TestName = "TryWorldToGrid_WorldOriginOutsideMovedBoard_ReturnsFalse")]
        [TestCase(-100f, -100f, TestName = "TryWorldToGrid_FarAway_ReturnsFalse")]
        public void TryWorldToGrid_PointsOutsideBoard(float worldX, float worldY)
        {
            ConfigureBoard(width: 6, height: 4, cellSize: 0.5f);
            boardObject.transform.position = new Vector3(3f, -2f, 0f);

            Assert.IsFalse(board.TryWorldToGrid(new Vector3(worldX, worldY, 0f), out _));
        }

        [Test]
        public void TryWorldToGrid_IgnoresZ()
        {
            // The board is 2D: a point in front of or behind it still maps to the cell it covers.
            Vector3 center = board.GridToWorld(4, 1);

            bool found = board.TryWorldToGrid(center + new Vector3(0f, 0f, -10f), out Vector2Int cell);

            Assert.IsTrue(found);
            Assert.AreEqual(new Vector2Int(4, 1), cell);
        }

        // ---------------------------------------------------------------- TrySwap rejection

        // Only the rejection paths are tested here. A *valid* swap starts a coroutine and
        // needs a filled board (tokens are created in Start, which never runs in Edit Mode),
        // so it belongs in Play Mode tests. Every case below is rejected before the Board
        // looks at its tokens.
        [TestCase(3, 3, 4, 4, TestName = "TrySwap_DiagonalUpRight_ReturnsFalse")]
        [TestCase(3, 3, 2, 4, TestName = "TrySwap_DiagonalUpLeft_ReturnsFalse")]
        [TestCase(3, 3, 5, 3, TestName = "TrySwap_TwoCellsApartHorizontally_ReturnsFalse")]
        [TestCase(3, 3, 3, 0, TestName = "TrySwap_ThreeCellsApartVertically_ReturnsFalse")]
        [TestCase(3, 3, 3, 3, TestName = "TrySwap_SameCell_ReturnsFalse")]
        [TestCase(0, 0, -1, 0, TestName = "TrySwap_OffLeftEdge_ReturnsFalse")]
        [TestCase(0, 0, 0, -1, TestName = "TrySwap_OffBottomEdge_ReturnsFalse")]
        [TestCase(5, 2, 6, 2, TestName = "TrySwap_OffRightEdge_ReturnsFalse")]
        [TestCase(1, 3, 1, 4, TestName = "TrySwap_OffTopEdge_ReturnsFalse")]
        [TestCase(-1, 0, -2, 0, TestName = "TrySwap_BothCellsOffBoard_ReturnsFalse")]
        public void TrySwap_InvalidPair(int ax, int ay, int bx, int by)
        {
            // Non-square 6x4 board, so "off the right/top edge" differs from the default 8x8.
            ConfigureBoard(width: 6, height: 4, cellSize: 1f);

            Assert.IsFalse(board.TrySwap(new Vector2Int(ax, ay), new Vector2Int(bx, by)));
            Assert.IsFalse(board.IsResolving, "A rejected swap must not lock the board.");
        }
    }
}
