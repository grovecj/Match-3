using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="SoundSet"/> (looking up a clip, empty slots) and
    /// <see cref="AudioManager"/> (the saved master volume, the saved sound set, and playing from
    /// an incomplete set).
    ///
    /// PlayerPrefs and AudioListener.volume are shared with the real game in the Editor, so each
    /// test backs up the player's saved volume and sound set first and puts them back afterwards.
    /// </summary>
    public class AudioManagerTests
    {
        private readonly List<Object> created = new List<Object>();
        private bool hadSavedVolume;
        private float savedVolume;
        private float listenerVolume;
        private bool hadSavedSoundSet;
        private string savedSoundSet;

        [SetUp]
        public void SetUp()
        {
            hadSavedVolume = PlayerPrefs.HasKey(AudioManager.SoundVolumeKey);
            savedVolume = PlayerPrefs.GetFloat(AudioManager.SoundVolumeKey);
            listenerVolume = AudioListener.volume;
            PlayerPrefs.DeleteKey(AudioManager.SoundVolumeKey);

            hadSavedSoundSet = PlayerPrefs.HasKey(AudioManager.SoundSetKey);
            savedSoundSet = PlayerPrefs.GetString(AudioManager.SoundSetKey, "");
            PlayerPrefs.DeleteKey(AudioManager.SoundSetKey);
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
            if (hadSavedSoundSet)
            {
                PlayerPrefs.SetString(AudioManager.SoundSetKey, savedSoundSet);
            }
            else
            {
                PlayerPrefs.DeleteKey(AudioManager.SoundSetKey);
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

        /// <summary>Makes a sound set with the Match3 and RoundEndHorn slots filled in.</summary>
        private SoundSet CreateSetWithMatch3AndHorn()
        {
            SoundSet set = CreateSetWithMatch3(CreateClip("match3"));
            var serialized = new SerializedObject(set);
            serialized.FindProperty("roundEndHorn").objectReferenceValue = CreateClip("horn");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return set;
        }

        /// <summary>Makes an empty sound set with the given id, as the Settings menu would list it.</summary>
        private SoundSet CreateSetWithId(string id)
        {
            var set = ScriptableObject.CreateInstance<SoundSet>();
            set.name = id;
            created.Add(set);
            var serialized = new SerializedObject(set);
            serialized.FindProperty("id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return set;
        }

        /// <summary>Puts <paramref name="clip"/> in one of <paramref name="set"/>'s slots, named by its field (e.g. "match4").</summary>
        private static void SetClip(SoundSet set, string slot, AudioClip clip)
        {
            var serialized = new SerializedObject(set);
            serialized.FindProperty(slot).objectReferenceValue = clip;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Makes an AudioManager with <paramref name="set"/> selected in the "Inspector" and
        /// <paramref name="available"/> as the sets to choose from, and runs its Awake (where it
        /// loads the saved choice).
        /// </summary>
        // `params` lets a caller list the sets as extra arguments, or leave them out entirely.
        private AudioManager CreateManager(SoundSet set, params SoundSet[] available)
        {
            var go = new GameObject("AudioManager");
            created.Add(go);
            // RequireComponent adds the AudioSource for us.
            var manager = go.AddComponent<AudioManager>();

            var serialized = new SerializedObject(manager);
            serialized.FindProperty("soundSet").objectReferenceValue = set;
            SerializedProperty list = serialized.FindProperty("soundSets");
            list.arraySize = available.Length;
            for (int i = 0; i < available.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = available[i];
            }
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
        public void Awake_AddsASecondSourceForTheHorn()
        {
            // One from RequireComponent for the quick effects, one added in Awake for the horn,
            // so a pitched cascade sound can't bend the horn while it's playing.
            AudioManager manager = CreateManager(null);
            AudioSource[] sources = manager.GetComponents<AudioSource>();
            Assert.AreEqual(2, sources.Length);
            foreach (AudioSource source in sources)
            {
                Assert.IsFalse(source.playOnAwake);
            }
        }

        [Test]
        public void Play_KeepsTheHornOnItsOwnSource()
        {
            AudioManager manager = CreateManager(CreateSetWithMatch3AndHorn());
            // GetComponents returns them in the order they were added: the RequireComponent
            // one (quick effects) first, then the one Awake adds for the horn.
            AudioSource[] sources = manager.GetComponents<AudioSource>();
            AudioSource effects = sources[0];
            AudioSource horn = sources[1];

            // A pitched-up cascade sound changes the effects source and leaves the horn's alone.
            manager.Play(SoundId.Match3, pitch: 1.4f);
            Assert.AreEqual(1.4f, effects.pitch, 0.0001f);
            Assert.AreEqual(1f, horn.pitch, 0.0001f);

            // And the horn doesn't reset the pitch of an effect that's still ringing out.
            manager.Play(SoundId.RoundEndHorn, pitch: 0.9f);
            Assert.AreEqual(1.4f, effects.pitch, 0.0001f);
            Assert.AreEqual(0.9f, horn.pitch, 0.0001f);
        }

        [Test]
        public void Preview_DoesNothingForAnEmptySlotOrNoSet()
        {
            AudioManager manager = CreateManager(null);
            Assert.DoesNotThrow(() => manager.Preview(CreateSetWithId("empty"), SoundId.Match4));
            Assert.DoesNotThrow(() => manager.Preview(null, SoundId.Match4));
        }

        [Test]
        public void Preview_PlaysFromTheGivenSetWithoutSwitching()
        {
            // Chimes (the current set) only has a Match3 clip and Arcade only a Match4 clip, so
            // which set a sound came from decides whether anything plays at all.
            SoundSet chimes = CreateSetWithId("chimes");
            SetClip(chimes, "match3", CreateClip("chime"));
            SoundSet arcade = CreateSetWithId("arcade");
            SetClip(arcade, "match4", CreateClip("blip"));
            AudioManager manager = CreateManager(chimes, chimes, arcade);
            AudioSource effects = manager.GetComponents<AudioSource>()[0];

            // An AudioSource can't be asked which one-shots it's playing, but playing a sound
            // sets the source's pitch first, so a changed pitch shows that a sound reached it.
            manager.Play(SoundId.Match3, pitch: 1.4f);
            Assert.AreEqual(1.4f, effects.pitch, 0.0001f);

            // Chimes has no Match4 clip: previewing it plays nothing and leaves the pitch alone.
            manager.Preview(chimes, SoundId.Match4);
            Assert.AreEqual(1.4f, effects.pitch, 0.0001f);

            // Arcade has one, so this plays (at normal pitch) even though Chimes is current.
            manager.Preview(arcade, SoundId.Match4);
            Assert.AreEqual(1f, effects.pitch, 0.0001f);

            Assert.AreSame(chimes, manager.SoundSet);
            Assert.IsFalse(PlayerPrefs.HasKey(AudioManager.SoundSetKey));
        }

        [Test]
        public void SoundSet_IsRestoredFromTheSavedId()
        {
            SoundSet chimes = CreateSetWithId("chimes");
            SoundSet arcade = CreateSetWithId("arcade");
            PlayerPrefs.SetString(AudioManager.SoundSetKey, "arcade");

            AudioManager manager = CreateManager(chimes, chimes, arcade);

            Assert.AreSame(arcade, manager.SoundSet);
        }

        [Test]
        public void SoundSet_KeepsTheDefaultWhenTheSavedIdIsUnknown()
        {
            // E.g. a set that was saved last session and has since been deleted.
            SoundSet chimes = CreateSetWithId("chimes");
            SoundSet arcade = CreateSetWithId("arcade");
            PlayerPrefs.SetString(AudioManager.SoundSetKey, "deleted-set");

            AudioManager manager = CreateManager(arcade, chimes, arcade);

            Assert.AreSame(arcade, manager.SoundSet);
        }

        [Test]
        public void SoundSet_FallsBackToTheFirstAvailableWhenNoneIsSelected()
        {
            SoundSet chimes = CreateSetWithId("chimes");
            SoundSet arcade = CreateSetWithId("arcade");
            PlayerPrefs.SetString(AudioManager.SoundSetKey, "deleted-set");

            // The leading null is what a deleted asset leaves behind in the Inspector's list.
            AudioManager manager = CreateManager(null, null, chimes, arcade);

            Assert.AreSame(chimes, manager.SoundSet);
        }

        [Test]
        public void SetSoundSet_SwitchesSavesAndAnnouncesIt()
        {
            SoundSet chimes = CreateSetWithId("chimes");
            SoundSet arcade = CreateSetWithId("arcade");
            AudioManager manager = CreateManager(chimes, chimes, arcade);
            int changes = 0;
            manager.SoundSetChanged += () => changes++;

            manager.SetSoundSet(arcade);

            Assert.AreSame(arcade, manager.SoundSet);
            Assert.AreEqual("arcade", PlayerPrefs.GetString(AudioManager.SoundSetKey));
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void SetSoundSet_IgnoresNullAndTheCurrentSet()
        {
            SoundSet chimes = CreateSetWithId("chimes");
            AudioManager manager = CreateManager(chimes, chimes);
            int changes = 0;
            manager.SoundSetChanged += () => changes++;

            manager.SetSoundSet(null);
            manager.SetSoundSet(chimes);

            Assert.AreSame(chimes, manager.SoundSet);
            Assert.AreEqual(0, changes);
            Assert.IsFalse(PlayerPrefs.HasKey(AudioManager.SoundSetKey));
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
