using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for how a <see cref="Token"/> shows its power: the Flame's glow is an
    /// overlay on a child object (<see cref="FlameGlow"/>), separate from the token's own
    /// sprite, so it doesn't depend on which <see cref="TokenStyle"/> is in use.
    /// </summary>
    public class TokenPowerOverlayTests
    {
        private TokenType type;
        private GameObject tokenObject;
        private Token token;

        [SetUp]
        public void SetUp()
        {
            type = ScriptableObject.CreateInstance<TokenType>();
            type.name = "Red";
            // Passing typeof(Token) adds the component as the GameObject is made. Token's
            // [RequireComponent] brings a SpriteRenderer along with it.
            tokenObject = new GameObject("Token", typeof(Token));
            token = tokenObject.GetComponent<Token>();
        }

        [TearDown]
        public void TearDown()
        {
            // Destroying a GameObject destroys its children too, so the glow goes with it.
            Object.DestroyImmediate(tokenObject);
            Object.DestroyImmediate(type);
        }

        [Test]
        public void OrdinaryToken_HasNoOverlay()
        {
            token.SetPiece(new Piece(type));

            Assert.IsFalse(token.ShowsPowerOverlay);
            Assert.AreEqual(0, tokenObject.transform.childCount, "An ordinary token shouldn't create a glow it doesn't need.");
        }

        [Test]
        public void FlameToken_ShowsTheGlowAsAChildOnTopOfItsSprite()
        {
            token.SetPiece(new Piece(type, PowerKind.Flame));

            Assert.AreEqual(PowerKind.Flame, token.Power);
            Assert.IsTrue(token.ShowsPowerOverlay);

            FlameGlow glow = tokenObject.GetComponentInChildren<FlameGlow>();
            Assert.IsNotNull(glow, "The Flame's glow should be on a child of the token.");
            // A child follows its parent around, which is what keeps the glow on a falling token.
            Assert.AreSame(tokenObject.transform, glow.transform.parent);
            Assert.AreEqual(Vector3.zero, glow.transform.localPosition, "The glow should sit exactly on the token.");

            var glowRenderer = glow.GetComponent<SpriteRenderer>();
            Assert.AreEqual(FlameGlow.GlowSprite, glowRenderer.sprite);
            Assert.That(glowRenderer.sortingOrder, Is.GreaterThan(tokenObject.GetComponent<SpriteRenderer>().sortingOrder),
                "The glow has to be drawn in front of the token, or the token would hide it.");
        }

        [Test]
        public void FlameToken_NeedsNoStyle_SoItWorksWithEveryStyle()
        {
            // No TokenStyle at all: the token has no sprite of its own, and the glow still shows.
            token.SetStyle(null);
            token.SetPiece(new Piece(type, PowerKind.Flame));

            Assert.IsNull(tokenObject.GetComponent<SpriteRenderer>().sprite);
            Assert.IsTrue(token.ShowsPowerOverlay);
        }

        [Test]
        public void TokenThatStopsBeingAFlame_HidesTheGlow_AndCanShowItAgain()
        {
            token.SetPiece(new Piece(type, PowerKind.Flame));
            token.SetPiece(new Piece(type));
            Assert.IsFalse(token.ShowsPowerOverlay);

            token.SetPiece(new Piece(type, PowerKind.Flame));
            Assert.IsTrue(token.ShowsPowerOverlay);
            Assert.AreEqual(1, tokenObject.transform.childCount, "The glow should be reused, not created a second time.");
        }

        [Test]
        public void ChangingStyle_KeepsTheGlow()
        {
            TokenStyle style = ScriptableObject.CreateInstance<TokenStyle>();
            token.SetPiece(new Piece(type, PowerKind.Flame));

            token.SetStyle(style);

            Assert.IsTrue(token.ShowsPowerOverlay);
            Object.DestroyImmediate(style);
        }

        [Test]
        public void GlowSprite_IsWiderThanACell()
        {
            // bounds.size is the sprite's size in world units at scale 1, which for a token
            // means "in cells".
            Assert.AreEqual(FlameGlow.SizeInCells, FlameGlow.GlowSprite.bounds.size.x, 0.001f);
            Assert.That(FlameGlow.SizeInCells, Is.GreaterThan(1f));
        }

        [Test]
        public void GlowShape_IsARing_FaintInTheMiddleAndGoneAtTheEdge()
        {
            float middle = FlameGlow.AlphaAt(0f);
            float ring = FlameGlow.AlphaAt(0.72f);

            Assert.That(ring, Is.GreaterThan(0.95f), "The ring should be close to fully opaque.");
            Assert.That(middle, Is.LessThan(0.3f), "The middle should stay faint so the gem's colour shows through.");
            Assert.That(middle, Is.GreaterThan(0f));
            Assert.AreEqual(0f, FlameGlow.AlphaAt(1f), "The sprite's edge has to be fully transparent, or it would show as a square.");
            Assert.AreEqual(0f, FlameGlow.AlphaAt(1.4f), "The corners are further out than the edge, and are transparent too.");
        }
    }
}
