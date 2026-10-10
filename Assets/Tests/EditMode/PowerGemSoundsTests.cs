using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="PowerGemSounds"/>'s mappings: which sound each kind of gem
    /// makes, and how a chain reaction's waves climb in pitch.
    /// </summary>
    public class PowerGemSoundsTests
    {
        [Test]
        public void EachKind_HasItsOwnDetonationSound()
        {
            Assert.AreEqual(SoundId.FlameDetonate, PowerGemSounds.SoundForDetonation(PowerKind.Flame));
            Assert.AreEqual(SoundId.StarDetonate, PowerGemSounds.SoundForDetonation(PowerKind.Star));
            Assert.AreEqual(SoundId.HypercubeActivate, PowerGemSounds.SoundForDetonation(PowerKind.Hypercube));
        }

        [Test]
        public void TheFirstWave_PlaysAtNormalPitch()
        {
            Assert.AreEqual(1f, PowerGemSounds.PitchForWave(0), 0.0001f);
        }

        [Test]
        public void EachWave_IsHigherThanTheOneBefore_UntilTheCap()
        {
            for (int wave = 1; wave <= MatchSounds.MaxCascadeSemitones; wave++)
            {
                Assert.That(PowerGemSounds.PitchForWave(wave), Is.GreaterThan(PowerGemSounds.PitchForWave(wave - 1)),
                    $"Wave {wave} should be higher than wave {wave - 1}, so a chain audibly builds.");
            }
            Assert.AreEqual(PowerGemSounds.PitchForWave(MatchSounds.MaxCascadeSemitones),
                PowerGemSounds.PitchForWave(MatchSounds.MaxCascadeSemitones + 5), 0.0001f,
                "Past the cap, a long chain stops climbing rather than turning into a squeak.");
        }

        [Test]
        public void Waves_ClimbTheSameWayCascadesDo()
        {
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(MatchSounds.PitchForCascade(i), PowerGemSounds.PitchForWave(i), 0.0001f);
            }
        }
    }
}
