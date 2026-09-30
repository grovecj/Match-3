using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Checks the real Explosion prefab (not the Play Mode tests' stand-in) keeps the settings
    /// the game relies on. The prefab is built by Match3 > Build Explosion Prefab, but it can
    /// still be edited by hand afterwards, and a wrong setting here fails quietly in game:
    /// bursts that never clean up, keep playing through a pause, or lose their flash.
    /// </summary>
    public class ExplosionPrefabAssetTests
    {
        private const string PrefabPath = "Assets/Prefabs/Explosion.prefab";

        private GameObject prefab;
        private ParticleSystem shards;
        private ParticleSystem flash;

        [SetUp]
        public void SetUp()
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No prefab at {PrefabPath}. Run Match3 > Build Explosion Prefab.");
            var explosion = prefab.GetComponent<Explosion>();
            Assert.IsNotNull(explosion, "The prefab has no Explosion component.");
            shards = prefab.GetComponent<ParticleSystem>();

            // The flash field is private, so read it the way the Inspector does.
            flash = new SerializedObject(explosion).FindProperty("flash").objectReferenceValue as ParticleSystem;
        }

        [Test]
        public void Shards_PlayOnceWhenToldAndThenDestroyThemselves()
        {
            ParticleSystem.MainModule main = shards.main;
            Assert.IsFalse(main.playOnAwake, "Explosion.Play starts the burst; playing on spawn would skip its color.");
            Assert.IsFalse(main.loop, "A looping burst never finishes, so it would never destroy itself.");
            Assert.AreEqual(ParticleSystemStopAction.Destroy, main.stopAction,
                "Without Stop Action = Destroy, every burst would stay in the Hierarchy forever.");
        }

        [Test]
        public void ShardsAndFlash_UseScaledTime_SoPausingFreezesThem()
        {
            Assert.IsFalse(shards.main.useUnscaledTime, "The shards would keep moving while paused.");
            Assert.IsFalse(flash.main.useUnscaledTime, "The flash would keep playing while paused.");
        }

        [Test]
        public void Flash_IsAssignedAChildThatOnlyPlaysWhenTold()
        {
            Assert.IsNotNull(flash, "The Explosion's Flash isn't assigned, so 5+ matches get no flash.");
            Assert.IsTrue(flash.transform.IsChildOf(prefab.transform), "The Flash should be part of the prefab.");
            Assert.AreNotSame(shards, flash, "The Flash should be its own ParticleSystem, not the shards.");
            Assert.IsFalse(flash.main.playOnAwake, "The flash would go off for every match, not just 5+.");
        }

        [Test]
        public void Flash_EndsBeforeTheShards_SoItIsNeverCutOff()
        {
            // The shards' Stop Action destroys the whole prefab, flash included, once the shards
            // are done. So the flash's longest life must fit inside the shards' shortest one.
            Assert.Less(flash.main.startLifetime.constantMax, shards.main.startLifetime.constantMin);
        }
    }
}
