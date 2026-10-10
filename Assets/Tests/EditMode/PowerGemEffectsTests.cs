using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for the plain logic behind the power gem effects: which gems slide into
    /// a new power gem (<see cref="Board.CollapseTargets"/>), the shape of a Hypercube's bolt
    /// (<see cref="HypercubeZap.JaggedPoints"/>), and where a step's score popup goes
    /// (<see cref="ClearStep.CenterCell"/>).
    /// </summary>
    public class PowerGemEffectsTests
    {
        private TokenType red;

        [SetUp]
        public void SetUp()
        {
            red = ScriptableObject.CreateInstance<TokenType>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(red);
        }

        private static List<Vector2Int> Row(int y, int fromX, int toX)
        {
            var cells = new List<Vector2Int>();
            for (int x = fromX; x <= toX; x++)
            {
                cells.Add(new Vector2Int(x, y));
            }
            return cells;
        }

        // ---------------------------------------------------------------- Collapse

        [Test]
        public void CollapseTargets_EveryOtherGemOfTheMatch_SlidesIntoTheNewGem()
        {
            // A line of 4 made by swapping into (2, 0): a Flame appears there.
            var group = new MatchGroup(red, new List<List<Vector2Int>> { Row(0, 0, 3) }, new Vector2Int(2, 0));
            Assert.AreEqual(new Vector2Int(2, 0), group.Anchor, "The swapped cell should be the anchor.");

            Dictionary<Vector2Int, Vector2Int> targets =
                Board.CollapseTargets(new[] { group }, new[] { group.Anchor });

            Assert.AreEqual(3, targets.Count, "Four gems, minus the one that becomes the Flame.");
            Assert.IsFalse(targets.ContainsKey(group.Anchor), "The new gem doesn't slide into itself.");
            foreach (KeyValuePair<Vector2Int, Vector2Int> pair in targets)
            {
                Assert.AreEqual(group.Anchor, pair.Value);
            }
        }

        [Test]
        public void CollapseTargets_AMatchThatMadeNoGem_DoesntSlide()
        {
            var flameGroup = new MatchGroup(red, new List<List<Vector2Int>> { Row(0, 0, 3) }, new Vector2Int(1, 0));
            var plainGroup = new MatchGroup(red, new List<List<Vector2Int>> { Row(4, 0, 2) });

            Dictionary<Vector2Int, Vector2Int> targets =
                Board.CollapseTargets(new[] { flameGroup, plainGroup }, new[] { flameGroup.Anchor });

            foreach (Vector2Int cell in plainGroup.Cells)
            {
                Assert.IsFalse(targets.ContainsKey(cell), $"{cell} was in a plain match of 3 and should just pop.");
            }
            Assert.AreEqual(3, targets.Count);
        }

        [Test]
        public void CollapseTargets_NoNewGems_NothingSlides()
        {
            var group = new MatchGroup(red, new List<List<Vector2Int>> { Row(0, 0, 2) });
            Assert.That(Board.CollapseTargets(new[] { group }, new Vector2Int[0]), Is.Empty);
        }

        // ---------------------------------------------------------------- Hypercube bolts

        [Test]
        public void JaggedPoints_StartAndEndExactlyOnTheTwoGems()
        {
            var from = new Vector3(1f, 2f, 0f);
            var to = new Vector3(5f, -1f, 0f);
            Vector3[] points = HypercubeZap.JaggedPoints(from, to, 6, 0.3f, new System.Random(1));

            Assert.AreEqual(7, points.Length, "Six pieces need seven points.");
            Assert.AreEqual(from, points[0]);
            Assert.AreEqual(to, points[6]);
        }

        [Test]
        public void JaggedPoints_KinksStayWithinTheJitter()
        {
            var from = Vector3.zero;
            var to = new Vector3(6f, 0f, 0f);
            const float jitter = 0.3f;
            Vector3[] points = HypercubeZap.JaggedPoints(from, to, 6, jitter, new System.Random(7));

            bool anyKink = false;
            for (int i = 1; i < points.Length - 1; i++)
            {
                // The line runs along x, so each kink should sit at its even step along x and
                // be pushed only up or down, by no more than the jitter.
                Assert.AreEqual(i, points[i].x, 0.0001f);
                Assert.That(Mathf.Abs(points[i].y), Is.LessThanOrEqualTo(jitter + 0.0001f));
                anyKink |= Mathf.Abs(points[i].y) > 0.0001f;
            }
            Assert.IsTrue(anyKink, "A bolt that's dead straight isn't a bolt.");
        }

        [Test]
        public void JaggedPoints_SameSeed_SamePath()
        {
            Vector3[] a = HypercubeZap.JaggedPoints(Vector3.zero, Vector3.one, 6, 0.3f, new System.Random(42));
            Vector3[] b = HypercubeZap.JaggedPoints(Vector3.zero, Vector3.one, 6, 0.3f, new System.Random(42));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ABolt_IsInvisibleUntilItsTurn_ThenFades()
        {
            Assert.AreEqual(0f, HypercubeZap.Opacity(0.05f, 0.1f));
            Assert.AreEqual(1f, HypercubeZap.Opacity(0.1f, 0.1f), 0.0001f);
            Assert.AreEqual(0f, HypercubeZap.Opacity(0.1f + HypercubeZap.FadeDuration, 0.1f), 0.0001f);
        }

        // ---------------------------------------------------------------- Score popup position

        [Test]
        public void ClearStepCenter_IsTheMiddleOfTheMatch()
        {
            var group = new MatchGroup(red, new List<List<Vector2Int>> { Row(3, 1, 3) });
            var result = new BoardModel.ClearResult(group.Cells, new BoardModel.Creation[0],
                new IReadOnlyList<BoardModel.Detonation>[0]);

            ClearStep step = ClearStep.From(result, new[] { group }, 0);

            Assert.AreEqual(new Vector2(2f, 3f), step.CenterCell);
        }

        [Test]
        public void ClearStepCenter_CountsTheBlastedGemsToo()
        {
            // A match of 3 along the bottom, and a Flame among them that blasted the three cells
            // above: the middle of all six is half a cell up from the match's middle.
            var group = new MatchGroup(red, new List<List<Vector2Int>> { Row(0, 0, 2) });
            var flame = new BoardModel.Detonation(new Vector2Int(1, 0), new Piece(red, PowerKind.Flame), Row(1, 0, 2));
            var result = new BoardModel.ClearResult(group.Cells, new BoardModel.Creation[0],
                new IReadOnlyList<BoardModel.Detonation>[] { new[] { flame } });

            ClearStep step = ClearStep.From(result, new[] { group }, 0);

            Assert.AreEqual(6, step.GemsCleared);
            Assert.AreEqual(1f, step.CenterCell.x, 0.0001f);
            Assert.AreEqual(0.5f, step.CenterCell.y, 0.0001f);
        }
    }
}
