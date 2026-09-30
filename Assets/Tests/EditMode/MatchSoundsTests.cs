using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="MatchSounds"/>' two mappings: run length to sound, and
    /// cascade index to pitch. Both are plain static methods, so no scene or audio is needed.
    /// </summary>
    public class MatchSoundsTests
    {
        // [TestCase] runs the same test once per set of arguments, each shown as its own test in
        // the Test Runner.
        [TestCase(3, SoundId.Match3)]
        [TestCase(4, SoundId.Match4)]
        [TestCase(5, SoundId.Match5Plus)]
        [TestCase(6, SoundId.Match5Plus)]
        [TestCase(8, SoundId.Match5Plus)]
        public void SoundForRun_PicksTheSoundForThatLength(int runLength, SoundId expected)
        {
            Assert.AreEqual(expected, MatchSounds.SoundForRun(runLength));
        }

        [Test]
        public void PitchForCascade_IsNormalForThePlayersOwnMatch()
        {
            Assert.AreEqual(1f, MatchSounds.PitchForCascade(0), 0.0001f);
        }

        [Test]
        public void PitchForCascade_RisesOneSemitonePerCascade()
        {
            // One semitone up is 2^(1/12), about 1.0595.
            Assert.AreEqual(1.0595f, MatchSounds.PitchForCascade(1), 0.0001f);
            Assert.AreEqual(1.1225f, MatchSounds.PitchForCascade(2), 0.0001f);
        }

        [Test]
        public void PitchForCascade_StopsRisingAtTheCap()
        {
            // 6 semitones up is 2^(6/12) = the square root of 2, about 1.4142.
            float capped = MatchSounds.PitchForCascade(MatchSounds.MaxCascadeSemitones);
            Assert.AreEqual(1.4142f, capped, 0.0001f);
            Assert.AreEqual(capped, MatchSounds.PitchForCascade(MatchSounds.MaxCascadeSemitones + 1));
            Assert.AreEqual(capped, MatchSounds.PitchForCascade(50));
        }
    }
}
