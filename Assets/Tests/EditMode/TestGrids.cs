using System.Collections.Generic;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Test data builder: turns a few readable strings into a <see cref="BoardModel"/>, so a
    /// test can show the board it's about instead of burying it in SetTypeAt calls.
    ///
    ///     BoardModel board = grids.Build(
    ///         "GBGB",   // top row (y = 2)
    ///         "BGBG",
    ///         "RRRG");  // bottom row (y = 0)
    ///
    /// Each letter is a token type (the same letter is always the same type) and '.' is an
    /// empty cell. Rows are written top row first, the way the board looks on screen, even
    /// though y counts upward from the bottom.
    ///
    /// Power gems are marked on a second drawing of the same board, so the first one stays as
    /// easy to read as ever: 'F' makes the piece in that cell a Flame, 'S' a Star.
    ///
    ///     grids.SetPowers(board,
    ///         "....",
    ///         "....",
    ///         ".F..");  // the second R of the bottom row is a Flame
    ///
    /// A Hypercube has no colour, so no letter fits it: draw it as '*' in the first drawing.
    ///
    /// Create one per test (in [SetUp]) and <see cref="DestroyAll"/> in [TearDown], since the
    /// token types it makes are Unity objects that otherwise hang around in the Editor.
    /// </summary>
    public class TestGrids
    {
        /// <summary>The character that means "empty cell".</summary>
        public const char Empty = '.';
        /// <summary>The character that means "Hypercube": a piece with a power but no colour.</summary>
        public const char Hypercube = '*';

        private readonly Dictionary<char, TokenType> types = new Dictionary<char, TokenType>();

        /// <summary>The token type for <paramref name="letter"/>, created the first time it's asked for.</summary>
        public TokenType Type(char letter)
        {
            if (!types.TryGetValue(letter, out TokenType type))
            {
                // CreateInstance makes an in-memory TokenType, like an .asset file that was never
                // saved. Its name shows up in failure messages, so use the letter.
                type = ScriptableObject.CreateInstance<TokenType>();
                type.name = letter.ToString();
                types.Add(letter, type);
            }
            return type;
        }

        /// <summary>
        /// Builds a board from <paramref name="rows"/>, top row first. All rows must be the same
        /// length. The board's token types are every letter used so far; its randomness is
        /// seeded, so anything random it does is the same on every run.
        /// </summary>
        public BoardModel Build(params string[] rows)
        {
            int height = rows.Length;
            int width = rows[0].Length;

            // Register every letter first, so the model knows all the types in play.
            foreach (string row in rows)
            {
                if (row.Length != width)
                {
                    throw new System.ArgumentException($"Every row must be {width} wide, but \"{row}\" is {row.Length}.");
                }
                foreach (char letter in row)
                {
                    if (letter != Empty && letter != Hypercube)
                    {
                        Type(letter);
                    }
                }
            }

            var board = new BoardModel(width, height, types.Values, new System.Random(0));
            for (int i = 0; i < height; i++)
            {
                // rows[0] is the top of the board, so string index i is grid row height-1-i.
                int y = height - 1 - i;
                for (int x = 0; x < width; x++)
                {
                    char letter = rows[i][x];
                    board.SetPieceAt(x, y, PieceFor(letter));
                }
            }
            return board;
        }

        private Piece PieceFor(char letter)
        {
            switch (letter)
            {
                case Empty: return Piece.Empty;
                case Hypercube: return new Piece(null, PowerKind.Hypercube);
                default: return new Piece(Type(letter));
            }
        }

        /// <summary>
        /// Gives pieces already on <paramref name="board"/> a power. <paramref name="rows"/> is
        /// drawn like <see cref="Build"/>'s, top row first and the same size as the board: 'F' is
        /// a Flame, 'S' is a Star, and '.' leaves the cell alone. Each piece keeps its colour.
        /// </summary>
        public void SetPowers(BoardModel board, params string[] rows)
        {
            if (rows.Length != board.Height)
            {
                throw new System.ArgumentException($"The board is {board.Height} tall, but {rows.Length} rows were given.");
            }

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Length != board.Width)
                {
                    throw new System.ArgumentException($"Every row must be {board.Width} wide, but \"{rows[i]}\" is {rows[i].Length}.");
                }

                int y = board.Height - 1 - i;
                for (int x = 0; x < board.Width; x++)
                {
                    char mark = rows[i][x];
                    if (mark == Empty)
                    {
                        continue;
                    }

                    Piece piece = board.PieceAt(x, y);
                    if (piece.Colour == null)
                    {
                        throw new System.ArgumentException($"'{mark}' at ({x},{y}) needs a coloured piece to give its power to.");
                    }
                    // A Piece can't be changed (it's readonly), so make a new one with the same
                    // colour and put that in the cell instead.
                    board.SetPieceAt(x, y, new Piece(piece.Colour, PowerFor(mark)));
                }
            }
        }

        private static PowerKind PowerFor(char mark)
        {
            switch (mark)
            {
                case 'F': return PowerKind.Flame;
                case 'S': return PowerKind.Star;
                default: throw new System.ArgumentException($"'{mark}' isn't a power: use 'F' (Flame), 'S' (Star) or '{Empty}'.");
            }
        }

        /// <summary>
        /// The reverse of <see cref="Build"/>: draws <paramref name="board"/> as strings, top row
        /// first, so a test can compare a whole board in one assert and get a readable diff when
        /// it fails. Types this builder didn't create are drawn as '?'. A Flame or Star is drawn
        /// as its colour's letter, the same as an ordinary piece: see <see cref="PowerRows"/>.
        /// </summary>
        public string[] Rows(BoardModel board)
        {
            var rows = new string[board.Height];
            for (int i = 0; i < board.Height; i++)
            {
                int y = board.Height - 1 - i;
                var row = new System.Text.StringBuilder();
                for (int x = 0; x < board.Width; x++)
                {
                    row.Append(LetterFor(board.PieceAt(x, y)));
                }
                rows[i] = row.ToString();
            }
            return rows;
        }

        /// <summary>
        /// The reverse of <see cref="SetPowers"/>: draws where <paramref name="board"/>'s power
        /// gems are, top row first. 'F' is a Flame, 'S' a Star, 'H' a Hypercube, and '.' is
        /// anything else (an ordinary piece or an empty cell).
        /// </summary>
        public string[] PowerRows(BoardModel board)
        {
            var rows = new string[board.Height];
            for (int i = 0; i < board.Height; i++)
            {
                int y = board.Height - 1 - i;
                var row = new System.Text.StringBuilder();
                for (int x = 0; x < board.Width; x++)
                {
                    row.Append(MarkFor(board.PieceAt(x, y).Power));
                }
                rows[i] = row.ToString();
            }
            return rows;
        }

        private static char MarkFor(PowerKind power)
        {
            switch (power)
            {
                case PowerKind.Flame: return 'F';
                case PowerKind.Star: return 'S';
                case PowerKind.Hypercube: return 'H';
                default: return Empty;
            }
        }

        private char LetterFor(Piece piece)
        {
            if (piece.IsEmpty)
            {
                return Empty;
            }
            if (piece.Colour == null)
            {
                return Hypercube;
            }
            foreach (KeyValuePair<char, TokenType> pair in types)
            {
                if (pair.Value == piece.Colour)
                {
                    return pair.Key;
                }
            }
            return '?';
        }

        /// <summary>Destroys every token type this builder created.</summary>
        public void DestroyAll()
        {
            foreach (TokenType type in types.Values)
            {
                Object.DestroyImmediate(type);
            }
            types.Clear();
        }
    }
}
