using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Keeps an orthographic camera centered on the board and zoomed so the whole board
    /// fits on screen with some padding, at any window size or aspect ratio. Also paints the
    /// background behind the board in the current board theme's color.
    /// </summary>
    // RequireComponent makes Unity add a Camera automatically if one is missing.
    [RequireComponent(typeof(Camera))]
    public class BoardCamera : MonoBehaviour
    {
        [SerializeField] private Board board;
        [Tooltip("Empty space around the board, in world units (1 unit = 1 cell at default cell size).")]
        [SerializeField, Min(0f)] private float padding = 1f;
        [Tooltip("Says which part of the screen the side panel leaves free (to its left in " +
                 "landscape, above it in portrait). Optional: without one the board uses the whole screen.")]
        [SerializeField] private ResponsiveLayout layout;
        [Tooltip("Supplies the background color. Optional: without one the camera keeps its own color.")]
        [SerializeField] private ThemeManager themeManager;

        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
        }

        // Subscribe while enabled so theme changes recolor the background (see Board for more
        // on the OnEnable/OnDisable pairing).
        private void OnEnable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged += ApplyBackgroundColor;
            }
            // Catch up on any change made while we were disabled and not listening.
            ApplyBackgroundColor();
        }

        private void OnDisable()
        {
            if (themeManager != null)
            {
                themeManager.ThemeChanged -= ApplyBackgroundColor;
            }
        }

        // Start rather than Awake: the ThemeManager picks its default theme in its own Awake,
        // and Start is guaranteed to run after every Awake.
        private void Start()
        {
            ApplyBackgroundColor();
        }

        /// <summary>
        /// Sets the color the camera clears the screen to before drawing anything, which is
        /// what shows around the board.
        /// </summary>
        private void ApplyBackgroundColor()
        {
            if (themeManager != null && themeManager.BoardTheme != null)
            {
                cam.backgroundColor = themeManager.BoardTheme.BackgroundColor;
            }
        }

        // LateUpdate runs after every Update, so the camera reacts to anything that changed
        // this frame (board resized in the Inspector, game window resized, phone rotated, etc).
        private void LateUpdate()
        {
            if (board == null)
            {
                return;
            }

            // The board only gets the part of the screen the panel doesn't cover, in pixels.
            var screenSize = new Vector2(cam.pixelWidth, cam.pixelHeight);
            Rect boardRect = layout != null ? layout.BoardRect : new Rect(Vector2.zero, screenSize);
            if (boardRect.width <= 0f || boardRect.height <= 0f)
            {
                return;
            }

            // orthographicSize is HALF the visible height in world units. LayoutRules picks the
            // largest zoom at which the board plus padding fits inside boardRect on both axes.
            Vector2 halfExtents = board.WorldSize / 2f + Vector2.one * padding;
            float size = LayoutRules.FitOrthographicSize(halfExtents, boardRect, screenSize.y);
            cam.orthographicSize = size;

            // Put the board in the middle of the free area rather than the middle of the screen
            // by moving the camera the other way. Keep the camera's own z so it stays in front
            // of the sprites.
            Vector3 boardCenter = board.transform.position;
            Vector2 offset = LayoutRules.CameraOffset(boardRect, screenSize, size);
            transform.position = new Vector3(boardCenter.x + offset.x, boardCenter.y + offset.y, transform.position.z);
        }
    }
}
