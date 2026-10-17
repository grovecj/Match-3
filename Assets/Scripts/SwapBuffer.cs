using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Remembers one swap the player made while the board was still resolving, so it can be
    /// played the moment the board settles. This is "input buffering": instead of throwing
    /// away input that arrives at a bad time, keep it and act on it as soon as you can.
    ///
    /// It holds cells, not tokens: the tokens may still be falling when the swap is made, and
    /// what gets swapped is whatever is in those cells once the board settles.
    ///
    /// The rules, all in one place so Edit Mode tests can check them without a scene:
    ///   - Only one swap is kept. A new one replaces the old.
    ///   - Clicking a marked cell again, or pressing outside the board, cancels everything.
    ///   - A click-click swap is made the same way as on a settled board: the first click
    ///     selects a cell, a click on one of its neighbors makes the swap.
    ///   - <see cref="Discard"/> throws everything away. BoardInput calls it whenever the swap
    ///     must not run: the game pauses, the board reshuffles, the clock reaches 0:00, the Last
    ///     Hurrah starts, or input is turned off for any other reason.
    ///
    /// It knows nothing about the board, the pointer or the screen; BoardInput feeds it cells
    /// and asks it what to do.
    /// </summary>
    public class SwapBuffer
    {
        /// <summary>True while a swap is waiting to be played.</summary>
        public bool HasSwap { get; private set; }

        /// <summary>The cell the swap starts from (the one picked up, or clicked first). Only meaningful while <see cref="HasSwap"/>.</summary>
        public Vector2Int First { get; private set; }

        /// <summary>The cell it swaps with. Only meaningful while <see cref="HasSwap"/>.</summary>
        public Vector2Int Second { get; private set; }

        /// <summary>
        /// The first click of a click-click swap made while the board was resolving, waiting for
        /// a click on a neighbor. Null when nothing is selected.
        /// </summary>
        public Vector2Int? Selected { get; private set; }

        /// <summary>True when there's neither a swap nor a selection.</summary>
        public bool IsEmpty => !HasSwap && !Selected.HasValue;

        /// <summary>
        /// Records the swap of cells <paramref name="first"/> and <paramref name="second"/>,
        /// replacing any swap already kept, and ends any selection. Returns false (and changes
        /// nothing) if the cells aren't neighbors.
        /// </summary>
        public bool Record(Vector2Int first, Vector2Int second)
        {
            if (!AreNeighbors(first, second))
            {
                return false;
            }
            First = first;
            Second = second;
            HasSwap = true;
            Selected = null;
            return true;
        }

        /// <summary>
        /// A click (press and release without dragging) on <paramref name="cell"/>, a cell on
        /// the board. A marked cell cancels everything; a neighbor of the selected cell makes a
        /// swap; any other cell becomes the selection (a swap already kept stays until it's
        /// replaced).
        /// </summary>
        public void Click(Vector2Int cell)
        {
            if (IsMarked(cell))
            {
                Cancel();
                return;
            }
            if (Selected is Vector2Int selected && AreNeighbors(selected, cell))
            {
                Record(selected, cell);
                return;
            }
            Selected = cell;
        }

        /// <summary>A press outside the board: cancels everything.</summary>
        public void PressOutside()
        {
            Cancel();
        }

        /// <summary>The player called it off: forgets the swap and the selection.</summary>
        public void Cancel()
        {
            HasSwap = false;
            Selected = null;
        }

        /// <summary>
        /// The game called it off (a pause, a reshuffle, 0:00, the Last Hurrah, input turned off):
        /// forgets the swap and the selection, so nothing runs afterwards. The same as
        /// <see cref="Cancel"/>; a separate name so the code says who called it off.
        /// </summary>
        public void Discard()
        {
            Cancel();
        }

        /// <summary>
        /// Hands over the swap to play now and forgets it. The selection, if any, is kept: the
        /// player may still be making their next swap. Returns false if no swap is kept.
        /// </summary>
        public bool TryTake(out Vector2Int first, out Vector2Int second)
        {
            first = First;
            second = Second;
            if (!HasSwap)
            {
                return false;
            }
            HasSwap = false;
            return true;
        }

        /// <summary>Whether <paramref name="cell"/> is one of the kept swap's cells, or the selection.</summary>
        public bool IsMarked(Vector2Int cell)
        {
            return (HasSwap && (cell == First || cell == Second)) || Selected == cell;
        }

        // Manhattan distance 1 means up/down/left/right, never diagonal.
        private static bool AreNeighbors(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
        }
    }
}
