using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="ThemeManager"/>'s saved selection: the
    /// <see cref="ThemeManager.FindById{T}"/> lookup, saving the choice to PlayerPrefs, and
    /// restoring it when the manager starts up.
    ///
    /// PlayerPrefs is shared with the real game in the Editor, so each test backs up the
    /// player's saved theme first and puts it back afterwards.
    /// </summary>
    public class ThemeManagerTests
    {
        private BoardTheme midnight;
        private BoardTheme ocean;
        private BoardTheme[] themes;

        private readonly List<Object> created = new List<Object>();
        private bool hadSavedTheme;
        private string savedTheme;

        [SetUp]
        public void SetUp()
        {
            midnight = CreateTheme("midnight");
            ocean = CreateTheme("ocean");
            themes = new[] { midnight, ocean };

            hadSavedTheme = PlayerPrefs.HasKey(ThemeManager.BoardThemeKey);
            savedTheme = PlayerPrefs.GetString(ThemeManager.BoardThemeKey, "");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
            {
                Object.DestroyImmediate(obj);
            }
            created.Clear();

            if (hadSavedTheme)
            {
                PlayerPrefs.SetString(ThemeManager.BoardThemeKey, savedTheme);
            }
            else
            {
                PlayerPrefs.DeleteKey(ThemeManager.BoardThemeKey);
            }
            PlayerPrefs.Save();
        }

        private BoardTheme CreateTheme(string id)
        {
            var theme = ScriptableObject.CreateInstance<BoardTheme>();
            theme.name = id;
            created.Add(theme);

            // `id` is a private [SerializeField], so set it the way the Inspector does.
            var serialized = new SerializedObject(theme);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return theme;
        }

        /// <summary>
        /// Makes a ThemeManager offering midnight and ocean, with <paramref name="defaultTheme"/>
        /// selected in the "Inspector", and runs its Awake (where it loads the saved choice).
        /// </summary>
        private ThemeManager CreateManager(BoardTheme defaultTheme)
        {
            var go = new GameObject("ThemeManager");
            created.Add(go);
            var manager = go.AddComponent<ThemeManager>();

            var serialized = new SerializedObject(manager);
            SerializedProperty list = serialized.FindProperty("boardThemes");
            list.arraySize = themes.Length;
            for (int i = 0; i < themes.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = themes[i];
            }
            serialized.FindProperty("boardTheme").objectReferenceValue = defaultTheme;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // In Edit Mode Unity doesn't call Awake on a component we add, so call the private
            // method ourselves through reflection, just as Unity would when the game starts.
            typeof(ThemeManager)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, null);
            return manager;
        }

        private static BoardTheme Find(BoardTheme[] items, string id) =>
            ThemeManager.FindById(items, id, theme => theme.Id);

        [Test]
        public void FindById_ReturnsTheItemWithThatId()
        {
            Assert.AreSame(ocean, Find(themes, "ocean"));
            Assert.AreSame(midnight, Find(themes, "midnight"));
        }

        [Test]
        public void FindById_ReturnsNullForAnUnknownId()
        {
            // E.g. a theme that was saved last session and has since been deleted.
            Assert.IsNull(Find(themes, "deleted-theme"));
        }

        [Test]
        public void FindById_ReturnsNullForNoSavedId()
        {
            Assert.IsNull(Find(themes, ""));
            Assert.IsNull(Find(themes, null));
        }

        [Test]
        public void FindById_SkipsNullEntriesAndNullLists()
        {
            // An empty slot in the Inspector array shows up as null.
            Assert.AreSame(ocean, Find(new[] { null, ocean }, "ocean"));
            Assert.IsNull(Find(null, "ocean"));
        }

        [Test]
        public void FindById_IsCaseSensitive()
        {
            Assert.IsNull(Find(themes, "Ocean"));
        }

        [Test]
        public void SetBoardTheme_SavesTheThemesId()
        {
            ThemeManager manager = CreateManager(midnight);

            manager.SetBoardTheme(ocean);

            Assert.AreEqual("ocean", PlayerPrefs.GetString(ThemeManager.BoardThemeKey));
        }

        [Test]
        public void Awake_RestoresTheSavedTheme()
        {
            PlayerPrefs.SetString(ThemeManager.BoardThemeKey, "ocean");

            ThemeManager manager = CreateManager(midnight);

            Assert.AreSame(ocean, manager.BoardTheme);
        }

        [Test]
        public void Awake_KeepsTheDefaultWhenTheSavedIdIsUnknown()
        {
            PlayerPrefs.SetString(ThemeManager.BoardThemeKey, "deleted-theme");

            ThemeManager manager = CreateManager(midnight);

            Assert.AreSame(midnight, manager.BoardTheme);
        }

        [Test]
        public void Awake_KeepsTheDefaultWhenNothingIsSaved()
        {
            PlayerPrefs.DeleteKey(ThemeManager.BoardThemeKey);

            ThemeManager manager = CreateManager(ocean);

            Assert.AreSame(ocean, manager.BoardTheme);
        }
    }
}
