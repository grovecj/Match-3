using TMPro;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// One floating score: "+1000" (with "x2" and so on when there's a multiplier) that pops up
    /// where a clear happened, drifts upward and fades away. The bigger the multiplier, the
    /// bigger and brighter it is. <see cref="ScorePopups"/> creates these and reuses them.
    /// </summary>
    // TextMeshPro (no "UGUI") is the version of TMP text that lives in the world, among the
    // sprites, rather than on a UI Canvas. That's what lets it sit over a board cell.
    [RequireComponent(typeof(TextMeshPro))]
    public class ScorePopup : MonoBehaviour
    {
        /// <summary>How long, in seconds, a popup is on screen.</summary>
        public const float Lifetime = 0.8f;

        /// <summary>How far, in cells, a popup drifts up over its life.</summary>
        public const float Rise = 0.7f;

        // The first part of its life it spends springing up to full size; the last part, fading.
        private const float GrowTime = 0.15f;
        private const float FadeStart = 0.55f;

        // TMP font size for a popup at x1 on a 1-unit cell: about half a cell tall.
        private const float BaseFontSize = 4.5f;

        private TextMeshPro text;
        private Vector3 start;
        private float cellSize = 1f;
        private float fullScale = 1f;
        private Color color;
        private float elapsed;

        /// <summary>Fired once each time the popup has finished, so it can be reused.</summary>
        public event System.Action<ScorePopup> Finished;

        /// <summary>What the popup is showing, e.g. "+2000".</summary>
        public string Text => text != null ? text.text : "";

        private void Awake()
        {
            text = GetComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.fontSize = BaseFontSize;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            // A dark outline keeps the number readable over any gem colour.
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(0, 0, 0, 200);
        }

        /// <summary>
        /// Shows <paramref name="points"/> at <paramref name="position"/>, styled for
        /// <paramref name="multiplier"/>.
        /// </summary>
        /// <param name="sortingLayerID">The sorting layer to draw in (the tokens').</param>
        /// <param name="sortingOrder">Where in that layer: in front of the tokens and effects.</param>
        public void Show(Vector3 position, int points, int multiplier, float cellSize, int sortingLayerID, int sortingOrder)
        {
            start = position;
            this.cellSize = cellSize;
            fullScale = ScaleFor(multiplier) * cellSize;
            color = ColorFor(multiplier);
            elapsed = 0f;

            text.text = TextFor(points, multiplier);
            text.sortingLayerID = sortingLayerID;
            text.sortingOrder = sortingOrder;
            Redraw();
        }

        private void Update()
        {
            // Scaled time: popups freeze on pause along with the board.
            elapsed += Time.deltaTime;
            if (elapsed >= Lifetime)
            {
                Finished?.Invoke(this);
                return;
            }
            Redraw();
        }

        private void Redraw()
        {
            float t = elapsed / Lifetime;
            // OutCubic: quick to start rising, slowing as it goes, like something thrown upward.
            transform.position = start + Vector3.up * (Rise * cellSize * Easing.OutCubic(t));
            float grow = Easing.OutBack(elapsed / GrowTime);
            transform.localScale = Vector3.one * (fullScale * grow);

            Color faded = color;
            faded.a *= 1f - Mathf.Clamp01((t - FadeStart) / (1f - FadeStart));
            text.color = faded;
        }

        // The three mappings below are `public static` so the Edit Mode tests can check them.

        /// <summary>The popup's text: "+1000" at x1, "+2000 x2" (the x2 smaller) above that.</summary>
        public static string TextFor(int points, int multiplier)
        {
            if (multiplier <= 1)
            {
                return $"+{points}";
            }
            // <size> is a TextMeshPro "rich text" tag: it changes the size of just that part.
            return $"+{points} <size=70%>x{multiplier}</size>";
        }

        /// <summary>
        /// How big the popup is at <paramref name="multiplier"/>, relative to an x1 popup: a
        /// quarter bigger for each step, so x5 is twice the size.
        /// </summary>
        public static float ScaleFor(int multiplier)
        {
            int extra = Mathf.Clamp(multiplier, 1, ScoreRules.MaxMultiplier) - 1;
            return 1f + 0.25f * extra;
        }

        /// <summary>
        /// The popup's colour at <paramref name="multiplier"/>: a soft white at x1, warming
        /// through yellow to a bright orange-gold at the highest multiplier.
        /// </summary>
        public static Color ColorFor(int multiplier)
        {
            // How far up the multiplier scale this is: 0 at x1, 1 at the max.
            float t = Mathf.InverseLerp(1, ScoreRules.MaxMultiplier, multiplier);
            var plain = new Color(1f, 1f, 0.92f);
            var hot = new Color(1f, 0.62f, 0.1f);
            return Color.Lerp(plain, hot, t);
        }
    }
}
