using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Match3.Tests
{
    /// <summary>
    /// Checks the real <see cref="TokenStyle"/> assets in the project (not test doubles): each
    /// one must have a sprite for every <see cref="TokenType"/>, or some tokens would be drawn
    /// blank when the player picks that style.
    /// </summary>
    public class TokenStyleAssetTests
    {
        // AssetDatabase is the Editor's index of everything under Assets/. FindAssets searches
        // it ("t:TokenStyle" = every asset of that type) and returns GUIDs, which we turn back
        // into paths and then load. It only exists in the Editor, which is fine for a test.
        private static List<T> LoadAll<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .ToList();
        }

        [Test]
        public void EveryTokenStyle_HasASpriteForEveryTokenType()
        {
            List<TokenType> types = LoadAll<TokenType>();
            List<TokenStyle> styles = LoadAll<TokenStyle>();
            Assert.That(types, Is.Not.Empty, "No TokenType assets found.");
            Assert.That(styles, Is.Not.Empty, "No TokenStyle assets found.");

            foreach (TokenStyle style in styles)
            {
                List<TokenType> missing = style.FindMissingTypes(types);
                Assert.That(missing, Is.Empty,
                    $"Token style '{style.name}' has no sprite for: {string.Join(", ", missing.Select(t => t.name))}");
            }
        }

        [Test]
        public void TokenStyleIds_AreUnique()
        {
            // The Settings menu saves the chosen style by id, so two styles sharing one would
            // make the saved choice ambiguous.
            List<string> ids = LoadAll<TokenStyle>().Select(style => style.Id).ToList();
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Has.None.Null.And.None.Empty);
        }
    }
}
