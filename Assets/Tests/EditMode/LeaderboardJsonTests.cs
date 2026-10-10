using Match3.Online;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for reading the leaderboard server's replies (<see cref="LeaderboardJson"/>),
    /// using replies in the exact shape the server sends, plus things that aren't replies from it
    /// at all.
    /// </summary>
    public class LeaderboardJsonTests
    {
        // What a proxy's error page looks like: the start of a Cloudflare challenge page.
        private const string HtmlPage = "<!DOCTYPE html><html lang=\"en-US\"><head><title>Just a moment...</title>";

        [Test]
        public void ParseRoundToken_ReadsTheToken()
        {
            Assert.That(LeaderboardJson.ParseRoundToken("{\"roundToken\":\"r-123\"}"), Is.EqualTo("r-123"));
        }

        [Test]
        public void ParseRoundToken_MissingOrNotJson_IsNull()
        {
            Assert.That(LeaderboardJson.ParseRoundToken("{}"), Is.Null);
            Assert.That(LeaderboardJson.ParseRoundToken(HtmlPage), Is.Null);
            Assert.That(LeaderboardJson.ParseRoundToken(""), Is.Null);
            Assert.That(LeaderboardJson.ParseRoundToken(null), Is.Null);
        }

        [Test]
        public void ParseStanding_ReadsBestAndRank()
        {
            Standing standing = LeaderboardJson.ParseStanding("{\"best\":84300,\"rank\":12}");

            Assert.That(standing.best, Is.EqualTo(84300));
            Assert.That(standing.rank, Is.EqualTo(12));
        }

        [Test]
        public void ParseStanding_ReadsWhatTheRoundUnlocked()
        {
            Standing standing = LeaderboardJson.ParseStanding(
                "{\"best\":9000,\"rank\":1,\"unlocked\":[{\"key\":\"first-round\",\"name\":\"Warm-Up\"," +
                "\"description\":\"Finish a round\",\"icon\":\"rounds\",\"goal\":1,\"progress\":1," +
                "\"unlockedAt\":\"2026-10-10T04:15:00.123456Z\"}]}");

            Assert.That(standing.unlocked, Has.Length.EqualTo(1));
            Assert.That(standing.unlocked[0].key, Is.EqualTo("first-round"));
            Assert.That(standing.unlocked[0].name, Is.EqualTo("Warm-Up"));
            Assert.That(standing.unlocked[0].IsUnlocked, Is.True);
        }

        [Test]
        public void ParseStanding_NothingUnlocked_IsAnEmptyListNotNull()
        {
            // Null is kept to mean "we don't know" (the reply was lost), so it must not come
            // from a reply that simply has none, or from an older server with no such field.
            Assert.That(LeaderboardJson.ParseStanding("{\"best\":9000,\"rank\":1,\"unlocked\":[]}").unlocked, Is.Empty);
            Assert.That(LeaderboardJson.ParseStanding("{\"best\":9000,\"rank\":1}").unlocked, Is.Empty);
        }

        [Test]
        public void ParseStanding_WithoutARank_IsNull()
        {
            Assert.That(LeaderboardJson.ParseStanding("{\"best\":5}"), Is.Null);
            Assert.That(LeaderboardJson.ParseStanding(HtmlPage), Is.Null);
        }

        [Test]
        public void ParseTop_ReadsTheBareArray()
        {
            GlobalEntry[] top = LeaderboardJson.ParseTop(
                "[{\"rank\":1,\"tag\":\"ADA\",\"score\":150000},{\"rank\":1,\"tag\":\"BOB\",\"score\":150000},{\"rank\":3,\"tag\":\"CY\",\"score\":90000}]");

            Assert.That(top, Has.Length.EqualTo(3));
            Assert.That(top[0].tag, Is.EqualTo("ADA"));
            Assert.That(top[0].score, Is.EqualTo(150000));
            // Tied players share a rank, and the next one skips ahead.
            Assert.That(top[1].rank, Is.EqualTo(1));
            Assert.That(top[2].rank, Is.EqualTo(3));
        }

        [Test]
        public void ParseTop_EmptyBoard_IsAnEmptyArray()
        {
            Assert.That(LeaderboardJson.ParseTop("[]"), Is.Empty);
        }

        [Test]
        public void ParseTop_NotAnArray_IsNull()
        {
            Assert.That(LeaderboardJson.ParseTop("{\"rank\":1}"), Is.Null);
            Assert.That(LeaderboardJson.ParseTop(HtmlPage), Is.Null);
            Assert.That(LeaderboardJson.ParseTop(null), Is.Null);
        }

        [Test]
        public void ParseProfile_ReadsAPlayerWithAScore()
        {
            PlayerProfile me = LeaderboardJson.ParseProfile("{\"tag\":\"ADA\",\"best\":84300,\"rank\":12}");

            Assert.That(me.tag, Is.EqualTo("ADA"));
            Assert.That(me.best, Is.EqualTo(84300));
            Assert.That(me.rank, Is.EqualTo(12));
            Assert.That(me.HasScore, Is.True);
        }

        [Test]
        public void ParseProfile_NoScoreYet_ReadsNullsAsNoScore()
        {
            PlayerProfile me = LeaderboardJson.ParseProfile("{\"tag\":\"ADA\",\"best\":null,\"rank\":null}");

            Assert.That(me.tag, Is.EqualTo("ADA"));
            Assert.That(me.HasScore, Is.False);
            Assert.That(me.best, Is.Zero);
        }

        [Test]
        public void ParseProfile_WithoutATag_IsNull()
        {
            Assert.That(LeaderboardJson.ParseProfile("{\"best\":1,\"rank\":1}"), Is.Null);
            Assert.That(LeaderboardJson.ParseProfile(HtmlPage), Is.Null);
        }

        [Test]
        public void ParseErrorCode_ReadsTheServersCode()
        {
            const string problem =
                "{\"type\":\"about:blank\",\"title\":\"Unprocessable Content\",\"status\":422," +
                "\"detail\":\"The round isn't over yet\",\"instance\":\"/match3/api/scores\",\"code\":\"round_too_short\"}";

            Assert.That(LeaderboardJson.ParseErrorCode(problem), Is.EqualTo("round_too_short"));
        }

        [Test]
        public void ParseErrorCode_NoCode_IsNull()
        {
            // A 401 from the token check has an empty body; a proxy's has HTML.
            Assert.That(LeaderboardJson.ParseErrorCode(""), Is.Null);
            Assert.That(LeaderboardJson.ParseErrorCode(HtmlPage), Is.Null);
            Assert.That(LeaderboardJson.ParseErrorCode("{\"status\":500}"), Is.Null);
        }
    }
}
