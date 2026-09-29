using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="Board"/>. Edit Mode tests run inside the Editor without
    /// entering Play Mode, so they're fast, but Unity's per-frame callbacks (Start, Update...)
    /// never run. That makes them a good fit for plain logic like bounds checks.
    /// </summary>
    public class BoardTests
    {
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
        // have to be removed with DestroyImmediate; Destroy only works while the game is running.
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(boardObject);
        }

        [Test]
        public void IsInBounds_CornersOfDefaultBoard_AreInBounds()
        {
            // A fresh Board uses its default 8x8 size, so valid cells run from (0,0) to (7,7).
            Assert.IsTrue(board.IsInBounds(new Vector2Int(0, 0)));
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
    }
}
