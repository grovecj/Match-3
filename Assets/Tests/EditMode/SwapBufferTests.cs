using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="SwapBuffer"/>: the rules for the one swap the player can
    /// make while the board is resolving (keep, replace, cancel, discard). It's a plain C# class,
    /// so there's no board or pointer to set up. That the swap really runs when the board
    /// settles is checked by the Play Mode tests.
    /// </summary>
    public class SwapBufferTests
    {
        private static readonly Vector2Int A = new Vector2Int(2, 2);
        private static readonly Vector2Int RightOfA = new Vector2Int(3, 2);
        private static readonly Vector2Int AboveA = new Vector2Int(2, 3);
        private static readonly Vector2Int FarAway = new Vector2Int(6, 6);

        private SwapBuffer buffer;

        [SetUp]
        public void SetUp()
        {
            buffer = new SwapBuffer();
        }

        // ---- Keep ----

        [Test]
        public void NewBuffer_IsEmpty_AndHasNothingToTake()
        {
            Assert.IsTrue(buffer.IsEmpty);
            Assert.IsFalse(buffer.TryTake(out _, out _));
        }

        [Test]
        public void Record_KeepsTheSwap_UntilItIsTaken()
        {
            Assert.IsTrue(buffer.Record(A, RightOfA));
            Assert.IsTrue(buffer.HasSwap);
            Assert.IsTrue(buffer.IsMarked(A));
            Assert.IsTrue(buffer.IsMarked(RightOfA));

            Assert.IsTrue(buffer.TryTake(out Vector2Int first, out Vector2Int second));
            Assert.AreEqual(A, first, "The cell the swap started from comes first.");
            Assert.AreEqual(RightOfA, second);

            Assert.IsFalse(buffer.HasSwap, "A swap runs once.");
            Assert.IsFalse(buffer.TryTake(out _, out _));
            Assert.IsFalse(buffer.IsMarked(A), "A swap that ran is no longer marked.");
        }

        [Test]
        public void Record_RefusesCellsThatAreNotNeighbors()
        {
            Assert.IsFalse(buffer.Record(A, FarAway));
            Assert.IsFalse(buffer.Record(A, new Vector2Int(3, 3)), "Diagonal cells aren't neighbors.");
            Assert.IsFalse(buffer.Record(A, A));
            Assert.IsTrue(buffer.IsEmpty);
        }

        [Test]
        public void ClickThenClickOnANeighbor_RecordsTheSwap_FromTheFirstClick()
        {
            buffer.Click(A);
            Assert.AreEqual(A, buffer.Selected);
            Assert.IsFalse(buffer.HasSwap);

            buffer.Click(AboveA);

            Assert.IsTrue(buffer.TryTake(out Vector2Int first, out Vector2Int second));
            Assert.AreEqual(A, first);
            Assert.AreEqual(AboveA, second);
            Assert.IsNull(buffer.Selected, "Making the swap ends the selection.");
        }

        [Test]
        public void ClickThenClickFarAway_MovesTheSelection()
        {
            buffer.Click(A);
            buffer.Click(FarAway);

            Assert.AreEqual(FarAway, buffer.Selected);
            Assert.IsFalse(buffer.HasSwap);
        }

        [Test]
        public void TryTake_LeavesASelectionMadeAfterTheSwap()
        {
            buffer.Record(A, RightOfA);
            buffer.Click(FarAway);

            Assert.IsTrue(buffer.TryTake(out _, out _));
            Assert.AreEqual(FarAway, buffer.Selected, "The player may still be making their next swap.");
        }

        // ---- Replace ----

        [Test]
        public void SecondSwap_ReplacesTheFirst()
        {
            buffer.Record(A, RightOfA);
            buffer.Record(FarAway, new Vector2Int(6, 5));

            Assert.IsTrue(buffer.TryTake(out Vector2Int first, out Vector2Int second));
            Assert.AreEqual(FarAway, first);
            Assert.AreEqual(new Vector2Int(6, 5), second);
            Assert.IsFalse(buffer.IsMarked(A), "The replaced swap's cells are no longer marked.");
            Assert.IsFalse(buffer.TryTake(out _, out _), "Only one swap is kept.");
        }

        [Test]
        public void SecondSwapByClicks_ReplacesTheFirst()
        {
            buffer.Record(A, RightOfA);
            buffer.Click(FarAway);
            buffer.Click(new Vector2Int(5, 6));

            Assert.IsTrue(buffer.TryTake(out Vector2Int first, out Vector2Int second));
            Assert.AreEqual(FarAway, first);
            Assert.AreEqual(new Vector2Int(5, 6), second);
        }

        // ---- Cancel ----

        [Test]
        public void ClickingAMarkedSwapCell_CancelsIt()
        {
            buffer.Record(A, RightOfA);
            buffer.Click(RightOfA);

            Assert.IsTrue(buffer.IsEmpty);
            Assert.IsNull(buffer.Selected, "The cancelling click doesn't also select the cell.");
        }

        [Test]
        public void ClickingTheSelectedCellAgain_Deselects()
        {
            buffer.Click(A);
            buffer.Click(A);

            Assert.IsTrue(buffer.IsEmpty);
        }

        [Test]
        public void PressingOutsideTheBoard_CancelsTheSwapAndTheSelection()
        {
            buffer.Record(A, RightOfA);
            buffer.Click(FarAway);

            buffer.PressOutside();

            Assert.IsTrue(buffer.IsEmpty);
            Assert.IsFalse(buffer.TryTake(out _, out _));
        }

        // ---- Discard ----
        //
        // Every reason the game throws the swap away (a pause, a reshuffle, the clock reaching
        // 0:00, the Last Hurrah, input turned off) calls Discard: BoardInput's OnDisable for all
        // but the reshuffle, which comes from Board.NoMovesLeft. Each one must leave nothing to
        // run afterwards. The Play Mode tests check that those two calls happen.

        [TestCase("pause")]
        [TestCase("reshuffle")]
        [TestCase("clock reached 0:00")]
        [TestCase("Last Hurrah")]
        [TestCase("input turned off")]
        public void Discard_LeavesNothingToRun(string reason)
        {
            buffer.Record(A, RightOfA);
            buffer.Click(FarAway);

            buffer.Discard();

            Assert.IsTrue(buffer.IsEmpty, $"After a {reason}, nothing should be kept.");
            Assert.IsFalse(buffer.TryTake(out _, out _), $"After a {reason}, no swap should run.");
            Assert.IsFalse(buffer.IsMarked(A), $"After a {reason}, no cell should stay marked.");
        }
    }
}
