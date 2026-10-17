using UnityEngine;

namespace Match3
{
    /// <summary>The two arrangements of the screen (issue #114).</summary>
    public enum ScreenLayout
    {
        /// <summary>Wider than tall (or square): the side panel is docked to the right.</summary>
        Landscape,
        /// <summary>Taller than wide: the panel is docked to the bottom, under the board.</summary>
        Portrait,
    }

    /// <summary>
    /// What <see cref="LayoutRules.Choose"/> decided for one screen size. All rects are in
    /// screen pixels with the origin at the bottom left, the same as <c>Screen.safeArea</c>
    /// and pointer positions.
    /// </summary>
    public readonly struct LayoutResult
    {
        public readonly ScreenLayout Layout;
        /// <summary>Screen pixels per canvas unit: what the Canvas Scaler's scale factor will be.</summary>
        public readonly float Scale;
        /// <summary>The safe area, clamped to the screen. Nothing important goes outside it.</summary>
        public readonly Rect SafeArea;
        /// <summary>Where the side panel goes.</summary>
        public readonly Rect PanelRect;
        /// <summary>What's left for the board: the safe area minus the panel.</summary>
        public readonly Rect BoardRect;

        public LayoutResult(ScreenLayout layout, float scale, Rect safeArea, Rect panelRect, Rect boardRect)
        {
            Layout = layout;
            Scale = scale;
            SafeArea = safeArea;
            PanelRect = panelRect;
            BoardRect = boardRect;
        }
    }

    /// <summary>
    /// The rules for laying out the screen, as plain functions with no scene or MonoBehaviour,
    /// so Edit Mode tests can check them for any screen size. <see cref="ResponsiveLayout"/>
    /// applies the result to the UI, and <see cref="BoardCamera"/> fits the board into
    /// <see cref="LayoutResult.BoardRect"/>.
    /// </summary>
    public static class LayoutRules
    {
        // The Canvas Scaler's "reference resolution" is the screen size the UI was designed
        // for. Landscape keeps the original 1920x1080 and matches height; portrait turns it on
        // its side and matches width, so on a phone 1080 canvas units span the screen's width
        // and text and buttons are sized for that.
        public static readonly Vector2 LandscapeReference = new Vector2(1920, 1080);
        public static readonly Vector2 PortraitReference = new Vector2(1080, 1920);

        /// <summary>Width of the side panel in landscape, in canvas units (as built in Game.unity).</summary>
        public const float LandscapePanelWidth = 447f;

        /// <summary>
        /// Height of the panel in portrait, in canvas units: a score/time row, the round-over
        /// message, the Start Round button and a row of four buttons, with margins.
        /// </summary>
        public const float PortraitPanelHeight = 520f;

        /// <summary>
        /// The board always keeps at least this share of the safe area's width (landscape) or
        /// height (portrait), so an oversized panel can't leave it no room at all.
        /// </summary>
        public const float MinBoardShare = 0.1f;

        /// <summary>
        /// Portrait when the screen is taller than it is wide; landscape otherwise, so a
        /// square screen keeps the desktop layout.
        /// </summary>
        public static ScreenLayout ChooseLayout(Vector2 screenSize)
        {
            return screenSize.x >= screenSize.y ? ScreenLayout.Landscape : ScreenLayout.Portrait;
        }

        /// <summary>
        /// The Canvas Scaler's scale factor for <paramref name="layout"/>. With "Match" set fully
        /// to height (landscape) or width (portrait), the scaler just divides that side of the
        /// screen by the same side of the reference resolution.
        /// </summary>
        public static float CanvasScale(Vector2 screenSize, ScreenLayout layout)
        {
            return layout == ScreenLayout.Landscape
                ? screenSize.y / LandscapeReference.y
                : screenSize.x / PortraitReference.x;
        }

        /// <summary>
        /// Picks the layout for a screen and works out where the panel and the board go.
        /// </summary>
        /// <param name="screenSize">The screen (or window, or browser canvas) in pixels.</param>
        /// <param name="safeArea">
        /// <c>Screen.safeArea</c>: the part of the screen not under a notch, rounded corner or
        /// home indicator. On desktop it's the whole screen.
        /// </param>
        public static LayoutResult Choose(Vector2 screenSize, Rect safeArea)
        {
            ScreenLayout layout = ChooseLayout(screenSize);
            float scale = CanvasScale(screenSize, layout);
            Rect safe = ClampToScreen(safeArea, screenSize);

            Rect panel;
            Rect board;
            if (layout == ScreenLayout.Landscape)
            {
                // A full-height strip on the right of the safe area; the board gets the rest.
                float panelWidth = LandscapePanelWidth * scale;
                panel = new Rect(safe.xMax - panelWidth, safe.yMin, panelWidth, safe.height);
                float boardWidth = Mathf.Max(safe.width - panelWidth, safe.width * MinBoardShare);
                board = new Rect(safe.xMin, safe.yMin, boardWidth, safe.height);
            }
            else
            {
                // A full-width strip along the bottom of the safe area; the board sits above it.
                float panelHeight = PortraitPanelHeight * scale;
                panel = new Rect(safe.xMin, safe.yMin, safe.width, panelHeight);
                float boardHeight = Mathf.Max(safe.height - panelHeight, safe.height * MinBoardShare);
                board = new Rect(safe.xMin, safe.yMax - boardHeight, safe.width, boardHeight);
            }
            return new LayoutResult(layout, scale, safe, panel, board);
        }

        /// <summary>
        /// The orthographic size (half the visible height, in world units) that makes a board
        /// with these half extents (padding included) fill <paramref name="boardRect"/> as far
        /// as it can without spilling out of it on either axis.
        /// </summary>
        public static float FitOrthographicSize(Vector2 halfExtents, Rect boardRect, float screenHeight)
        {
            // How many screen pixels one world unit can take so the board fits the rect's width,
            // and so it fits its height. The smaller of the two fits both.
            float pixelsPerUnit = Mathf.Min(
                boardRect.width / (2f * halfExtents.x),
                boardRect.height / (2f * halfExtents.y));
            // The camera shows screenHeight pixels, which is 2 * size world units.
            return screenHeight / (2f * pixelsPerUnit);
        }

        /// <summary>
        /// How far (in world units) to move the camera away from the board's center so the
        /// board appears in the middle of <paramref name="boardRect"/> rather than the middle
        /// of the screen.
        /// </summary>
        public static Vector2 CameraOffset(Rect boardRect, Vector2 screenSize, float orthographicSize)
        {
            // The camera's center is drawn at the screen's center. To show the board at the
            // rect's center instead, move the camera by the gap between those two points,
            // converted from pixels to world units.
            float unitsPerPixel = 2f * orthographicSize / screenSize.y;
            return (screenSize / 2f - boardRect.center) * unitsPerPixel;
        }

        /// <summary>
        /// The safe area cut down to the screen, or the whole screen if it's empty or outside it
        /// (some platforms report nonsense before the first frame).
        /// </summary>
        private static Rect ClampToScreen(Rect safeArea, Vector2 screenSize)
        {
            float xMin = Mathf.Max(safeArea.xMin, 0f);
            float yMin = Mathf.Max(safeArea.yMin, 0f);
            float xMax = Mathf.Min(safeArea.xMax, screenSize.x);
            float yMax = Mathf.Min(safeArea.yMax, screenSize.y);
            if (xMax <= xMin || yMax <= yMin)
            {
                return new Rect(Vector2.zero, screenSize);
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
