using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>Edit Mode tests for <see cref="LayoutRules"/>, the portrait/landscape layout rules (issue #114).</summary>
    public class LayoutRulesTests
    {
        private const float Tolerance = 0.01f;

        // An iPhone-sized portrait screen in CSS pixels (the issue's acceptance size), and the
        // same phone turned on its side.
        private static readonly Vector2 Phone = new Vector2(390, 844);
        private static readonly Vector2 PhoneLandscape = new Vector2(844, 390);
        private static readonly Vector2 Desktop = new Vector2(1920, 1080);

        private static Rect Full(Vector2 screen) => new Rect(Vector2.zero, screen);

        // ---- Landscape or portrait --------------------------------------------------------

        [Test]
        public void ChooseLayout_WiderThanTall_IsLandscape()
        {
            Assert.AreEqual(ScreenLayout.Landscape, LayoutRules.ChooseLayout(Desktop));
            Assert.AreEqual(ScreenLayout.Landscape, LayoutRules.ChooseLayout(PhoneLandscape));
        }

        [Test]
        public void ChooseLayout_TallerThanWide_IsPortrait()
        {
            Assert.AreEqual(ScreenLayout.Portrait, LayoutRules.ChooseLayout(Phone));
        }

        [Test]
        public void ChooseLayout_Square_IsLandscape()
        {
            Assert.AreEqual(ScreenLayout.Landscape, LayoutRules.ChooseLayout(new Vector2(800, 800)));
        }

        [Test]
        public void ChooseLayout_OnePixelEitherSideOfSquare()
        {
            // The threshold is exactly width == height: one pixel taller tips it into portrait.
            Assert.AreEqual(ScreenLayout.Landscape, LayoutRules.ChooseLayout(new Vector2(801, 800)));
            Assert.AreEqual(ScreenLayout.Portrait, LayoutRules.ChooseLayout(new Vector2(800, 801)));
        }

        // ---- Canvas scale -----------------------------------------------------------------

        [Test]
        public void CanvasScale_LandscapeMatchesHeight_PortraitMatchesWidth()
        {
            Assert.AreEqual(1f, LayoutRules.CanvasScale(Desktop, ScreenLayout.Landscape), Tolerance);
            Assert.AreEqual(390f / 1080f, LayoutRules.CanvasScale(PhoneLandscape, ScreenLayout.Landscape), Tolerance);
            Assert.AreEqual(390f / 1080f, LayoutRules.CanvasScale(Phone, ScreenLayout.Portrait), Tolerance);
        }

        // ---- Board and panel rects --------------------------------------------------------

        [Test]
        public void Choose_Desktop_PanelOnTheRightAsBefore()
        {
            LayoutResult result = LayoutRules.Choose(Desktop, Full(Desktop));

            Assert.AreEqual(ScreenLayout.Landscape, result.Layout);
            // At 1080 tall the scale is 1, so the panel is its designed 447 pixels wide.
            AssertRect(new Rect(1920 - 447, 0, 447, 1080), result.PanelRect);
            AssertRect(new Rect(0, 0, 1920 - 447, 1080), result.BoardRect);
        }

        [Test]
        public void Choose_Phone_BoardFullWidthWithPanelBelow()
        {
            LayoutResult result = LayoutRules.Choose(Phone, Full(Phone));

            Assert.AreEqual(ScreenLayout.Portrait, result.Layout);
            float panelHeight = LayoutRules.PortraitPanelHeight * 390f / 1080f;
            AssertRect(new Rect(0, 0, 390, panelHeight), result.PanelRect);
            AssertRect(new Rect(0, panelHeight, 390, 844 - panelHeight), result.BoardRect);
        }

        [Test]
        public void Choose_Phone_PanelTakesAtMostThirtyPercentOfTheHeight()
        {
            LayoutResult result = LayoutRules.Choose(Phone, Full(Phone));
            Assert.That(result.PanelRect.height / Phone.y, Is.LessThanOrEqualTo(0.3f));
        }

        [Test]
        public void Choose_PhoneLandscape_BoardStaysBesideThePanel()
        {
            LayoutResult result = LayoutRules.Choose(PhoneLandscape, Full(PhoneLandscape));

            Assert.AreEqual(ScreenLayout.Landscape, result.Layout);
            float panelWidth = LayoutRules.LandscapePanelWidth * 390f / 1080f;
            AssertRect(new Rect(0, 0, 844 - panelWidth, 390), result.BoardRect);
            // The board and panel meet without overlapping.
            Assert.AreEqual(result.PanelRect.xMin, result.BoardRect.xMax, Tolerance);
        }

        [Test]
        public void Choose_HugePanel_LeavesTheBoardItsMinimumShare()
        {
            // A tall landscape screen with a narrow safe area: the ~830 px panel is wider than
            // the 500 px safe area, so without the minimum the board would have no room.
            var screen = new Vector2(2100, 2000);
            LayoutResult result = LayoutRules.Choose(screen, new Rect(0, 0, 500, 2000));

            Assert.AreEqual(500 * LayoutRules.MinBoardShare, result.BoardRect.width, Tolerance);
        }

        // ---- Safe area --------------------------------------------------------------------

        [Test]
        public void Choose_PortraitSafeArea_KeepsBoardOffTheNotchAndPanelOffTheHomeIndicator()
        {
            // 47 px notch at the top, 34 px home indicator at the bottom (an iPhone in points).
            var safe = new Rect(0, 34, 390, 844 - 34 - 47);
            LayoutResult result = LayoutRules.Choose(Phone, safe);

            Assert.AreEqual(34f, result.PanelRect.yMin, Tolerance);
            Assert.AreEqual(844f - 47f, result.BoardRect.yMax, Tolerance);
            Assert.AreEqual(result.PanelRect.yMax, result.BoardRect.yMin, Tolerance);
        }

        [Test]
        public void Choose_LandscapeSafeArea_KeepsEverythingClearOfTheSides()
        {
            // Notch on the left, home indicator side-on: insets left and right.
            var safe = new Rect(47, 21, 844 - 47 - 47, 390 - 21);
            LayoutResult result = LayoutRules.Choose(PhoneLandscape, safe);

            Assert.AreEqual(47f, result.BoardRect.xMin, Tolerance);
            Assert.AreEqual(844f - 47f, result.PanelRect.xMax, Tolerance);
            Assert.AreEqual(21f, result.PanelRect.yMin, Tolerance);
            Assert.AreEqual(21f, result.BoardRect.yMin, Tolerance);
        }

        [Test]
        public void Choose_SafeAreaOutsideTheScreen_IsClampedToIt()
        {
            LayoutResult result = LayoutRules.Choose(Phone, new Rect(-10, -10, 500, 900));
            AssertRect(Full(Phone), result.SafeArea);
        }

        [Test]
        public void Choose_EmptySafeArea_UsesTheWholeScreen()
        {
            LayoutResult result = LayoutRules.Choose(Phone, new Rect(0, 0, 0, 0));
            AssertRect(Full(Phone), result.SafeArea);
        }

        // ---- Fitting the camera -----------------------------------------------------------

        [Test]
        public void FitOrthographicSize_WholeScreen_FitsTheTallerAxis()
        {
            // A 10x10 board (5 half extents) on a 1920x1080 screen is limited by the height,
            // so the camera shows exactly 10 units top to bottom.
            float size = LayoutRules.FitOrthographicSize(new Vector2(5, 5), Full(Desktop), 1080);
            Assert.AreEqual(5f, size, Tolerance);
        }

        [Test]
        public void FitOrthographicSize_PortraitBoardRect_FitsTheWidth()
        {
            LayoutResult result = LayoutRules.Choose(Phone, Full(Phone));
            float size = LayoutRules.FitOrthographicSize(new Vector2(5, 5), result.BoardRect, Phone.y);

            // Width-limited: the board's 10 units span the 390 px of width.
            float pixelsPerUnit = Phone.y / (2f * size);
            Assert.AreEqual(390f, 10f * pixelsPerUnit, Tolerance);
        }

        [Test]
        public void CameraOffset_MatchesTheOldRightPanelMaths()
        {
            // Before #114 the camera moved right by size * aspect * panelFraction.
            LayoutResult result = LayoutRules.Choose(Desktop, Full(Desktop));
            float size = LayoutRules.FitOrthographicSize(new Vector2(5, 5), result.BoardRect, Desktop.y);
            Vector2 offset = LayoutRules.CameraOffset(result.BoardRect, Desktop, size);

            float aspect = Desktop.x / Desktop.y;
            Assert.AreEqual(size * aspect * (447f / 1920f), offset.x, Tolerance);
            Assert.AreEqual(0f, offset.y, Tolerance);
        }

        [Test]
        public void CameraOffset_Portrait_MovesTheCameraDownSoTheBoardSitsAboveThePanel()
        {
            LayoutResult result = LayoutRules.Choose(Phone, Full(Phone));
            float size = LayoutRules.FitOrthographicSize(new Vector2(5, 5), result.BoardRect, Phone.y);
            Vector2 offset = LayoutRules.CameraOffset(result.BoardRect, Phone, size);

            Assert.AreEqual(0f, offset.x, Tolerance);
            Assert.That(offset.y, Is.LessThan(0f));
        }

        private static void AssertRect(Rect expected, Rect actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, "x");
            Assert.AreEqual(expected.y, actual.y, Tolerance, "y");
            Assert.AreEqual(expected.width, actual.width, Tolerance, "width");
            Assert.AreEqual(expected.height, actual.height, Tolerance, "height");
        }
    }
}
