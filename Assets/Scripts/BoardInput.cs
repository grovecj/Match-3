using UnityEngine;
using UnityEngine.InputSystem;

namespace Match3
{
    /// <summary>
    /// Turns pointer (mouse/touch) input into moves on the <see cref="Board"/>. There are two
    /// ways to swap:
    ///   Drag  - press on a token and drag it toward a neighbor. The token follows the pointer
    ///           along one axis, and the neighbor slides the other way. Let go far enough (or
    ///           with a quick flick) and they swap; otherwise they spring back.
    ///   Click - click a token to select it, then click one of its neighbors.
    ///
    /// This is a small state machine:
    ///   Idle     - nothing picked up; waiting for a press on a token.
    ///   Pressed  - pressed on a token, but the pointer hasn't left the dead zone yet. Letting go
    ///              here is a click.
    ///   Dragging - the pointer has left the dead zone; the token follows it. Letting go here
    ///              swaps or springs back.
    ///   Selected - a token was clicked and is waiting for a click on a neighbor.
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
        [Tooltip("How far (as a fraction of a cell) the pointer must move before the token starts " +
                 "following it. Letting go inside this distance counts as a click.")]
        [SerializeField, Range(0.01f, 0.5f)] private float deadZone = 0.15f;
        [Tooltip("How far (as a fraction of a cell) the token must be dragged for letting go to swap it.")]
        [SerializeField, Range(0.1f, 1f)] private float commitThreshold = 0.4f;
        [Tooltip("Letting go while the pointer moves toward the neighbor at least this fast " +
                 "(in cells per second) swaps, even if the drag was short.")]
        [SerializeField, Min(0.1f)] private float flickSpeed = 8f;
        [Tooltip("How far (as a fraction of a cell) a token can be stretched toward the edge of the " +
                 "board, where there's nothing to swap with. It never quite reaches this.")]
        [SerializeField, Range(0f, 0.5f)] private float edgeStretch = 0.2f;

        // How quickly the measured pointer speed catches up with the real one, in seconds. Short
        // enough to notice a flick, long enough that one jittery frame doesn't count as one.
        private const float VelocitySmoothing = 0.05f;

        /// <summary>Which way a drag is locked to.</summary>
        public enum DragAxis { None, Horizontal, Vertical }

        private enum State { Idle, Pressed, Dragging, Selected }

        private Board board;
        private Camera cam;
        private State state = State.Idle;

        // The token that was pressed (or is selected), and where the press started.
        private Vector2Int pressCell;
        private Vector2 pressStartWorld;
        // Whether the pressed token was already selected, so letting go deselects it.
        private bool pressWasSelected;

        // The drag so far, worked out every frame by UpdateDrag.
        private DragAxis axis;
        private Vector2Int dragDirection;
        private float dragDistance;
        private bool dragHasNeighbor;

        // The pointer's recent velocity (world units per second), for spotting flicks.
        private Vector2 pointerVelocity;
        private Vector2 lastPointerWorld;

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

