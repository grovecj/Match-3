using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="TokenStyle"/>: looking up a type's sprite and reporting
    /// which types a style doesn't cover.
    /// </summary>
    public class TokenStyleTests
    {
        private TokenType circle;
        private TokenType square;
        private TokenType triangle;
        private Sprite circleSprite;
        private Sprite squareSprite;
        private TokenStyle style;

        // Every object made here is also added to this list so TearDown can destroy them all.
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            // ScriptableObject.CreateInstance makes an in-memory instance, like a .asset file
            // that was never saved. Plenty for a test, and nothing is written to the project.
            circle = Create<TokenType>("Circle");
            square = Create<TokenType>("Square");
            triangle = Create<TokenType>("Triangle");
            circleSprite = CreateSprite("CircleSprite");
            squareSprite = CreateSprite("SquareSprite");
            style = Create<TokenStyle>("TestStyle");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
            {
                Object.DestroyImmediate(obj);
            }
            created.Clear();
        }

        private T Create<T>(string name) where T : ScriptableObject
        {
            T obj = ScriptableObject.CreateInstance<T>();
            obj.name = name;
            created.Add(obj);
            return obj;
        }

        private Sprite CreateSprite(string name)
        {
            var texture = new Texture2D(4, 4);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            created.Add(texture);
            created.Add(sprite);
            return sprite;
        }

        /// <summary>Fills the style's private entry list, the same way the Inspector would.</summary>
        // See BoardTests.ConfigureBoard for why tests go through SerializedObject.
        private void SetEntries(params (TokenType type, Sprite sprite, Color tint)[] entries)
        {
            var serialized = new SerializedObject(style);
            SerializedProperty list = serialized.FindProperty("entries");
            Assert.IsNotNull(list, "TokenStyle has no serialized field named 'entries'.");

            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("tokenType").objectReferenceValue = entries[i].type;
                entry.FindPropertyRelative("sprite").objectReferenceValue = entries[i].sprite;
                entry.FindPropertyRelative("tint").colorValue = entries[i].tint;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void TryGetVisual_TypeWithEntry_ReturnsItsSpriteAndTint()
        {
            SetEntries((circle, circleSprite, Color.white), (square, squareSprite, Color.red));

            bool found = style.TryGetVisual(square, out Sprite sprite, out Color tint);

            Assert.IsTrue(found);
            Assert.AreSame(squareSprite, sprite);
            Assert.AreEqual(Color.red, tint);
        }

        [Test]
        public void TryGetVisual_TypeWithoutEntry_ReturnsFalseAndNoSprite()
        {
            SetEntries((circle, circleSprite, Color.white));

            bool found = style.TryGetVisual(triangle, out Sprite sprite, out Color tint);

            Assert.IsFalse(found);
            Assert.IsNull(sprite);
            Assert.AreEqual(Color.white, tint);
        }

        [Test]
        public void FindMissingTypes_StyleCoversEveryType_ReturnsEmpty()
        {
            SetEntries((circle, circleSprite, Color.white), (square, squareSprite, Color.white));

            List<TokenType> missing = style.FindMissingTypes(new[] { circle, square });

            Assert.IsEmpty(missing);
        }

        [Test]
        public void FindMissingTypes_ReportsTypesWithNoEntryOrNoSprite()
        {
            // Square has an entry but its sprite was never assigned, which is just as invisible
            // as having no entry at all.
            SetEntries((circle, circleSprite, Color.white), (square, null, Color.white));

            List<TokenType> missing = style.FindMissingTypes(new[] { circle, square, triangle });

            CollectionAssert.AreEquivalent(new[] { square, triangle }, missing);
        }
    }
}
