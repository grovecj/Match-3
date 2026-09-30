using UnityEngine;
using UnityEngine.InputSystem;

namespace Match3
{
    /// <summary>
    /// Turns pointer (mouse/touch) input into swap requests on the <see cref="Board"/>:
    /// press on a token, drag toward a neighbor, and once the drag passes a threshold the
    /// two tokens swap.
    ///
    /// This is a tiny state machine with two states:
    ///   Idle     - waiting for a press on the board.
    ///   Dragging - a press started on a token; watching the pointer to see where it goes.
    /// A swap (or a drag that goes nowhere useful) sends us back to Idle.
    /// </summary>
    [RequireComponent(typeof(Board))]
    public class BoardInput : MonoBehaviour
    {
        // An InputAction is a named, device-independent input ("press", "point") bound to one
        // or more concrete controls. "<Pointer>" matches any pointer device, so the same code
        // works for a mouse and a touchscreen. Serialized actions show up in the Inspector,
        // where you can see or change their bindings; these initializers are the defaults.
        [Header("Input Actions")]
        [SerializeField] private InputAction pressAction = new InputAction("Press", InputActionType.Button, "<Pointer>/press");
        [SerializeField] private InputAction pointAction = new InputAction("Point", InputActionType.Value, "<Pointer>/position");

        [Header("Drag")]
        [Tooltip("How far (as a fraction of a cell) the pointer must move before a swap triggers.")]
        [SerializeField, Range(0.1f, 1f)] private float dragThreshold = 0.5f;
        [Tooltip("Drags whose minor axis is at least this fraction of the major axis count as " +
                 "diagonal and are ignored. 1 = only exact 45° drags are ignored.")]
        [SerializeField, Range(0f, 1f)] private float diagonalTolerance = 0.75f;

        private enum State { Idle, Dragging }

        private Board board;
        private Camera cam;
        private State state = State.Idle;
        private Vector2Int dragStartCell;
        private Vector2 dragStartWorld;

        private void Awake()
        {
            board = GetComponent<Board>();
            // Camera.main finds the camera tagged "MainCamera" in the scene.
            cam = Camera.main;
        }

        // Actions don't do anything until enabled. Pairing Enable/Disable with the component's
        // own OnEnable/OnDisable means input stops whenever this component is turned off.
        private void OnEnable()
        {
            pressAction.Enable();
            pointAction.Enable();
        }

        private void OnDisable()
        {
            pressAction.Disable();
            pointAction.Disable();
            state = State.Idle;
        }

        private void Update()
        {
            switch (state)
            {
                case State.Idle:
                    // WasPressedThisFrame is true only on the single frame the button goes down,
                    // so holding the button doesn't keep starting new drags.
                    if (pressAction.WasPressedThisFrame())
                    {
                        TryBeginDrag();
                    }
                    break;

                case State.Dragging:
                    if (!pressAction.IsPressed())
                    {
                        // Released before dragging far enough: the drag was too short, do nothing.
                        state = State.Idle;
                    }
                    else
                    {
                        UpdateDrag();
                    }
                    break;
            }
        }

        private void TryBeginDrag()
        {
            // Block new input while the board is still resolving the previous swap.
            if (board.IsResolving)
            {
                return;
            }

            Vector2 world = PointerWorldPosition();
            // Presses outside the board, or on an empty cell, are ignored.
            if (!board.TryWorldToGrid(world, out Vector2Int cell) || board.GetToken(cell.x, cell.y) == null)
            {
                return;
            }

            dragStartCell = cell;
            dragStartWorld = world;
            state = State.Dragging;
        }

        private void UpdateDrag()
        {
            Vector2 delta = PointerWorldPosition() - dragStartWorld;

            // No clear direction yet (too short, or too diagonal): keep waiting. Not canceling
            // lets the player keep dragging, or straighten out a diagonal drag.
            if (!TryGetSwapDirection(delta, dragThreshold * board.CellSize, diagonalTolerance, out Vector2Int direction))
            {
                return;
            }

            // TrySwap refuses targets off the edge of the board, so an outward drag from an
            // edge cell simply does nothing. Either way this drag is finished.
            board.TrySwap(dragStartCell, dragStartCell + direction);
            state = State.Idle;
        }

        /// <summary>
        /// Decides which neighbor a drag points at. Pure math with no input or scene access,
        /// which is why it's <c>static</c>: Edit Mode tests can call it directly with any delta.
        /// </summary>
        /// <param name="delta">How far the pointer has moved since the press, in world units.</param>
        /// <param name="threshold">Minimum distance along the dominant axis, in world units.
        /// A drag exactly this long counts.</param>
        /// <param name="diagonalTolerance">Drags whose minor axis is at least this fraction of the
        /// major axis are too diagonal and are rejected.</param>
        /// <param name="direction">One of up/down/left/right when this returns true; zero otherwise.</param>
        /// <returns>True if the drag is long and straight enough to pick a direction.</returns>
        public static bool TryGetSwapDirection(Vector2 delta, float threshold, float diagonalTolerance, out Vector2Int direction)
        {
            direction = Vector2Int.zero;

            float absX = Mathf.Abs(delta.x);
            float absY = Mathf.Abs(delta.y);
            float major = Mathf.Max(absX, absY);
            float minor = Mathf.Min(absX, absY);

            // Not far enough yet.
            if (major < threshold)
            {
                return false;
            }

            // Too close to 45° to tell which neighbor the player meant.
            if (minor >= major * diagonalTolerance)
            {
                return false;
            }

            // Pick the dominant direction. Mathf.Sign returns +1 or -1.
            direction = absX > absY
                ? new Vector2Int((int)Mathf.Sign(delta.x), 0)
                : new Vector2Int(0, (int)Mathf.Sign(delta.y));
            return true;
        }

        /// <summary>The pointer's current position converted from screen pixels to world units.</summary>
        private Vector2 PointerWorldPosition()
        {
            // The pointer reports screen coordinates: pixels from the bottom-left of the window.
            // ScreenToWorldPoint maps them into the world through the camera. For an orthographic
            // camera the z we pass doesn't affect x/y, and our board logic ignores z anyway.
            Vector2 screen = pointAction.ReadValue<Vector2>();
            return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        }
    }
}
