using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Everything something drawing a power gem's blast needs to know, handed out by the
    /// <see cref="Board"/>'s <see cref="Board.PowerGemBlastStarted"/> event: which gem went off
    /// and where, which wave of the chain it's in, which cells its blast hits, and how many
    /// seconds into the blast each of them is hit.
    ///
    /// The Board pops each hit gem at exactly that delay. An effect that waits the same delays
    /// before showing anything at a cell (<see cref="PowerGemEffects"/> does) stays in step with
    /// the pops without ever talking to the Board again.
    /// </summary>
    // A plain C# class, not a MonoBehaviour: it's just data passed from one script to another.
    public class PowerGemBlast
    {
        /// <summary>What kind of gem went off.</summary>
        public PowerKind Kind { get; }

        /// <summary>The cell the gem was in when it went off.</summary>
        public Vector2Int Cell { get; }

        /// <summary>
        /// Which wave of the chain reaction this is: 0 for a gem set off directly, 1 for one
        /// caught in a wave-0 blast, and so on (as in <see cref="Board.PowerGemDetonated"/>).
        /// </summary>
        public int WaveIndex { get; }

        /// <summary>
        /// The colour the blast is drawn in. A Flame's or Star's own colour; for a Hypercube,
        /// which has none, the colour it's destroying.
        /// </summary>
        public TokenType Colour { get; }

        /// <summary>Every cell the blast hits, in the order the model listed them.</summary>
        public IReadOnlyList<Vector2Int> Blasted { get; }

        /// <summary>
        /// For each cell of <see cref="Blasted"/> (same order), how many seconds after the blast
        /// starts it's hit: 0 everywhere for a Flame, growing with distance for a Star's beam,
        /// one zap interval apart for a Hypercube.
        /// </summary>
        public IReadOnlyList<float> Delays { get; }

        /// <summary>
        /// The combo this blast is, if the gem was the one the player moved in a combo swap
        /// (see <see cref="BoardModel.ActivateCombo"/>): then <see cref="Blasted"/> is the whole
        /// combined blast, centred on <see cref="Cell"/>. None for any other blast.
        /// </summary>
        public PowerCombo Combo { get; }

        /// <summary>
        /// True for the other gem of a combo swap. It goes off (so it's still drawn and heard)
        /// but blasts nothing of its own: <see cref="Blasted"/> is empty.
        /// </summary>
        public bool IsComboPartner { get; }

        public PowerGemBlast(PowerKind kind, Vector2Int cell, int waveIndex, TokenType colour,
            IReadOnlyList<Vector2Int> blasted, IReadOnlyList<float> delays,
            PowerCombo combo = PowerCombo.None, bool isComboPartner = false)
        {
            Kind = kind;
            Cell = cell;
            WaveIndex = waveIndex;
            Colour = colour;
            Blasted = blasted;
            Delays = delays;
            Combo = combo;
            IsComboPartner = isComboPartner;
        }
    }
}
