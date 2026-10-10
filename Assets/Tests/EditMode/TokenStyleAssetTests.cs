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
        public void GameScene_UsesEveryTokenType()
        {
            // A TokenType asset the Board doesn't list never appears in play, however much art
            // it has. GetDependencies lists every asset a scene refers to without opening the
            // scene, so this can't disturb whatever is open in the Editor.
            string[] dependencies = AssetDatabase.GetDependencies("Assets/Scenes/Game.unity", recursive: true);
            List<TokenType> types = LoadAll<TokenType>();
            Assert.AreEqual(6, types.Count, "The game is balanced for six token types.");

            foreach (TokenType type in types)
            {
                Assert.That(dependencies, Does.Contain(AssetDatabase.GetAssetPath(type)),
                    $"The Game scene's Board doesn't list {type.name} in its Token Types.");
            }
        }

        [Test]
        public void TokenTypeIds_AreUnique()
        {
            List<string> ids = LoadAll<TokenType>().Select(type => type.Id).ToList();
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Has.None.Null.And.None.Empty);
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

        [Test]
        public void EveryTokenStyle_HasAColoredBurstForEveryTokenType()
        {
            // White is the default for a new entry, so a white burst almost certainly means the
            // color was never set: every token would explode in the same plain white.
            List<TokenType> types = LoadAll<TokenType>();
            foreach (TokenStyle style in LoadAll<TokenStyle>())
            {
                foreach (TokenType type in types)
                {
                    Assert.AreNotEqual(UnityEngine.Color.white, style.BurstColorFor(type),
                        $"Token style '{style.name}' has no burst color set for {type.name}.");
                }
            }
        }
    }
}
