using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// Switches the UI between the landscape layout (side panel on the right, as designed in
    /// the scene) and the portrait layout for phones (panel along the bottom, board above it),
    /// whenever the screen's size or safe area changes (issue #114). The rules live in
    /// <see cref="LayoutRules"/>; this component only applies them.
    ///
    /// It only moves and resizes UI. It never touches the game, so rotating a phone mid-round
    /// leaves the round, the timer and any swap or cascade exactly as they were.
    ///
    /// Built and wired by Match3 > Build Responsive Layout. Every reference is optional:
    /// anything left empty is just not moved.
    /// </summary>
    // Unity runs Update in "execution order", lowest first. CanvasScaler applies its settings in
    // its own Update (order 0), so going first means a new layout's scale is used the same frame.
    [DefaultExecutionOrder(-100)]
    public class ResponsiveLayout : MonoBehaviour
    {
        // Portrait panel measurements, in canvas units (1080 span the screen's width).
        private const float Margin = 24f;
        private const float Gap = 16f;
        private const float TopRowY = 24f;
        private const float TopRowHeight = 90f;
        private const float MessageY = 114f;
        private const float MessageHeight = 90f;
        private const float RoundButtonY = 220f;
        private const float ButtonRowY = 366f;
        // 130 units at 1080 across a 390-point-wide phone is ~47 points, above the 44 point
        // minimum tap target Apple recommends.
        private const float ButtonHeight = 130f;
        private const float MinLabelSize = 18f;

        [SerializeField] private CanvasScaler canvasScaler;
        [Tooltip("The panel with the score, timer and buttons (ScorePanel).")]
        [SerializeField] private RectTransform sidePanel;

        [Header("Panel contents")]
        [SerializeField] private RectTransform scoreLabel;
        [SerializeField] private RectTransform scoreValue;
        [SerializeField] private RectTransform timeLabel;
        [SerializeField] private RectTransform timerValue;
        [SerializeField] private RectTransform roundOverMessage;
        [SerializeField] private RectTransform roundButton;
        [Tooltip("Shown left to right along the bottom of the portrait panel.")]
        [SerializeField] private RectTransform[] menuButtons = new RectTransform[0];

        [Header("Placed over the board")]
        [Tooltip("Covers the board while paused, but not the panel.")]
        [SerializeField] private RectTransform pauseOverlay;
        [Tooltip("The achievement toast's card, centered over the board at the top of the safe area.")]
        [SerializeField] private RectTransform achievementToast;

        [Header("Windows")]
        [Tooltip("Modal windows, shrunk to fit if they're bigger than the screen.")]
        [SerializeField] private RectTransform[] windows = new RectTransform[0];
        [SerializeField, Min(0f)] private float windowMargin = 24f;

        /// <summary>Raised when the layout switches between landscape and portrait.</summary>
        public event Action<ScreenLayout> LayoutChanged;

        public ScreenLayout Layout => current.Layout;

        /// <summary>The part of the screen (in pixels) the board should be fitted into.</summary>
        public Rect BoardRect => current.BoardRect;

        private LayoutResult current;
        private Vector2 lastScreenSize;
        private Rect lastSafeArea;
        private bool applied;

        // How every panel child looks in the scene, which is the landscape layout. Saved before
        // anything moves, so going back to landscape restores the scene exactly.
        private RectSnapshot[] landscapeContents;
        private RectTransform[] contents;
        private TMP_Text[] buttonLabels;
        private float[] labelSizes;

        private void Awake()
        {
            contents = new RectTransform[6 + menuButtons.Length];
            contents[0] = scoreLabel;
            contents[1] = scoreValue;
            contents[2] = timeLabel;
            contents[3] = timerValue;
            contents[4] = roundOverMessage;
            contents[5] = roundButton;
            menuButtons.CopyTo(contents, 6);

            landscapeContents = new RectSnapshot[contents.Length];
            for (int i = 0; i < contents.Length; i++)
            {
                landscapeContents[i] = RectSnapshot.Capture(contents[i]);
            }

            // The buttons' labels (the gear has none) and their font sizes, which the portrait
            // layout lets shrink to fit its narrower buttons.
            buttonLabels = new TMP_Text[menuButtons.Length];
            labelSizes = new float[menuButtons.Length];
            for (int i = 0; i < menuButtons.Length; i++)
            {
                buttonLabels[i] = menuButtons[i] != null ? menuButtons[i].GetComponentInChildren<TMP_Text>(true) : null;
                labelSizes[i] = buttonLabels[i] != null ? buttonLabels[i].fontSize : 0f;
            }

            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        // LateUpdate, after the menus have filled their windows for this frame.
        private void LateUpdate()
        {
            FitWindows();
        }

        /// <summary>
        /// Re-applies the layout if the screen's size or safe area changed: a rotation, the
        /// browser's toolbar sliding in or out, or a window being resized.
        /// </summary>
        private void Refresh()
        {
            var screenSize = new Vector2(Screen.width, Screen.height);
            Rect safeArea = Screen.safeArea;
            // A minimized window can report a 0x0 screen; keep the last layout until it's back.
            if (screenSize.x <= 0f || screenSize.y <= 0f)
            {
                return;
            }
            if (applied && screenSize == lastScreenSize && safeArea == lastSafeArea)
            {
                return;
            }

            ScreenLayout previous = current.Layout;
            current = LayoutRules.Choose(screenSize, safeArea);
            lastScreenSize = screenSize;
            lastSafeArea = safeArea;
            Apply(screenSize);

            bool changed = applied && current.Layout != previous;
            applied = true;
            if (changed)
            {
                LayoutChanged?.Invoke(current.Layout);
            }
        }

        private void Apply(Vector2 screenSize)
        {
            bool portrait = current.Layout == ScreenLayout.Portrait;

            if (canvasScaler != null)
            {
                canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasScaler.referenceResolution = portrait ? LayoutRules.PortraitReference : LayoutRules.LandscapeReference;
                // 0 = match width, 1 = match height.
                canvasScaler.matchWidthOrHeight = portrait ? 0f : 1f;
            }

            if (sidePanel != null)
            {
                PlaceOnScreen(sidePanel, current.PanelRect, screenSize);
            }
            if (pauseOverlay != null)
            {
                PlaceOnScreen(pauseOverlay, current.BoardRect, screenSize);
            }
            if (achievementToast != null)
            {
                // Its anchor sits at the top middle of the board's area, and AchievementToast
                // slides it down from there by changing only anchoredPosition.y.
                var anchor = new Vector2(
                    current.BoardRect.center.x / screenSize.x,
                    current.SafeArea.yMax / screenSize.y);
                achievementToast.anchorMin = anchor;
                achievementToast.anchorMax = anchor;
                achievementToast.anchoredPosition = new Vector2(0f, achievementToast.anchoredPosition.y);
            }

            if (portrait)
            {
                ArrangePortraitPanel();
            }
            else
            {
                for (int i = 0; i < contents.Length; i++)
                {
                    landscapeContents[i].Apply(contents[i]);
                }
            }

            for (int i = 0; i < buttonLabels.Length; i++)
            {
                TMP_Text label = buttonLabels[i];
                if (label == null)
                {
                    continue;
                }
                // Auto Size shrinks the text until it fits, from fontSizeMax down to fontSizeMin.
                // Landscape turns it back off and restores the size from the scene.
                label.enableAutoSizing = portrait;
                label.fontSizeMin = MinLabelSize;
                label.fontSizeMax = labelSizes[i];
                label.fontSize = labelSizes[i];
            }
        }

        /// <summary>
        /// The portrait panel, top to bottom: "SCORE 12,400  TIME 0:42" in one row, the
        /// round-over message, a full-width Start Round button, then the menu buttons in a row.
        /// </summary>
        private void ArrangePortraitPanel()
        {
            PlaceFromTop(scoreLabel, 0f, 0.2f, TopRowY, TopRowHeight, Margin, 0f);
            PlaceFromTop(scoreValue, 0.2f, 0.6f, TopRowY, TopRowHeight, 0f, 0f);
            PlaceFromTop(timeLabel, 0.6f, 0.78f, TopRowY, TopRowHeight, 0f, 0f);
            PlaceFromTop(timerValue, 0.78f, 1f, TopRowY, TopRowHeight, 0f, Margin);
            PlaceFromTop(roundOverMessage, 0f, 1f, MessageY, MessageHeight, Margin, Margin);
            PlaceFromTop(roundButton, 0f, 1f, RoundButtonY, ButtonHeight, Margin, Margin);

            // Each button gets an equal share of the width (its anchors), then the insets take
            // the outer margins and the gaps between buttons out of those shares.
            int count = menuButtons.Length;
            float shrink = (2f * Margin + (count - 1) * Gap) / count;
            for (int i = 0; i < count; i++)
            {
                // Where this button's left edge would be if the buttons were laid end to end
                // after the left margin, with gaps, minus where its share of the width starts.
                float left = Margin + i * (Gap - shrink);
                float right = shrink - left;
                PlaceFromTop(menuButtons[i], (float)i / count, (float)(i + 1) / count, ButtonRowY, ButtonHeight, left, right);
            }
        }

        /// <summary>
        /// Shrinks any open window that's bigger than the screen (minus a margin) so it all
        /// fits, keeping its proportions. Most fit as they are: a portrait canvas is 1080
        /// units wide and at least 1080 tall, and the windows were made for 1920x1080.
        /// </summary>
        private void FitWindows()
        {
            var canvasSize = ((RectTransform)transform).rect.size;
            foreach (RectTransform window in windows)
            {
                if (window == null || !window.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Vector2 size = window.rect.size;
                if (size.x <= 0f || size.y <= 0f)
                {
                    continue;
                }
                float scale = Mathf.Min(1f,
                    (canvasSize.x - 2f * windowMargin) / size.x,
                    (canvasSize.y - 2f * windowMargin) / size.y);
                window.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>
        /// Stretches <paramref name="rt"/> over a rect given in screen pixels. Anchors are
        /// fractions of the parent, which (for a child of the Canvas) is the whole screen, so
        /// pixel / screen size gives the anchor, whatever the Canvas Scaler's scale.
        /// </summary>
        private static void PlaceOnScreen(RectTransform rt, Rect pixels, Vector2 screenSize)
        {
            rt.anchorMin = new Vector2(pixels.xMin / screenSize.x, pixels.yMin / screenSize.y);
            rt.anchorMax = new Vector2(pixels.xMax / screenSize.x, pixels.yMax / screenSize.y);
            // With the anchors at the edges, zero offsets make it exactly that size.
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Puts <paramref name="rt"/> in a band <paramref name="top"/> units below the top of its
        /// parent, spanning <paramref name="xMin"/>..<paramref name="xMax"/> of its width, minus
        /// the given insets on the left and right.
        /// </summary>
        private static void PlaceFromTop(RectTransform rt, float xMin, float xMax, float top, float height, float leftInset, float rightInset)
        {
            if (rt == null)
            {
                return;
            }
            rt.anchorMin = new Vector2(xMin, 1f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // offsetMin is the bottom-left corner's distance from anchorMin, offsetMax the
            // top-right corner's from anchorMax. Down is negative.
            rt.offsetMin = new Vector2(leftInset, -top - height);
            rt.offsetMax = new Vector2(-rightInset, -top);
        }

        /// <summary>A RectTransform's position and size, saved so it can be put back later.</summary>
        private struct RectSnapshot
        {
            private bool valid;
            private Vector2 anchorMin;
            private Vector2 anchorMax;
            private Vector2 pivot;
            private Vector2 anchoredPosition;
            private Vector2 sizeDelta;

            public static RectSnapshot Capture(RectTransform rt)
            {
                if (rt == null)
                {
                    return default;
                }
                return new RectSnapshot
                {
                    valid = true,
                    anchorMin = rt.anchorMin,
                    anchorMax = rt.anchorMax,
                    pivot = rt.pivot,
                    anchoredPosition = rt.anchoredPosition,
                    sizeDelta = rt.sizeDelta,
                };
            }

            public void Apply(RectTransform rt)
            {
                if (!valid || rt == null)
                {
                    return;
                }
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.pivot = pivot;
                rt.anchoredPosition = anchoredPosition;
                rt.sizeDelta = sizeDelta;
            }
        }
    }
}
