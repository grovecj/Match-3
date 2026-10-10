using Match3.Online;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the <c>/me/preferences</c> JSON (<see cref="PreferencesJson"/>): replies
    /// in the shapes the server sends, with settings missing or unknown, and writing a save.
    /// </summary>
    public class PreferencesJsonTests
    {
        [Test]
        public void Parse_ReadsTheSettingsAndTheVersion()
        {
            SavedPreferences saved = PreferencesJson.Parse(
                "{\"data\":{\"tokenStyle\":\"gems\",\"boardTheme\":\"ocean\",\"soundVolume\":0.35},\"version\":7}");

            Assert.That(saved.version, Is.EqualTo(7));
            Assert.That(saved.data.tokenStyle, Is.EqualTo("gems"));
            Assert.That(saved.data.boardTheme, Is.EqualTo("ocean"));
            Assert.That(saved.data.soundVolume, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(saved.data.HasSoundVolume, Is.True);
        }

        [Test]
        public void Parse_NothingSavedYet_IsEmptyAtVersionZero()
        {
            SavedPreferences saved = PreferencesJson.Parse("{\"data\":{},\"version\":0}");

            Assert.That(saved.version, Is.Zero);
            Assert.That(saved.data.tokenStyle, Is.Empty);
            Assert.That(saved.data.boardTheme, Is.Empty);
            Assert.That(saved.data.HasSoundVolume, Is.False);
        }

        [Test]
        public void Parse_AVolumeOfZero_IsMutedNotMissing()
        {
            SavedPreferences muted = PreferencesJson.Parse("{\"data\":{\"soundVolume\":0.0},\"version\":2}");
            SavedPreferences missing = PreferencesJson.Parse("{\"data\":{\"boardTheme\":\"ocean\"},\"version\":2}");

            Assert.That(muted.data.HasSoundVolume, Is.True);
            Assert.That(muted.data.soundVolume, Is.Zero);
            Assert.That(missing.data.HasSoundVolume, Is.False);
        }

        [Test]
        public void Parse_SkipsSettingsItDoesntKnow()
        {
            // As if saved by a newer version of the game.
            SavedPreferences saved = PreferencesJson.Parse(
                "{\"data\":{\"boardTheme\":\"ocean\",\"musicVolume\":0.5,\"colorblind\":true},\"version\":4}");

            Assert.That(saved.data.boardTheme, Is.EqualTo("ocean"));
            Assert.That(saved.version, Is.EqualTo(4));
        }

        [Test]
        public void Parse_NotAPreferencesReply_IsNull()
        {
            Assert.That(PreferencesJson.Parse("{}"), Is.Null);
            Assert.That(PreferencesJson.Parse("<!DOCTYPE html><html><head><title>Just a moment...</title>"), Is.Null);
            Assert.That(PreferencesJson.Parse(""), Is.Null);
            Assert.That(PreferencesJson.Parse(null), Is.Null);
        }

        [Test]
        public void ToJson_WritesWhatParseReads()
        {
            var change = new SavedPreferences
            {
                data = new PreferenceValues { tokenStyle = "glass", boardTheme = "midnight", soundVolume = 0.8f },
                version = 3,
            };

            string json = PreferencesJson.ToJson(change);
            SavedPreferences back = PreferencesJson.Parse(json);

            // The shape the server expects: the settings under "data", next to the version.
            Assert.That(json, Does.Contain("\"data\":{").And.Contain("\"version\":3"));
            Assert.That(back.version, Is.EqualTo(3));
            Assert.That(back.data.Matches(change.data), Is.True);
        }

        [Test]
        public void Matches_ComparesEverySetting()
        {
            var a = new PreferenceValues { tokenStyle = "gems", boardTheme = "ocean", soundVolume = 0.8f };

            Assert.That(a.Matches(new PreferenceValues { tokenStyle = "gems", boardTheme = "ocean", soundVolume = 0.8f }), Is.True);
            Assert.That(a.Matches(new PreferenceValues { tokenStyle = "glass", boardTheme = "ocean", soundVolume = 0.8f }), Is.False);
            Assert.That(a.Matches(new PreferenceValues { tokenStyle = "gems", boardTheme = "midnight", soundVolume = 0.8f }), Is.False);
            Assert.That(a.Matches(new PreferenceValues { tokenStyle = "gems", boardTheme = "ocean", soundVolume = 0.5f }), Is.False);
            Assert.That(a.Matches(null), Is.False);
        }
    }
}
