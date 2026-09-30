using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="SoundSet"/> (looking up a clip, empty slots) and
    /// <see cref="AudioManager"/> (the saved master volume, and playing from an incomplete set).
    ///
    /// PlayerPrefs and AudioListener.volume are shared with the real game in the Editor, so each
    /// test backs up the player's saved volume first and puts both back afterwards.
    /// </summary>
    public class AudioManagerTests
    {
        private readonly List<Object> created = new List<Object>();
        private bool hadSavedVolume;
        private float savedVolume;
        private float listenerVolume;

        [SetUp]
        public void SetUp()
        {
            hadSavedVolume = PlayerPrefs.HasKey(AudioManager.SoundVolumeKey);
            savedVolume = PlayerPrefs.GetFloat(AudioManager.SoundVolumeKey);
            listenerVolume = AudioListener.volume;
            PlayerPrefs.DeleteKey(AudioManager.SoundVolumeKey);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
            {
                Object.DestroyImmediate(obj);
            }
            created.Clear();

            if (hadSavedVolume)
            {
                PlayerPrefs.SetFloat(AudioManager.SoundVolumeKey, savedVolume);
            }
            else
            {
                PlayerPrefs.DeleteKey(AudioManager.SoundVolumeKey);
            }
            PlayerPrefs.Save();
            AudioListener.volume = listenerVolume;
        }

        private AudioClip CreateClip(string name)
        {
            // A tenth of a second of silence: enough to be a real clip without any audio files.
            AudioClip clip = AudioClip.Create(name, 4410, 1, 44100, false);
            created.Add(clip);
            return clip;
        }

        /// <summary>Makes a sound set with only the Match3 slot filled in.</summary>
        private SoundSet CreateSetWithMatch3(AudioClip match3)
        {
            var set = ScriptableObject.CreateInstance<SoundSet>();
            created.Add(set);
            // The clip slots are private [SerializeField]s, so set them the way the Inspector does.
            var serialized = new SerializedObject(set);
            serialized.FindProperty("match3").objectReferenceValue = match3;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return set;
        }

        /// <summary>Makes an AudioManager using <paramref name="set"/> and runs its Awake.</summary>
        private AudioManager CreateManager(SoundSet set)
        {
            var go = new GameObject("AudioManager");
            created.Add(go);
            // RequireComponent adds the AudioSource for us.
            var manager = go.AddComponent<AudioManager>();

            var serialized = new SerializedObject(manager);
            serialized.FindProperty("soundSet").objectReferenceValue = set;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // In Edit Mode Unity doesn't call Awake on a component we add, so call it ourselves.
            typeof(AudioManager)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, null);
            return manager;
        }

        [Test]
        public void GetClip_ReturnsTheClipInThatSlot()
        {
            AudioClip clip = CreateClip("match3");
            Assert.AreSame(clip, CreateSetWithMatch3(clip).GetClip(SoundId.Match3));
        }

        [Test]
        public void GetClip_ReturnsNullForEveryEmptySlot()
        {
            SoundSet set = CreateSetWithMatch3(CreateClip("match3"));
            foreach (SoundId sound in System.Enum.GetValues(typeof(SoundId)))
            {
                if (sound != SoundId.Match3)
                {
                    Assert.IsNull(set.GetClip(sound), sound.ToString());
                }
            }
        }

        [Test]
        public void Play_DoesNothingForAnEmptySlotOrNoSet()
        {
            AudioManager manager = CreateManager(CreateSetWithMatch3(null));
            Assert.DoesNotThrow(() => manager.Play(SoundId.Swap));

            AudioManager noSet = CreateManager(null);
            Assert.DoesNotThrow(() => noSet.Play(SoundId.Match3));
        }

        [Test]
        public void Volume_DefaultsTo80PercentWhenNothingIsSaved()
        {
            AudioManager manager = CreateManager(null);
            Assert.AreEqual(AudioManager.DefaultVolume, manager.Volume);
            Assert.AreEqual(AudioManager.DefaultVolume, AudioListener.volume, 0.0001f);
        }

        [Test]
        public void Volume_IsRestoredFromTheSavedValue()
        {
            PlayerPrefs.SetFloat(AudioManager.SoundVolumeKey, 0.25f);
            AudioManager manager = CreateManager(null);
            Assert.AreEqual(0.25f, manager.Volume);
            Assert.AreEqual(0.25f, AudioListener.volume, 0.0001f);
        }

        [Test]
        public void SetVolume_AppliesAndRemembersIt()
        {
            AudioManager manager = CreateManager(null);
            manager.SetVolume(0.5f);
            Assert.AreEqual(0.5f, manager.Volume);
            Assert.AreEqual(0.5f, AudioListener.volume, 0.0001f);
            Assert.AreEqual(0.5f, PlayerPrefs.GetFloat(AudioManager.SoundVolumeKey));
        }

        [Test]
        public void SetVolume_ClampsToZeroToOne()
        {
            AudioManager manager = CreateManager(null);
            manager.SetVolume(-1f);
            Assert.AreEqual(0f, manager.Volume);
            manager.SetVolume(3f);
            Assert.AreEqual(1f, manager.Volume);
        }
    }
}
