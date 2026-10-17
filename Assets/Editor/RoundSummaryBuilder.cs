using Match3;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds the personal best and round summary UI (issue #107). Run it
/// from the menu bar: Match3 > Build Round Summary (after the other menu builders, since it
/// wires up the RoundStatsTracker that Match3 > Build Achievements Menu makes), then save the
/// scene. Running it again replaces what it built before.
///
/// It builds:
///   - BestLine: the "Best 84,000" line under the score in the side panel, for the ScoreView.
///   - RoundSummary: a card over the board that the RoundSummaryView fills at round over with
///     the score against the best and the round's stat lines.
/// and points the GameManager at them (and at the RoundStatsTracker).
/// </summary>
public static class RoundSummaryBuilder
{
    // Wide enough for "Flames 3 · Stars 1 · Hypercubes 0" on one line, and narrow enough to fit
    // over the board even in the narrowest landscape window (a square one leaves the board
    // 1080 - 447 = 633 units).
    private const float CardWidth = 560f;
    private static readonly Color BestLineColor = new Color(0.8f, 0.82f, 0.88f);
    private static readonly Color StatsColor = new Color(0.82f, 0.84f, 0.9f);

    [MenuItem("Match3/Build Round Summary")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        var scoreView = Object.FindAnyObjectByType<ScoreView>();
        Transform sidePanel = canvas != null ? canvas.transform.Find("ScorePanel") : null;
        if (canvas == null || gameManager == null || scoreView == null || sidePanel == null)
        {
            Debug.LogError("Build Round Summary: open the Game scene first (needs Canvas/ScorePanel, GameManager and ScoreView).");
            return;
        }
        var roundStats = Object.FindAnyObjectByType<RoundStatsTracker>();
        if (roundStats == null)
        {
            Debug.LogWarning("Build Round Summary: no RoundStatsTracker, so the stat lines will read 0. " +
                             "Run Match3 > Build Achievements Menu, then this again.");
        }

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(sidePanel, "BestLine");
        DestroyChild(canvas.transform, "RoundSummary");

        TMP_Text bestLine = BuildBestLine(sidePanel);
        RoundSummaryView view = BuildSummary(canvas.transform, sidePanel);

        SetReference(scoreView, "bestText", bestLine);
        SetReference(gameManager, "scoreView", scoreView);
        SetReference(gameManager, "roundSummaryView", view);
        SetReference(gameManager, "roundStats", roundStats);

        Undo.RegisterCreatedObjectUndo(bestLine.gameObject, "Build Round Summary");
        Undo.RegisterCreatedObjectUndo(view.gameObject, "Build Round Summary");
        // The portrait layout (issue #114) moves the best line and places the card.
        ResponsiveLayoutBuilder.RewireIfPresent();
        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = view.gameObject;
        Debug.Log("Round summary built. Save the scene (Ctrl+S) to keep it.");
    }

    /// <summary>
    /// The "Best 84,000" line: in the 40-unit gap between the score (which ends 200 units down
    /// the panel) and the TIME label (which starts at 240).
    /// </summary>
    private static TMP_Text BuildBestLine(Transform sidePanel)
    {
        TMP_Text text = AddText(sidePanel.gameObject, "BestLine", "Best 84,000", 26);
        text.color = BestLineColor;
        // Stretched across the top of the panel, 20 units in from each side, like the score.
        Anchor(text.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
            new Vector2(0, -202), new Vector2(-40, 34));
        return text;
    }

    /// <summary>
    /// The summary: a root that covers the board (ResponsiveLayout keeps it there) with the
    /// card centered in it. The root holds the RoundSummaryView and stays active; only the card
    /// is shown and hidden.
    /// </summary>
    private static RoundSummaryView BuildSummary(Transform canvas, Transform sidePanel)
    {
        GameObject root = NewUI("RoundSummary", canvas);
        // Left of the side panel, which is where the board is in the landscape layout.
        RectTransform rootRect = Rect(root);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = new Vector2(-LayoutRules.LandscapePanelWidth, 0);
        // Just above the side panel in the draw order: over the board, but under every menu
        // window and the initials prompt (those are later children of the Canvas, so they draw
        // on top).
        root.transform.SetSiblingIndex(sidePanel.GetSiblingIndex() + 1);

        GameObject card = NewUI("Card", root.transform);
        Anchor(Rect(card), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(CardWidth, 0));
        // AddImage leaves raycastTarget off, so the card never swallows a click.
        AddImage(card, WindowColor, UISprite());
        // A Vertical Layout Group stacks the texts; the Content Size Fitter makes the card as
        // tall as they are, so more stat lines later just make it taller.
        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 26, 30);
        layout.spacing = 12;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = card.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text scoreLine = AddText(card, "ScoreLine", "84,200: New best!", 38);
        scoreLine.color = HighlightColor;
        TMP_Text stats = AddText(card, "Stats",
            "Flames 3 · Stars 1 · Hypercubes 0\nBest chain: 4\nMost gems from one swap: 23", 28);
        stats.color = StatsColor;
        stats.lineSpacing = 10;

        var view = root.AddComponent<RoundSummaryView>();
        SetReference(view, "card", card);
        SetReference(view, "scoreLineText", scoreLine);
        SetReference(view, "statsText", stats);
        // Hidden in the scene, so it doesn't cover the board while editing. The view hides it
        // at runtime anyway.
        card.SetActive(false);
        return view;
    }
}
