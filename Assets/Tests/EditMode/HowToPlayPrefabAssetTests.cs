using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Match3.Tests
{
    /// <summary>
    /// Checks the How to play card that Match3 > Build How To Play saves as a prefab: it has
    /// its window, its Close button and a picture for each power gem, all wired to the
    /// <see cref="HowToPlayMenu"/>. A missing reference would otherwise only show up as a card
    /// that won't open or close, or rows with blank pictures.
    ///
    /// A missing prefab fails the test, with what to run: the prefab is committed, so one that
    /// has gone means the game has lost its card.
    /// </summary>
    public class HowToPlayPrefabAssetTests
    {
        // The path HowToPlayBuilder saves to. (Tests can't use its constant: the Editor folder's
        // scripts are in an assembly test assemblies can't reference.)
        private const string PrefabPath = "Assets/Prefabs/UI/HowToPlay.prefab";

        private static SerializedObject LoadMenu()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No prefab at {PrefabPath}. Run Match3 > Build How To Play.");
            var menu = prefab.GetComponent<HowToPlayMenu>();
            Assert.IsNotNull(menu, $"{PrefabPath} has no HowToPlayMenu component.");
            // The fields are private, but a SerializedObject reads the saved data the way the
            // Inspector does.
            return new SerializedObject(menu);
        }

        private static HowToPlayIcon[] Icons(SerializedObject menu)
        {
            SerializedProperty icons = menu.FindProperty("icons");
            return Enumerable.Range(0, icons.arraySize)
                .Select(i => icons.GetArrayElementAtIndex(i).objectReferenceValue as HowToPlayIcon)
                .ToArray();
        }

        [Test]
        public void Prefab_HasAHiddenPanelWithACloseButtonInsideIt()
        {
            SerializedObject menu = LoadMenu();
            var panel = menu.FindProperty("panel").objectReferenceValue as GameObject;
            var close = menu.FindProperty("closeButton").objectReferenceValue as Button;

            Assert.IsNotNull(panel, "No panel: the card would have nothing to show.");
            Assert.IsFalse(panel.activeSelf, "The card must start closed; it never opens by itself.");
            Assert.IsNotNull(close, "No Close button: Esc is taken in browsers, so it's the only way out.");
            Assert.IsTrue(close.transform.IsChildOf(panel.transform), "The Close button should be on the card.");
        }

        [Test]
        public void Prefab_HasPicturesForEveryPowerGem()
        {
            HowToPlayIcon[] icons = Icons(LoadMenu());

            Assert.IsNotEmpty(icons);
            Assert.IsFalse(icons.Any(icon => icon == null), "An icon reference is missing.");
            foreach (PowerKind power in new[] { PowerKind.Flame, PowerKind.Star, PowerKind.Hypercube })
            {
                Assert.IsTrue(icons.Any(icon => icon.Power == power && icon.HasGem), $"No picture of a {power}.");
            }
        }

        [Test]
        public void Prefab_IconsAreAllOnTheCard()
        {
            SerializedObject menu = LoadMenu();
            var panel = (GameObject)menu.FindProperty("panel").objectReferenceValue;
            foreach (HowToPlayIcon icon in Icons(menu))
            {
                Assert.IsTrue(icon.transform.IsChildOf(panel.transform), $"{icon.name} isn't on the card.");
            }
        }
    }
}