            // Input is switched off when the game pauses or the round ends. Put down whatever
            // the player was holding: a half-finished drag springs back rather than swapping.
            // (isActiveAndEnabled is false while the scene is being torn down, when the Board
            // can't start the spring-back coroutine.)
            if (board != null && board.isActiveAndEnabled)
            {
                board.DropHeld();
            }
            state = State.Idle;
        }

        private void Update()
        {
            // The board can let go of a token by itself (a new round rebuilds it), so if we
            // think something's held but the board doesn't, start over.
            if (state != State.Idle && !board.IsHolding)
            {
                state = State.Idle;
            }

            switch (state)
            {
                case State.Idle:
                    // WasPressedThisFrame is true only on the single frame the button goes down,
                    // so holding the button doesn't keep starting new presses.
                    if (pressAction.WasPressedThisFrame())
                    {
                        PressWhileIdle();
                    }
                    break;

                case State.Selected:
                    if (pressAction.WasPressedThisFrame())
                    {
                        PressWhileSelected();
                    }
                    break;

                case State.Pressed:
                case State.Dragging:
                    UpdatePress();
                    break;
            }
        }

        private void PressWhileIdle()
        {
            // TryPickUp refuses while the board is resolving, which is what blocks input then.
            if (TryGetPressedToken(out Vector2Int cell) && board.TryPickUp(cell))
            {
                BeginPress(cell, wasSelected: false);
            }
        }

        private void PressWhileSelected()
        {
            Vector2Int selected = pressCell;

            // Outside the board or on an empty cell: deselect.
            if (!TryGetPressedToken(out Vector2Int cell))
            {
                board.DropHeld();
                state = State.Idle;
                return;
            }

            // The selected token again: it might be a click (deselect) or the start of a drag.
            if (cell == selected)
            {
                BeginPress(cell, wasSelected: true);
                return;
            }

            // A neighbor: swap with it. Manhattan distance 1 means up/down/left/right.
            if (Mathf.Abs(cell.x - selected.x) + Mathf.Abs(cell.y - selected.y) == 1)
            {
                board.TrySwap(selected, cell);
                state = State.Idle;
                return;
            }

            // Any other token: select that one instead (TryPickUp puts the old one down).
            if (board.TryPickUp(cell))
            {
                BeginPress(cell, wasSelected: false);
            }
            else
            {
                state = State.Idle;
            }
        }

        private void BeginPress(Vector2Int cell, bool wasSelected)
        {
            pressCell = cell;
            pressWasSelected = wasSelected;
            pressStartWorld = PointerWorldPosition();
            lastPointerWorld = pressStartWorld;
            pointerVelocity = Vector2.zero;
            axis = DragAxis.None;
            dragDirection = Vector2Int.zero;
            dragDistance = 0f;
            dragHasNeighbor = false;
            state = State.Pressed;
        }

        /// <summary>Follows the pointer while it's pressed, and finishes the press when it's let go.</summary>
        private void UpdatePress()
        {
            Vector2 world = PointerWorldPosition();
            // Unscaled time: real seconds, which is what the player's hand moves in.
            pointerVelocity = SmoothVelocity(pointerVelocity, world - lastPointerWorld,
                Time.unscaledDeltaTime, VelocitySmoothing);
            lastPointerWorld = world;
            Vector2 delta = world - pressStartWorld;

            // Leaving the dead zone turns the press into a drag, which also ends any selection.
            if (state == State.Pressed && delta.magnitude >= deadZone * board.CellSize)
            {
                state = State.Dragging;
                board.SetHeldSelected(false);
            }

            // Move the tokens before checking for a release, so the release frame's pointer
            // position counts too.
            if (state == State.Dragging)
            {
                UpdateDrag(delta);
            }

            if (pressAction.IsPressed())
            {
                return;
            }

            if (state == State.Dragging)
            {
                FinishDrag();
            }
            else
            {
                FinishClick();
            }
        }

        private void UpdateDrag(Vector2 delta)
        {
            float cellSize = board.CellSize;
            axis = ChooseAxis(delta, deadZone * cellSize, axis);
            dragDirection = DirectionAlong(delta, axis);

            Vector2Int neighbor = pressCell + dragDirection;
            // GetToken returns null off the edge of the board.
            dragHasNeighbor = dragDirection != Vector2Int.zero && board.GetToken(neighbor.x, neighbor.y) != null;

            // Dot with the direction measures how far the pointer has gone that way (and ignores
            // any sideways wobble). Divide by the cell size to get cells.
            float along = Mathf.Max(0f, Vector2.Dot(delta, dragDirection)) / cellSize;
            dragDistance = DragDistance(along, dragHasNeighbor, edgeStretch);
            board.DragHeld(dragDirection, dragDistance);
        }

        private void FinishDrag()
        {
            // How fast the pointer was going toward the neighbor, in cells per second.
            float speed = Vector2.Dot(pointerVelocity, dragDirection) / board.CellSize;
            if (ShouldCommit(dragDistance, speed, dragHasNeighbor, commitThreshold, flickSpeed))
            {
                board.TrySwap(pressCell, pressCell + dragDirection);
            }
            else
            {
                board.DropHeld();
            }
            state = State.Idle;
        }

        private void FinishClick()
        {
            if (pressWasSelected)
            {
                // Clicked the selected token again: put it down.
                board.DropHeld();
                state = State.Idle;
            }
            else
            {
                board.SetHeldSelected(true);
                state = State.Selected;
            }
        }

        /// <summary>The cell under the pointer, if it's on the board and holds a token.</summary>
        private bool TryGetPressedToken(out Vector2Int cell)
        {
            return board.TryWorldToGrid(PointerWorldPosition(), out cell) && board.GetToken(cell.x, cell.y) != null;
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

        // ---------------------------------------------------------------- Drag math
        //
        // Pure functions with no input or scene access, which is why they're `static`: Edit
        // Mode tests can call them directly with any numbers.

        /// <summary>
        /// Decides which axis a drag moves along. Inside the dead zone there's no axis. Leaving
        /// it picks whichever of horizontal/vertical the pointer has moved further along, and
        /// that choice sticks until the pointer comes back inside the dead zone.
        /// </summary>
        /// <param name="delta">How far the pointer has moved since the press, in world units.</param>
        /// <param name="deadZone">Radius of the dead zone, in world units. Exactly on it counts as outside.</param>
        /// <param name="current">The axis chosen so far (None if not chosen yet).</param>
        public static DragAxis ChooseAxis(Vector2 delta, float deadZone, DragAxis current)
        {
            if (delta.magnitude < deadZone)
            {
                return DragAxis.None;
            }
            if (current != DragAxis.None)
            {
                return current;
            }
            // An exact 45° goes horizontal; it has to go somewhere.
            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? DragAxis.Horizontal : DragAxis.Vertical;
        }

        /// <summary>
        /// Which neighbor a drag points at: the sign of <paramref name="delta"/> along
        /// <paramref name="axis"/>. Zero if there's no axis, or no movement along it.
        /// </summary>
        public static Vector2Int DirectionAlong(Vector2 delta, DragAxis axis)
        {
            // A switch expression: picks the value for the first matching case.
            float along = axis switch
            {
                DragAxis.Horizontal => delta.x,
                DragAxis.Vertical => delta.y,
                _ => 0f,
            };
            if (along == 0f)
            {
                return Vector2Int.zero;
            }

            // Mathf.Sign returns +1 or -1.
            int sign = (int)Mathf.Sign(along);
            return axis == DragAxis.Horizontal ? new Vector2Int(sign, 0) : new Vector2Int(0, sign);
        }

        /// <summary>
        /// How far (in cells) to move the held token for a pointer that's <paramref name="along"/>
        /// cells from the press. Toward a neighbor it follows the pointer exactly, up to one cell.
        /// Toward the edge of the board it stretches a little, with more resistance the further
        /// the pointer goes, and never quite reaches <paramref name="edgeStretch"/>.
        /// </summary>
        public static float DragDistance(float along, bool hasNeighbor, float edgeStretch)
        {
            along = Mathf.Max(0f, along);
            if (hasNeighbor)
            {
                return Mathf.Min(along, 1f);
            }
            if (edgeStretch <= 0f)
            {
                return 0f;
            }
            // A "rubber band" curve: it starts out following the pointer one-for-one (the slope
            // at 0 is 1) and flattens out toward edgeStretch. Mathf.Exp(-x) shrinks from 1 toward
            // 0 as x grows, so 1 - Exp(-x) climbs from 0 toward 1.
            return edgeStretch * (1f - Mathf.Exp(-along / edgeStretch));
        }

        /// <summary>
        /// Whether letting go of a drag should swap. Only if there's a neighbor to swap with, and
        /// the token was dragged at least <paramref name="threshold"/> cells, or was moving toward
        /// the neighbor at least <paramref name="flickSpeed"/> cells per second (a flick).
        /// </summary>
        public static bool ShouldCommit(float distance, float speedTowardNeighbor, bool hasNeighbor,
            float threshold, float flickSpeed)
        {
            if (!hasNeighbor || distance <= 0f)
            {
                return false;
            }
            return distance >= threshold || speedTowardNeighbor >= flickSpeed;
        }

        /// <summary>
        /// Blends this frame's pointer velocity into the running average, so one jittery frame
        /// doesn't spike it. The older velocity fades away over about
        /// <paramref name="smoothingTime"/> seconds.
        /// </summary>
        /// <param name="velocity">The average so far, in units per second.</param>
        /// <param name="displacement">How far the pointer moved this frame, in units.</param>
        /// <param name="deltaTime">How long this frame took, in seconds.</param>
        public static Vector2 SmoothVelocity(Vector2 velocity, Vector2 displacement, float deltaTime, float smoothingTime)
        {
            // No time passed (e.g. the very first frame): there's no new speed to measure.
            if (deltaTime <= 0f)
            {
                return velocity;
            }
            Vector2 frameVelocity = displacement / deltaTime;
            if (smoothingTime <= 0f)
            {
                return frameVelocity;
            }
            // Exponential smoothing that behaves the same at any frame rate: the weight given to
            // the new frame grows with how long the frame took.
            float weight = 1f - Mathf.Exp(-deltaTime / smoothingTime);
            return Vector2.Lerp(velocity, frameVelocity, weight);
        }
    }
}
