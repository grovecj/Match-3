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
    /// Create one per test (in [SetUp]) and <see cref="DestroyAll"/> in [TearDown], since the
    /// token types it makes are Unity objects that otherwise hang around in the Editor.
    /// </summary>
    public class TestGrids
    {
        /// <summary>The character that means "empty cell".</summary>
        public const char Empty = '.';

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
                    if (letter != Empty)
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
                    board.SetTypeAt(x, y, letter == Empty ? null : Type(letter));
                }
            }
            return board;
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
