using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Match3.Tests
{
    /// <summary>
    /// Checks the real <see cref="SoundSet"/> assets in the project (not test doubles): each one
    /// must have a clip for every <see cref="SoundId"/>, or part of the game would go silent when
    /// the player picks that set.
    /// </summary>
    public class SoundSetAssetTests
    {
        // AssetDatabase is the Editor's index of everything under Assets/. FindAssets searches
        // it ("t:SoundSet" = every asset of that type) and returns GUIDs, which we turn back
        // into paths and then load.
        private static List<SoundSet> LoadAllSoundSets()
        {
            return AssetDatabase.FindAssets($"t:{nameof(SoundSet)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<SoundSet>)
                .ToList();
        }

        [Test]
        public void EverySoundSet_HasAClipForEverySound()
        {
            List<SoundSet> sets = LoadAllSoundSets();
            Assert.That(sets, Is.Not.Empty, "No SoundSet assets found.");

            foreach (SoundSet set in sets)
            {
                // Enum.GetValues lists every SoundId, so a sound added later is checked too.
                List<SoundId> missing = System.Enum.GetValues(typeof(SoundId))
                    .Cast<SoundId>()
                    .Where(sound => set.GetClip(sound) == null)
                    .ToList();
                Assert.That(missing, Is.Empty,
                    $"Sound set '{set.name}' has no clip for: {string.Join(", ", missing)}");
            }
        }

        [Test]
        public void SoundSetIds_AreUnique()
        {
            // The Settings menu saves the chosen set by id, so two sets sharing one would make
            // the saved choice ambiguous.
            List<string> ids = LoadAllSoundSets().Select(set => set.Id).ToList();
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Has.None.Null.And.None.Empty);
        }

        [Test]
        public void EverySoundSet_HasADisplayName()
        {
            // The name is the only thing a sound set's Settings option shows.
            foreach (SoundSet set in LoadAllSoundSets())
            {
                Assert.That(set.DisplayName, Is.Not.Null.And.Not.Empty, $"Sound set '{set.name}' has no display name.");
            }
        }
    }
}
