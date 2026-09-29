using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="ThemeManager.FindById{T}"/>, the lookup used to turn a
    /// saved id from PlayerPrefs back into a theme asset.
    /// </summary>
    public class ThemeManagerTests
    {
        private BoardTheme midnight;
        private BoardTheme ocean;
        private BoardTheme[] themes;

        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            midnight = CreateTheme("midnight");
            ocean = CreateTheme("ocean");
            themes = new[] { midnight, ocean };
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
    }
}
