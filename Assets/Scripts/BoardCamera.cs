using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Keeps an orthographic camera centered on the board and zoomed so the whole board
    /// fits on screen with some padding, at any window size or aspect ratio.
    /// </summary>
    // RequireComponent makes Unity add a Camera automatically if one is missing.
    [RequireComponent(typeof(Camera))]
    public class BoardCamera : MonoBehaviour
    {
        [SerializeField] private Board board;
        [Tooltip("Empty space around the board, in world units (1 unit = 1 cell at default cell size).")]
        [SerializeField, Min(0f)] private float padding = 1f;

        private Camera cam;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
        }

        // LateUpdate runs after every Update, so the camera reacts to anything that changed
        // this frame (board resized in the Inspector, game window resized, etc).
        private void LateUpdate()
        {
            if (board == null)
            {
                return;
            }

            // Center on the board, keeping the camera's own z so it stays in front of the sprites.
            Vector3 boardCenter = board.transform.position;
            transform.position = new Vector3(boardCenter.x, boardCenter.y, transform.position.z);

            // orthographicSize is HALF the visible height in world units.
            // The visible width is that times the aspect ratio (width / height), so to fit the
            // board horizontally we divide the needed half-width by the aspect.
            Vector2 halfExtents = board.WorldSize / 2f + Vector2.one * padding;
            float sizeToFitHeight = halfExtents.y;
            float sizeToFitWidth = halfExtents.x / cam.aspect;
            cam.orthographicSize = Mathf.Max(sizeToFitHeight, sizeToFitWidth);
        }
    }
}
