namespace Match3
{
    /// <summary>
    /// The special ability a <see cref="Piece"/> has, if any. Each one is made by a match of a
    /// particular shape: see <see cref="BoardModel.PowerMadeBy"/>.
    /// </summary>
    // An enum is a named set of whole numbers: None is 0, Flame is 1, and so on. Putting None
    // first matters, because 0 is what a field holds before anything is assigned to it.
    public enum PowerKind
    {
        /// <summary>An ordinary gem.</summary>
        None,
        /// <summary>Made by a line of exactly 4. When it's matched, it clears the 3x3 around it.</summary>
        Flame,
        /// <summary>Made by an L, T or + shape. When it's matched, it clears its whole row and column.</summary>
        Star,
        /// <summary>
        /// Made by a line of 5 or more. The one power gem with no colour of its own: it is never
        /// matched, and goes off when the player swaps it with a coloured neighbour, clearing
        /// every gem of that colour (see <see cref="BoardModel.ActivateHypercube"/>). Caught in
        /// another gem's blast it goes off too, on whichever colour there is most of.
        /// </summary>
        Hypercube,
    }

    /// <summary>
    /// The bigger blast two power gems make when the player swaps them with each other (see
    /// <see cref="BoardModel.ActivateCombo"/>). Which one it is depends only on the pair.
    /// </summary>
    public enum PowerCombo
    {
        /// <summary>Not a combo: a gem going off on its own.</summary>
        None,
        /// <summary>Flame + Flame: the 5x5 around the moved gem.</summary>
        BigFlame,
        /// <summary>Any pair with a Star: three whole rows and three whole columns around the moved gem.</summary>
        StarCross,
    }

    /// <summary>
    /// What one board cell holds: a colour (which <see cref="TokenType"/> it matches with) plus
    /// an optional <see cref="PowerKind"/>. A cell can also hold nothing: see <see cref="Empty"/>.
    /// </summary>
    // A struct is a value type: a Piece[,] stores the pieces themselves, side by side, rather
    // than references to objects elsewhere. Two things follow from that:
    //  - assigning a Piece copies it, so nobody can change "the piece in cell (2, 3)" through a
    //    copy they were handed earlier (and `readonly` means nobody can change a copy either);
    //  - a struct can't be null. Every slot of a new array holds `default(Piece)`: all fields
    //    zeroed, so no colour and PowerKind.None. That is exactly what we call Empty.
    //
    // It's spelled "Colour" rather than "Color" so it can't be mistaken for UnityEngine.Color,
    // which is an actual RGBA colour. This one is a kind of gem.
    public readonly struct Piece
    {
        /// <summary>The cell with nothing in it.</summary>
        public static readonly Piece Empty = default;

        /// <summary>A Hypercube: a power with no colour. Every Hypercube is the same piece.</summary>
        public static readonly Piece Hypercube = new Piece(null, PowerKind.Hypercube);

        /// <summary>What this piece matches with, or null if it has no colour (a Hypercube, or an empty cell).</summary>
        public readonly TokenType Colour;
        public readonly PowerKind Power;

        public Piece(TokenType colour, PowerKind power = PowerKind.None)
        {
            Colour = colour;
            Power = power;
        }

        /// <summary>
        /// True if there's no piece here at all. A colourless power gem is not empty, which is
        /// why "no colour" alone isn't the test.
        /// </summary>
        public bool IsEmpty => Colour == null && Power == PowerKind.None;

        /// <summary>
        /// True if this piece and <paramref name="other"/> can be part of the same run: they have
        /// the same colour, and it is a colour. Empty and colourless pieces never match anything,
        /// not even each other.
        /// </summary>
        public bool Matches(Piece other)
        {
            return Colour != null && Colour == other.Colour;
        }

        // Shown by the debugger and in test failure messages.
        public override string ToString()
        {
            if (IsEmpty)
            {
                return "Empty";
            }

            string colour = Colour != null ? Colour.name : "Colourless";
            return Power == PowerKind.None ? colour : $"{colour} ({Power})";
        }
    }
}
