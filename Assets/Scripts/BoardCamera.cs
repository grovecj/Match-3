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
        [Tooltip("Optional UI panel docked to the right edge of the screen. The board is fitted " +
                 "into the space to its left so the two never overlap.")]
        [SerializeField] private RectTransform rightPanel;
        [Tooltip("Supplies the background color. Optional: without one the camera keeps its own color.")]
        [SerializeField] private ThemeManager themeManager;

        private Camera cam;
        // Reused buffer for RectTransform.GetWorldCorners, so we don't allocate every frame.
        private readonly Vector3[] panelCorners = new Vector3[4];

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
        // this frame (board resized in the Inspector, game window resized, etc).
        private void LateUpdate()
        {
            if (board == null)
            {
                return;
            }

            // The board only gets the part of the screen the right panel doesn't cover.
            // boardFraction is that part's share of the screen width (1 = whole screen).
            float panelFraction = GetRightPanelScreenFraction();
            float boardFraction = 1f - panelFraction;

            // orthographicSize is HALF the visible height in world units.
            // The visible width is that times the aspect ratio (width / height), and the board
            // only gets boardFraction of it, so to fit the board horizontally we divide the
            // needed half-width by both.
            Vector2 halfExtents = board.WorldSize / 2f + Vector2.one * padding;
            float sizeToFitHeight = halfExtents.y;
            float sizeToFitWidth = halfExtents.x / (cam.aspect * boardFraction);
            float size = Mathf.Max(sizeToFitHeight, sizeToFitWidth);
            cam.orthographicSize = size;

            // Put the board in the middle of the free area rather than the middle of the screen:
            // shift the camera right by half the panel's width in world units. (The visible
            // world width is size * 2 * aspect, so half the panel is size * aspect * fraction.)
            // Keep the camera's own z so it stays in front of the sprites.
            Vector3 boardCenter = board.transform.position;
            float offsetX = size * cam.aspect * panelFraction;
            transform.position = new Vector3(boardCenter.x + offsetX, boardCenter.y, transform.position.z);
        }

        /// <summary>
        /// How much of the screen's width (0..1) the right panel covers, or 0 if there's no panel.
        /// </summary>
        private float GetRightPanelScreenFraction()
        {
            if (rightPanel == null || !rightPanel.gameObject.activeInHierarchy)
            {
                return 0f;
            }

            // For a Screen Space - Overlay canvas, UI "world" corners are in screen pixels, so
            // this gives the panel's on-screen width after the Canvas Scaler has done its work.
            // Corners are ordered bottom-left, top-left, top-right, bottom-right.
            rightPanel.GetWorldCorners(panelCorners);
            float panelWidth = panelCorners[2].x - panelCorners[0].x;
            // Clamp so a misconfigured, huge panel can't leave the board zero (or negative) room.
            return Mathf.Clamp(panelWidth / cam.pixelWidth, 0f, 0.9f);
        }
    }
}
