namespace Match3
{
    /// <summary>
    /// The best score this device has finished a round with, for guests and signed-in players
    /// alike. It's what the "Best 84,000" line under the score and the round summary compare
    /// against (issue #107).
    ///
    /// A plain class with no Unity in it: where the number is kept is up to the
    /// <see cref="IPersonalBestStore"/> it's given, so the Edit Mode tests can hand it a fake.
    /// </summary>
    public class PersonalBest
    {
        private readonly IPersonalBestStore store;

        /// <summary>Reads the saved best (if any) from <paramref name="store"/>.</summary>
        public PersonalBest(IPersonalBestStore store)
        {
            this.store = store;
            if (store.TryLoad(out int saved))
            {
                Best = saved;
            }
        }

        /// <summary>
        /// The best score so far, or null before the first finished round.
        /// </summary>
        // `int?` is a "nullable int": an int that can also be null. That's how "no best yet"
        // stays different from "the best is 0".
        public int? Best { get; private set; }

        /// <summary>
        /// True if <paramref name="score"/> beats <paramref name="best"/>. A tie doesn't, and
        /// neither does anything when there's no best yet (the first round has nothing to beat).
        /// </summary>
        // Static, so the round summary and the in-round line use the very same rule.
        public static bool Beats(int score, int? best)
        {
            return best.HasValue && score > best.Value;
        }

        /// <summary>
        /// Records a finished round's score: it becomes (and is saved as) the best if it's the
        /// first round or beats the old best. Returns true if it did.
        /// </summary>
        public bool Record(int score)
        {
            if (Best.HasValue && !Beats(score, Best))
            {
                return false;
            }
            Best = score;
            store.Save(score);
            return true;
        }
    }
}
