using NUnit.Framework;
using UnityEngine;
using static Match3.BoardInput;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the drag math in <see cref="BoardInput"/>: choosing an axis, which
    /// neighbor a drag points at, how far the token moves (including the edge stretch), whether
    /// letting go swaps, and the pointer speed used to spot flicks. They're static methods taking
    /// plain numbers, so there's no pointer, camera, or board to set up.
    ///
    /// Most numbers below are exact in binary floating point (0.5, 0.25, 0.125, ...), so
    /// "exactly at the threshold" really is exact and the boundary tests aren't at the mercy of
    /// rounding.
    /// </summary>
    // `using static Match3.BoardInput` (above) lets us write ChooseAxis(...) and DragAxis.None
    // instead of BoardInput.ChooseAxis(...) and BoardInput.DragAxis.None.
    public class BoardInputDragTests
    {
        // Same shape as the Inspector defaults, for a 1-unit cell.
        private const float DeadZone = 0.125f;
        private const float Threshold = 0.5f;
        private const float FlickSpeed = 8f;
        private const float EdgeStretch = 0.25f;
        private const float Tolerance = 0.0001f;

        // ---- WantsHint ----

        [TestCase(0f, false)]
        [TestCase(5.75f, false)]
        [TestCase(6f, true)]
        [TestCase(30f, true)]
        public void WantsHint_OnlyOnceTheDelayHasPassed(float idleTime, bool expected)
        {
            Assert.AreEqual(expected, WantsHint(idleTime, hintDelay: 6f));
        }

        [Test]
        public void WantsHint_DelayOfZero_TurnsHintsOff()
        {
            Assert.IsFalse(WantsHint(idleTime: 100f, hintDelay: 0f));
        }

        // ---- ChooseAxis ----

        [Test]
        public void ChooseAxis_InsideDeadZone_IsNone()
        {
            Assert.AreEqual(DragAxis.None, ChooseAxis(Vector2.zero, DeadZone, DragAxis.None));
            Assert.AreEqual(DragAxis.None, ChooseAxis(new Vector2(0.0625f, 0f), DeadZone, DragAxis.None));
        }

        [Test]
        public void ChooseAxis_ExactlyOnDeadZone_CountsAsOutside()
        {
            Assert.AreEqual(DragAxis.Horizontal, ChooseAxis(new Vector2(DeadZone, 0f), DeadZone, DragAxis.None));
        }

        [Test]
        public void ChooseAxis_PicksTheDominantAxis()
        {
            Assert.AreEqual(DragAxis.Horizontal, ChooseAxis(new Vector2(-0.5f, 0.25f), DeadZone, DragAxis.None));
            Assert.AreEqual(DragAxis.Vertical, ChooseAxis(new Vector2(0.25f, -0.5f), DeadZone, DragAxis.None));
        }

        [Test]
        public void ChooseAxis_ExactDiagonal_GoesHorizontal()
        {
            Assert.AreEqual(DragAxis.Horizontal, ChooseAxis(new Vector2(0.5f, 0.5f), DeadZone, DragAxis.None));
        }

        [Test]
        public void ChooseAxis_StaysLockedOutsideTheDeadZone()
        {
            // Locked horizontal, now moving mostly vertically: still horizontal.
            Assert.AreEqual(DragAxis.Horizontal, ChooseAxis(new Vector2(0.125f, 1f), DeadZone, DragAxis.Horizontal));
        }

        [Test]
        public void ChooseAxis_ComingBackInsideTheDeadZone_UnlocksIt()
        {
            Assert.AreEqual(DragAxis.None, ChooseAxis(new Vector2(0.0625f, 0f), DeadZone, DragAxis.Horizontal));
            // ...so the next time out, a different axis can be picked.
            Assert.AreEqual(DragAxis.Vertical, ChooseAxis(new Vector2(0f, 0.5f), DeadZone, DragAxis.None));
        }

        // ---- DirectionAlong ----

        [Test]
        public void DirectionAlong_GivesTheSignAlongTheAxis()
        {
            Assert.AreEqual(Vector2Int.right, DirectionAlong(new Vector2(0.5f, 0.25f), DragAxis.Horizontal));
            Assert.AreEqual(Vector2Int.left, DirectionAlong(new Vector2(-0.5f, 0.25f), DragAxis.Horizontal));
            Assert.AreEqual(Vector2Int.up, DirectionAlong(new Vector2(0.25f, 0.5f), DragAxis.Vertical));
            Assert.AreEqual(Vector2Int.down, DirectionAlong(new Vector2(0.25f, -0.5f), DragAxis.Vertical));
        }

        [Test]
        public void DirectionAlong_IgnoresTheOtherAxis()
        {
            // Locked horizontal, pointer mostly up: still points right (by the small x part).
            Assert.AreEqual(Vector2Int.right, DirectionAlong(new Vector2(0.125f, 1f), DragAxis.Horizontal));
        }

        [Test]
        public void DirectionAlong_NoAxisOrNoMovement_IsZero()
        {
            Assert.AreEqual(Vector2Int.zero, DirectionAlong(new Vector2(0.5f, 0f), DragAxis.None));
            Assert.AreEqual(Vector2Int.zero, DirectionAlong(new Vector2(0f, 0.5f), DragAxis.Horizontal));
        }

        // ---- DragDistance ----

        [Test]
        public void DragDistance_WithNeighbor_FollowsThePointer()
        {
            Assert.AreEqual(0f, DragDistance(0f, true, EdgeStretch));
            Assert.AreEqual(0.5f, DragDistance(0.5f, true, EdgeStretch));
        }

        [Test]
        public void DragDistance_WithNeighbor_IsCappedAtOneCell()
        {
            Assert.AreEqual(1f, DragDistance(1f, true, EdgeStretch));
            Assert.AreEqual(1f, DragDistance(3f, true, EdgeStretch));
        }

        [Test]
        public void DragDistance_NegativeAlong_IsZero()
        {
            Assert.AreEqual(0f, DragDistance(-0.5f, true, EdgeStretch));
            Assert.AreEqual(0f, DragDistance(-0.5f, false, EdgeStretch));
        }

        [Test]
        public void DragDistance_AtTheEdge_StretchesLessThanThePointerMoves()
        {
            float stretch = DragDistance(0.25f, false, EdgeStretch);
            Assert.That(stretch, Is.GreaterThan(0f));
            Assert.That(stretch, Is.LessThan(0.25f), "Stretching toward the edge should resist the pointer.");
        }

        [Test]
        public void DragDistance_AtTheEdge_KeepsGrowingButNeverReachesTheLimit()
        {
            float near = DragDistance(0.25f, false, EdgeStretch);
            float far = DragDistance(1f, false, EdgeStretch);
            float veryFar = DragDistance(3f, false, EdgeStretch);
            Assert.That(far, Is.GreaterThan(near));
            Assert.That(veryFar, Is.GreaterThan(far));
            Assert.That(veryFar, Is.LessThanOrEqualTo(EdgeStretch));
        }

        [Test]
        public void DragDistance_AtTheEdge_WithNoStretch_DoesntMove()
        {
            Assert.AreEqual(0f, DragDistance(1f, false, 0f));
        }

        // ---- ShouldCommit ----

        [Test]
        public void ShouldCommit_PastTheThreshold_Swaps()
        {
            Assert.IsTrue(ShouldCommit(0.75f, 0f, true, Threshold, FlickSpeed));
        }

        [Test]
        public void ShouldCommit_ExactlyAtTheThreshold_Swaps()
        {
            Assert.IsTrue(ShouldCommit(Threshold, 0f, true, Threshold, FlickSpeed));
        }

        [Test]
        public void ShouldCommit_ShortAndSlow_SpringsBack()
        {
            Assert.IsFalse(ShouldCommit(0.25f, 1f, true, Threshold, FlickSpeed));
        }

        [Test]
        public void ShouldCommit_ShortButFast_IsAFlick()
        {
            Assert.IsTrue(ShouldCommit(0.25f, FlickSpeed, true, Threshold, FlickSpeed));
            Assert.IsTrue(ShouldCommit(0.25f, 20f, true, Threshold, FlickSpeed));
        }

        [Test]
        public void ShouldCommit_FastAwayFromTheNeighbor_IsNotAFlick()
        {
            // Dragged out, then whipped back toward the start: the speed toward the neighbor is negative.
            Assert.IsFalse(ShouldCommit(0.25f, -20f, true, Threshold, FlickSpeed));
        }

        [Test]
        public void ShouldCommit_WithNoNeighbor_NeverSwaps()
        {
            Assert.IsFalse(ShouldCommit(1f, 20f, false, Threshold, FlickSpeed));
        }

        [Test]
        public void ShouldCommit_NotMoved_NeverSwaps()
        {
            // Back inside the dead zone at release: even a fast pointer doesn't count.
            Assert.IsFalse(ShouldCommit(0f, 20f, true, Threshold, FlickSpeed));
        }

        // ---- SmoothVelocity ----

        [Test]
        public void SmoothVelocity_NoTimePassed_KeepsTheOldVelocity()
        {
            Vector2 old = new Vector2(3f, -2f);
            Assert.AreEqual(old, SmoothVelocity(old, new Vector2(1f, 1f), 0f, 0.05f));
        }

        [Test]
        public void SmoothVelocity_NoSmoothing_IsThisFramesVelocity()
        {
            Vector2 result = SmoothVelocity(Vector2.zero, new Vector2(0.5f, 0f), 0.25f, 0f);
            Assert.AreEqual(2f, result.x, Tolerance);
            Assert.AreEqual(0f, result.y, Tolerance);
        }

        [Test]
        public void SmoothVelocity_MovesPartWayTowardThisFramesVelocity()
        {
            // From rest, one frame at 10 units/s: somewhere between 0 and 10.
            Vector2 result = SmoothVelocity(Vector2.zero, new Vector2(0.1f, 0f), 0.01f, 0.05f);
            Assert.That(result.x, Is.GreaterThan(0f).And.LessThan(10f));
        }

        [Test]
        public void SmoothVelocity_SteadyMotion_ConvergesOnTheRealSpeed()
        {
            Vector2 velocity = Vector2.zero;
            // 60 frames at 60 fps, moving 10 units/s the whole time.
            for (int i = 0; i < 60; i++)
            {
                velocity = SmoothVelocity(velocity, new Vector2(10f / 60f, 0f), 1f / 60f, 0.05f);
            }
            Assert.AreEqual(10f, velocity.x, 0.01f);
        }

        [Test]
        public void SmoothVelocity_IsFrameRateIndependent()
        {
            // The same steady motion for 0.05 s, at 60 fps and at 240 fps, should end up with
            // (almost) the same average.
            Vector2 at60 = Vector2.zero;
            for (int i = 0; i < 3; i++)
            {
                at60 = SmoothVelocity(at60, new Vector2(10f / 60f, 0f), 1f / 60f, 0.05f);
            }
            Vector2 at240 = Vector2.zero;
            for (int i = 0; i < 12; i++)
            {
                at240 = SmoothVelocity(at240, new Vector2(10f / 240f, 0f), 1f / 240f, 0.05f);
            }
            Assert.AreEqual(at60.x, at240.x, 0.01f);
        }
    }
}
