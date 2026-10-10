using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Checks the real power gem effect prefabs (built by Match3 > Build Power Gem Effects) keep
    /// the settings <see cref="PowerGemEffects"/> relies on to pool them: an effect that loops,
    /// plays on its own, or never reports that it's finished would leak or misbehave quietly.
    ///
    /// A missing prefab fails the test, with what to run: the prefabs are committed, so one
    /// that has gone (deleted, moved or renamed) means a power gem has lost its effect.
    /// </summary>
    public class PowerGemEffectPrefabAssetTests
    {
        // The same paths PowerGemEffectsBuilder saves to. (Tests can't use its constants: the
        // Editor folder's scripts are in an assembly test assemblies can't reference.)
        // [TestCaseSource] runs a test once per entry, each listed as its own test.
        private static readonly string[] Paths =
        {
            "Assets/Prefabs/Effects/GemCreated.prefab",
            "Assets/Prefabs/Effects/FlameBlast.prefab",
            "Assets/Prefabs/Effects/StarBurst.prefab",
            "Assets/Prefabs/Effects/ZapSpark.prefab",
        };

        private static ParticleEffect Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"No prefab at {path}. Run Match3 > Build Power Gem Effects.");
            var effect = prefab.GetComponent<ParticleEffect>();
            Assert.IsNotNull(effect, $"{path} has no ParticleEffect component.");
            return effect;
        }

        [TestCaseSource(nameof(Paths))]
        public void Sparks_PlayOnceWhenToldAndReportWhenDone(string path)
        {
            ParticleSystem.MainModule main = Load(path).GetComponent<ParticleSystem>().main;
            Assert.IsFalse(main.playOnAwake, "ParticleEffect.Play starts it once it's coloured; a pooled copy must not play on its own.");
            Assert.IsFalse(main.loop, "A looping effect never finishes, so it would never go back to its pool.");
            Assert.AreEqual(ParticleSystemStopAction.Callback, main.stopAction,
                "Without Stop Action = Callback the effect never tells its pool it's done.");
            Assert.IsFalse(main.useUnscaledTime, "The effect would keep playing while the game is paused.");
        }

        [TestCaseSource(nameof(Paths))]
        public void Effects_StayShort_SoChainsStaySnappy(string path)
        {
            // The issue asks for about 0.4 s per wave.
            Assert.That(Load(path).GetComponent<ParticleSystem>().main.startLifetime.constantMax, Is.LessThanOrEqualTo(0.4f));
        }

        [TestCaseSource(nameof(Paths))]
        public void Glow_IsAQuietChildThatEndsBeforeTheSparks(string path)
        {
            ParticleEffect effect = Load(path);
            var glow = new SerializedObject(effect).FindProperty("glow").objectReferenceValue as ParticleSystem;
            if (glow == null)
            {
                return;
            }
            ParticleSystem sparks = effect.GetComponent<ParticleSystem>();
            Assert.IsTrue(glow.transform.IsChildOf(effect.transform), "The glow should be part of the prefab.");
            Assert.IsFalse(glow.main.playOnAwake);
            Assert.AreEqual(ParticleSystemStopAction.None, glow.main.stopAction,
                "Only the sparks report the end; a glow reporting too would release the effect twice.");
            Assert.Less(glow.main.startLifetime.constantMax, sparks.main.startLifetime.constantMin,
                "The effect goes back to its pool when the sparks end, so the glow must have faded by then.");
        }
    }
}
