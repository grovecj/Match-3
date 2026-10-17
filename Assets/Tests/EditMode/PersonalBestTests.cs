using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="PersonalBest"/>. They use a fake store that keeps the
    /// number in memory, so they never touch the real best in PlayerPrefs.
    /// </summary>
    public class PersonalBestTests
    {
        /// <summary>An <see cref="IPersonalBestStore"/> that keeps the best in a field.</summary>
        private class FakeStore : IPersonalBestStore
        {
            public int? Saved;
            public int SaveCount;

            public bool TryLoad(out int best)
            {
                best = Saved ?? 0;
                return Saved.HasValue;
            }

            public void Save(int best)
            {
                Saved = best;
                SaveCount++;
            }
        }

        [Test]
        public void NothingSaved_HasNoBest()
        {
            var best = new PersonalBest(new FakeStore());

            Assert.That(best.Best, Is.Null);
        }

        [Test]
        public void FirstRound_BecomesTheBest()
        {
            var store = new FakeStore();
            var best = new PersonalBest(store);

            bool recorded = best.Record(71800);

            Assert.That(recorded, Is.True);
            Assert.That(best.Best, Is.EqualTo(71800));
            Assert.That(store.Saved, Is.EqualTo(71800));
        }

        [Test]
        public void FirstRound_OfZero_IsStillABest()
        {
            // "No best" and "a best of 0" are different: after one round there is a best.
            var best = new PersonalBest(new FakeStore());

            best.Record(0);

            Assert.That(best.Best, Is.EqualTo(0));
        }

        [Test]
        public void BeatingTheBest_ReplacesIt()
        {
            var store = new FakeStore { Saved = 84000 };
            var best = new PersonalBest(store);

            bool recorded = best.Record(84200);

            Assert.That(recorded, Is.True);
            Assert.That(best.Best, Is.EqualTo(84200));
            Assert.That(store.Saved, Is.EqualTo(84200));
        }

        [Test]
        public void EqualToTheBest_IsNotANewBest()
        {
            var store = new FakeStore { Saved = 84000 };
            var best = new PersonalBest(store);

            bool recorded = best.Record(84000);

            Assert.That(recorded, Is.False);
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void MissingTheBest_KeepsIt()
        {
            var store = new FakeStore { Saved = 84000 };
            var best = new PersonalBest(store);

            bool recorded = best.Record(71800);

            Assert.That(recorded, Is.False);
            Assert.That(best.Best, Is.EqualTo(84000));
            Assert.That(store.Saved, Is.EqualTo(84000));
            Assert.That(store.SaveCount, Is.Zero);
        }

        [Test]
        public void ANewBest_SurvivesARestart()
        {
            // A "restart" is a new PersonalBest reading the same store.
            var store = new FakeStore();
            new PersonalBest(store).Record(50000);
            new PersonalBest(store).Record(62000);

            var afterRestart = new PersonalBest(store);

            Assert.That(afterRestart.Best, Is.EqualTo(62000));
        }

        [TestCase(84001, 84000, true)]
        [TestCase(84000, 84000, false)]
        [TestCase(83999, 84000, false)]
        public void Beats_OnlyAHigherScore(int score, int best, bool expected)
        {
            Assert.That(PersonalBest.Beats(score, best), Is.EqualTo(expected));
        }

        [Test]
        public void Beats_NothingWhenThereIsNoBest()
        {
            Assert.That(PersonalBest.Beats(84000, null), Is.False);
        }

        [Test]
        public void PlayerPrefsKey_IsNewForSixTokenTypes()
        {
            // Scores from before the sixth token type mustn't count, so the key must not be
            // one an older version could have written.
            Assert.That(PlayerPrefsPersonalBestStore.Key, Is.EqualTo("personalBest.v2.6types"));
        }
    }
}
