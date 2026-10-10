using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for how a <see cref="Token"/> shows its power: the Flame's glow
    /// (<see cref="FlameGlow"/>) and the Star's sparkle (<see cref="StarSparkle"/>) are an
    /// overlay on a child object, separate from the token's own sprite, so they don't depend on
    /// which <see cref="TokenStyle"/> is in use. A Hypercube has no colour to draw over, so it
    /// replaces the token's sprite instead (<see cref="HypercubeSprite"/>).
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

        // ---------------------------------------------------------------- The Star's sparkle

        [Test]
        public void StarToken_ShowsTheSparkleAsAChildOnTopOfItsSprite()
        {
            token.SetPiece(new Piece(type, PowerKind.Star));

            Assert.AreEqual(PowerKind.Star, token.Power);
            Assert.IsTrue(token.ShowsPowerOverlay);

            StarSparkle sparkle = tokenObject.GetComponentInChildren<StarSparkle>();
            Assert.IsNotNull(sparkle, "The Star's sparkle should be on a child of the token.");
            Assert.AreSame(tokenObject.transform, sparkle.transform.parent);
            Assert.AreEqual(Vector3.zero, sparkle.transform.localPosition, "The sparkle should sit exactly on the token.");

            var sparkleRenderer = sparkle.GetComponent<SpriteRenderer>();
            Assert.AreEqual(StarSparkle.SparkleSprite, sparkleRenderer.sprite);
            Assert.That(sparkleRenderer.sortingOrder, Is.GreaterThan(tokenObject.GetComponent<SpriteRenderer>().sortingOrder),
                "The sparkle has to be drawn in front of the token, or the token would hide it.");
        }

        [Test]
        public void StarToken_NeedsNoStyle_SoItWorksWithEveryStyle()
        {
            token.SetStyle(null);
            token.SetPiece(new Piece(type, PowerKind.Star));

            Assert.IsNull(tokenObject.GetComponent<SpriteRenderer>().sprite);
            Assert.IsTrue(token.ShowsPowerOverlay);
        }

        [Test]
        public void FlameAndStar_LookDifferent()
        {
            // "Distinguishable at a glance" can't be asserted, but its ingredients can: the two
            // gems draw different shapes, in different colours.
            token.SetPiece(new Piece(type, PowerKind.Flame));
            var overlay = tokenObject.GetComponentInChildren<FlameGlow>().GetComponent<SpriteRenderer>();
            Sprite flameSprite = overlay.sprite;
            Color flameColor = overlay.color;

            token.SetPiece(new Piece(type, PowerKind.Star));

            Assert.AreNotEqual(flameSprite, overlay.sprite);
            Assert.AreNotEqual(flameColor, overlay.color);
            Assert.That(FlameGlow.RestColor.r, Is.GreaterThan(FlameGlow.RestColor.b), "The Flame should be a warm colour.");
            Assert.That(StarSparkle.RestColor.b, Is.GreaterThan(StarSparkle.RestColor.r), "The Star should be a cool colour.");
        }

        [Test]
        public void TokenThatChangesPower_SwapsTheLookOnTheSameOverlay()
        {
            token.SetPiece(new Piece(type, PowerKind.Flame));
            token.SetPiece(new Piece(type, PowerKind.Star));

            Assert.AreEqual(1, tokenObject.transform.childCount, "Both looks share one overlay object.");
            var overlay = tokenObject.transform.GetChild(0).GetComponent<SpriteRenderer>();
            Assert.AreEqual(StarSparkle.SparkleSprite, overlay.sprite);
            // Only the script for the current power is switched on, so only it animates the overlay.
            Assert.IsTrue(overlay.GetComponent<StarSparkle>().enabled);
            Assert.IsFalse(overlay.GetComponent<FlameGlow>().enabled);

            token.SetPiece(new Piece(type, PowerKind.Flame));

            Assert.AreEqual(FlameGlow.GlowSprite, overlay.sprite);
            Assert.IsTrue(overlay.GetComponent<FlameGlow>().enabled);
            Assert.IsFalse(overlay.GetComponent<StarSparkle>().enabled);
        }

        // ---------------------------------------------------------------- The Hypercube's sprite

        [Test]
        public void HypercubeToken_DrawsItsOwnSprite_WithNoOverlay()
        {
            token.SetPiece(Piece.Hypercube);

            Assert.AreEqual(PowerKind.Hypercube, token.Power);
            Assert.IsNull(token.Type, "A Hypercube has no colour.");
            // Unlike a Flame or Star, there's no gem underneath to draw over: the cube's own
            // picture is the token's sprite.
            Assert.IsFalse(token.ShowsPowerOverlay);
            var tokenRenderer = tokenObject.GetComponent<SpriteRenderer>();
            Assert.AreEqual(HypercubeSprite.Sprite, tokenRenderer.sprite);
            Assert.AreEqual(Color.white, tokenRenderer.color, "A tint would discolour the sprite's own colours.");
        }

        [Test]
        public void HypercubeToken_LooksTheSameInEveryStyle()
        {
            token.SetPiece(Piece.Hypercube);
            var tokenRenderer = tokenObject.GetComponent<SpriteRenderer>();

            // No style at all, then a style with no entries: neither has a sprite for "no
            // colour", and the cube doesn't need one.
            token.SetStyle(null);
            Assert.AreEqual(HypercubeSprite.Sprite, tokenRenderer.sprite);

            TokenStyle style = ScriptableObject.CreateInstance<TokenStyle>();
            token.SetStyle(style);
            Assert.AreEqual(HypercubeSprite.Sprite, tokenRenderer.sprite);
            Object.DestroyImmediate(style);
        }

        [Test]
        public void TokenThatBecomesAHypercube_DropsItsOverlay_AndOneThatStopsDropsTheSprite()
        {
            // A Flame that anchors a line of 5 turns into the Hypercube that match makes.
            token.SetPiece(new Piece(type, PowerKind.Flame));
            token.SetPiece(Piece.Hypercube);

            Assert.IsFalse(token.ShowsPowerOverlay);
            var tokenRenderer = tokenObject.GetComponent<SpriteRenderer>();
            Assert.AreEqual(HypercubeSprite.Sprite, tokenRenderer.sprite);

            // And back to an ordinary piece (a redeal can do this to a token). With no style
            // here there's no sprite for it, but it mustn't keep the cube's.
            token.SetPiece(new Piece(type));
            Assert.IsNull(tokenRenderer.sprite);
        }

        [Test]
        public void HypercubeSprite_IsExactlyOneCellWide()
        {
            Assert.AreEqual(HypercubeSprite.SizeInCells, HypercubeSprite.Sprite.bounds.size.x, 0.001f);
            Assert.AreEqual(1f, HypercubeSprite.SizeInCells);
        }

        [Test]
        public void HypercubeShape_IsTwoSquareOutlinesJoinedAtTheCorners()
        {
            // On the outer outline (in the middle of a side, and in a corner) and on the inner one.
            Assert.That(HypercubeSprite.LineAlphaAt(new Vector2(0.8f, 0f)), Is.GreaterThan(0.95f));
            Assert.That(HypercubeSprite.LineAlphaAt(new Vector2(0.8f, 0.8f)), Is.GreaterThan(0.95f));
            Assert.That(HypercubeSprite.LineAlphaAt(new Vector2(0f, -0.38f)), Is.GreaterThan(0.95f));
            // On a diagonal between the two squares: a line joining their corners.
            Assert.That(HypercubeSprite.LineAlphaAt(new Vector2(-0.6f, 0.6f)), Is.GreaterThan(0.95f));

            // Between the squares but off the diagonals, and right in the middle: no line.
            Assert.AreEqual(0f, HypercubeSprite.LineAlphaAt(new Vector2(0.6f, 0f)));
            Assert.AreEqual(0f, HypercubeSprite.LineAlphaAt(Vector2.zero));
        }

        [Test]
        public void HypercubeColours_AreADarkTileWithLinesOfEveryHue_AndNothingOutside()
        {
            // The middle is the tile: dark, and nearly solid.
            Color middle = HypercubeSprite.ColorAt(Vector2.zero);
            Assert.That(middle.a, Is.GreaterThan(0.8f));
            Assert.That(middle.maxColorComponent, Is.LessThan(0.3f));

            // The same line is a different colour on each side of the cube. That's the point:
            // the cube isn't any one of the gem colours.
            Color right = HypercubeSprite.ColorAt(new Vector2(0.8f, 0f));
            Color top = HypercubeSprite.ColorAt(new Vector2(0f, 0.8f));
            Color left = HypercubeSprite.ColorAt(new Vector2(-0.8f, 0f));
            Assert.AreNotEqual(right, top);
            Assert.AreNotEqual(top, left);
            Assert.AreNotEqual(left, right);
            Assert.AreEqual(1f, right.a, 0.001f);

            // The sprite's edge has to be fully transparent, or it would show as a square.
            Assert.AreEqual(0f, HypercubeSprite.ColorAt(new Vector2(1f, 0f)).a);
            Assert.AreEqual(0f, HypercubeSprite.ColorAt(new Vector2(1f, 1f)).a);
        }

        [Test]
        public void SparkleSprite_IsWiderThanACell()
        {
            Assert.AreEqual(StarSparkle.SizeInCells, StarSparkle.SparkleSprite.bounds.size.x, 0.001f);
            Assert.That(StarSparkle.SizeInCells, Is.GreaterThan(1f));
        }

        [Test]
        public void SparkleShape_IsACross_BrightAlongTheAxesAndEmptyOnTheDiagonals()
        {
            Assert.That(StarSparkle.AlphaAt(Vector2.zero), Is.GreaterThan(0.95f), "The middle should be close to fully opaque.");

            // Halfway out along each of the four arms it's still clearly there, and equally so.
            float right = StarSparkle.AlphaAt(new Vector2(0.5f, 0f));
            Assert.That(right, Is.GreaterThan(0.4f));
            Assert.AreEqual(right, StarSparkle.AlphaAt(new Vector2(-0.5f, 0f)), 0.0001f);
            Assert.AreEqual(right, StarSparkle.AlphaAt(new Vector2(0f, 0.5f)), 0.0001f);
            Assert.AreEqual(right, StarSparkle.AlphaAt(new Vector2(0f, -0.5f)), 0.0001f);

            // The same distance out on a diagonal is between the arms: nothing there. That gap
            // is what leaves the gem's own colour showing.
            Assert.That(StarSparkle.AlphaAt(new Vector2(0.35f, 0.35f)), Is.LessThan(0.02f));

            Assert.AreEqual(0f, StarSparkle.AlphaAt(new Vector2(1f, 0f)), "The sprite's edge has to be fully transparent, or it would show as a square.");
            Assert.AreEqual(0f, StarSparkle.AlphaAt(new Vector2(0f, 1f)));
            Assert.AreEqual(0f, StarSparkle.AlphaAt(new Vector2(1f, 1f)));
        }
    }
}
