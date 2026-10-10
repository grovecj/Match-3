using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="ScreenShake"/>'s mappings (gems cleared to shake amount,
    /// and how a shake dies away) and for the <see cref="ScreenShakeSetting"/> toggle.
    /// </summary>
    public class ScreenShakeTests
    {
        // The setting lives in PlayerPrefs, which outlives a test (it's on disk). Remember what
        // was there and put it back, so running the tests doesn't change the player's choice.
        private bool hadSetting;
        private int savedSetting;

        [SetUp]
        public void SetUp()
        {
            hadSetting = PlayerPrefs.HasKey(ScreenShakeSetting.PrefsKey);
            savedSetting = PlayerPrefs.GetInt(ScreenShakeSetting.PrefsKey, 1);
            PlayerPrefs.DeleteKey(ScreenShakeSetting.PrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            if (hadSetting)
            {
                PlayerPrefs.SetInt(ScreenShakeSetting.PrefsKey, savedSetting);
            }
            else
            {
                PlayerPrefs.DeleteKey(ScreenShakeSetting.PrefsKey);
            }
            PlayerPrefs.Save();
        }

        [Test]
        public void SmallClears_DontShake()
        {
            for (int gems = 0; gems <= ScreenShake.GemsBeforeShaking; gems++)
            {
                Assert.AreEqual(0f, ScreenShake.ShakeAmount(gems), $"Clearing {gems} gems shouldn't shake the screen.");
            }
        }

        [Test]
        public void MoreGems_ShakeHarder()
        {
            float previous = 0f;
            for (int gems = ScreenShake.GemsBeforeShaking + 1; gems <= ScreenShake.GemsForFullShake; gems++)
            {
                float amount = ScreenShake.ShakeAmount(gems);
                Assert.That(amount, Is.GreaterThan(previous), $"{gems} gems should shake harder than {gems - 1}.");
                previous = amount;
            }
        }

        [Test]
        public void TheShake_IsCapped()
        {
            Assert.AreEqual(ScreenShake.MaxShakeCells, ScreenShake.ShakeAmount(ScreenShake.GemsForFullShake), 0.0001f);
            Assert.AreEqual(ScreenShake.MaxShakeCells, ScreenShake.ShakeAmount(500), 0.0001f,
                "Clearing the whole board must not shake more than the cap.");
            Assert.That(ScreenShake.MaxShakeCells, Is.LessThan(0.5f), "The shake is meant to stay small: under half a cell.");
        }

        [Test]
        public void AShake_DiesAwayToNothing()
        {
            Assert.AreEqual(1f, ScreenShake.Falloff(0f), 0.0001f);
            Assert.That(ScreenShake.Falloff(ScreenShake.Duration / 2f), Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(0f, ScreenShake.Falloff(ScreenShake.Duration), 0.0001f);
            Assert.AreEqual(0f, ScreenShake.Falloff(ScreenShake.Duration * 3f), 0.0001f);
        }

        [Test]
        public void TheSetting_IsOnByDefault()
        {
            Assert.IsTrue(ScreenShakeSetting.Enabled);
        }

        [Test]
        public void TurningItOff_IsSavedAndAnnounced()
        {
            int changes = 0;
            System.Action onChanged = () => changes++;
            ScreenShakeSetting.Changed += onChanged;
            try
            {
                ScreenShakeSetting.Enabled = false;
                Assert.IsFalse(ScreenShakeSetting.Enabled);
                Assert.AreEqual(0, PlayerPrefs.GetInt(ScreenShakeSetting.PrefsKey, 1), "The choice should be in PlayerPrefs, so it persists.");
                Assert.AreEqual(1, changes);

                // Setting it to what it already is changes nothing, so it isn't announced.
                ScreenShakeSetting.Enabled = false;
                Assert.AreEqual(1, changes);

                ScreenShakeSetting.Enabled = true;
                Assert.IsTrue(ScreenShakeSetting.Enabled);
                Assert.AreEqual(2, changes);
            }
            finally
            {
                ScreenShakeSetting.Changed -= onChanged;
            }
        }
    }
}
