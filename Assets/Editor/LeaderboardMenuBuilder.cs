using Match3;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds the local leaderboard UI (issue #21): the Leaderboard button
/// in the side panel, the modal Leaderboard panel with its ten rows, and the modal initials
/// prompt, with every Inspector reference wired up (including the GameManager's). Run it from
/// the menu bar: Match3 > Build Leaderboard Menu, then save the scene.
///
/// Like <see cref="SettingsMenuBuilder"/>, running it again replaces what it built before.
/// </summary>
public static class LeaderboardMenuBuilder
{
    private static readonly Color ColumnHeaderColor = new Color(0.7f, 0.72f, 0.8f);
    private static readonly Color RowHighlightColor = new Color(1f, 0.8f, 0.25f, 0.35f);

    [MenuItem("Match3/Build Leaderboard Menu")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        Transform sidePanel = canvas != null ? canvas.transform.Find("ScorePanel") : null;
        if (canvas == null || gameManager == null || sidePanel == null)
        {
            Debug.LogError("Build Leaderboard Menu: open the Game scene first (needs Canvas/ScorePanel and GameManager).");
            return;
        }

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(canvas.transform, "Leaderboard");
        DestroyChild(sidePanel, "LeaderboardButton");

        Button openButton = BuildOpenButton(sidePanel);

        // Root: always active, holds the LeaderboardMenu script. Last child of the Canvas, so
        // its windows draw on top of everything else.
        GameObject root = NewUI("Leaderboard", canvas.transform);
        Stretch(Rect(root), 0);
        root.transform.SetAsLastSibling();
        var menu = root.AddComponent<LeaderboardMenu>();

        BuildPanel(root, menu);
        BuildPrompt(root, menu);
        SetReference(menu, "openButton", openButton);
        SetReference(gameManager, "leaderboardMenu", menu);

        Undo.RegisterCreatedObjectUndo(root, "Build Leaderboard Menu");
        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = root;
        Debug.Log("Leaderboard menu built. Save the scene (Ctrl+S) to keep it.");
    }

    /// <summary>The "Leaderboard" button, bottom of the side panel, just left of the gear.</summary>
    private static Button BuildOpenButton(Transform sidePanel)
    {
        GameObject go = NewUI("LeaderboardButton", sidePanel);
        // The gear is 80 wide, 24 in from the right edge; leave a 16 gap next to it.
        Anchor(Rect(go), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-120, 24), new Vector2(280, 80));
        Image background = AddImage(go, ButtonColor, UISprite());
        background.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = background;

        TMP_Text label = AddText(go, "Label", "Leaderboard", 34);
        Stretch(label.rectTransform, 0);

        Undo.RegisterCreatedObjectUndo(go, "Build Leaderboard Menu");
        return button;
    }

    // ---- Leaderboard panel ----------------------------------------------------------------

    private static void BuildPanel(GameObject root, LeaderboardMenu menu)
    {
        GameObject modal = AddModal(root, "Panel", out GameObject window);

        TMP_Text title = AddText(window, "Title", "Leaderboard", 56);
        title.fontStyle = FontStyles.Bold;
        PreferredHeight(title.gameObject, 76);

        // Column headings, laid out exactly like the rows below them.
        GameObject header = NewUI("ColumnHeaders", window.transform);
        PreferredHeight(header, 40);
        AddColumns(header, 28, "#", "Initials", "Score", out TMP_Text[] headings);
        foreach (TMP_Text heading in headings)
        {
            heading.color = ColumnHeaderColor;
        }

        // The rows go in their own Vertical Layout Group so they can sit closer together than
        // the window's other children.
        GameObject rowList = NewUI("Rows", window.transform);
        var rowLayout = rowList.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 4;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var rows = new LeaderboardRow[Leaderboard.Capacity];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = BuildRow(rowList, i + 1);
        }

        GameObject closeRow = AddCenteredRow(window, "CloseRow", 90, 0);
        Button closeButton = AddButton(closeRow.transform, "CloseButton", "Close", 300, 80);

        SetReference(menu, "panel", modal);
        SetReference(menu, "closeButton", closeButton);
        SetArray(menu, "rows", rows);

        // Hidden until opened.
        modal.SetActive(false);
    }

    private static LeaderboardRow BuildRow(GameObject rowList, int rank)
    {
        GameObject go = NewUI("Row" + rank, rowList.transform);
        PreferredHeight(go, 46);
        var row = go.AddComponent<LeaderboardRow>();

        // The highlight is first, so it's drawn behind the text.
        GameObject highlight = NewUI("Highlight", go.transform);
        Stretch(Rect(highlight), 0);
        AddImage(highlight, RowHighlightColor, UISprite());
        highlight.SetActive(false);

        AddColumns(go, 34, rank.ToString(), "---", "---", out TMP_Text[] texts);

        SetReference(row, "rankText", texts[0]);
        SetReference(row, "initialsText", texts[1]);
        SetReference(row, "scoreText", texts[2]);
        SetReference(row, "highlight", highlight);
        return row;
    }

    /// <summary>
    /// Adds the three columns (rank, initials, score) to a row. Each text is anchored to a
    /// fraction of the row's width, so all rows line up without a layout group.
    /// </summary>
    private static void AddColumns(GameObject row, float fontSize, string rank, string initials, string score, out TMP_Text[] texts)
    {
        TMP_Text rankText = AddText(row, "Rank", rank, fontSize);
        Columns(rankText.rectTransform, 0f, 0.2f);

        TMP_Text initialsText = AddText(row, "Initials", initials, fontSize);
        Columns(initialsText.rectTransform, 0.2f, 0.55f);

        TMP_Text scoreText = AddText(row, "Score", score, fontSize);
        Columns(scoreText.rectTransform, 0.55f, 1f);
        scoreText.alignment = TextAlignmentOptions.Right;
        // Keep right-aligned scores off the window's edge.
        scoreText.rectTransform.offsetMax = new Vector2(-24, 0);

        texts = new[] { rankText, initialsText, scoreText };
    }

    /// <summary>Stretches <paramref name="rt"/> over the row's full height, between two fractions of its width.</summary>
    private static void Columns(RectTransform rt, float from, float to)
    {
        rt.anchorMin = new Vector2(from, 0);
        rt.anchorMax = new Vector2(to, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ---- Initials prompt ------------------------------------------------------------------

    private static void BuildPrompt(GameObject root, LeaderboardMenu menu)
    {
        GameObject modal = AddModal(root, "Prompt", out GameObject window);

        TMP_Text title = AddText(window, "Title", "New High Score!", 56);
        title.fontStyle = FontStyles.Bold;
        PreferredHeight(title.gameObject, 76);

        TMP_Text score = AddText(window, "Score", "0", 64);
        score.fontStyle = FontStyles.Bold;
        score.color = HighlightColor;
        PreferredHeight(score.gameObject, 80);

        TMP_Text instructions = AddText(window, "Instructions", "Enter your initials", 32);
        PreferredHeight(instructions.gameObject, 44);

        GameObject fieldRow = AddCenteredRow(window, "FieldRow", 110, 0);
        TMP_InputField field = BuildInitialsField(fieldRow.transform);

        GameObject buttonRow = AddCenteredRow(window, "ButtonRow", 90, 30);
        Button skipButton = AddButton(buttonRow.transform, "SkipButton", "Skip", 260, 80);
        Button submitButton = AddButton(buttonRow.transform, "SubmitButton", "Submit", 260, 80);

        SetReference(menu, "prompt", modal);
        SetReference(menu, "promptScoreText", score);
        SetReference(menu, "initialsField", field);
        SetReference(menu, "submitButton", submitButton);
        SetReference(menu, "skipButton", skipButton);

        modal.SetActive(false);
    }

    /// <summary>
    /// Builds a TMP_InputField. It's made of a few parts: a background Image (also what gets
    /// clicked), a "Text Area" that clips the text to the box (the RectMask2D), and inside that
    /// the Text the player types into plus a Placeholder shown while it's empty.
    /// </summary>
    private static TMP_InputField BuildInitialsField(Transform parent)
    {
        GameObject go = NewUI("InitialsField", parent);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 320;
        layout.preferredHeight = 100;
        Image background = AddImage(go, Color.white, UISprite());
        background.raycastTarget = true;

        GameObject textArea = NewUI("Text Area", go.transform);
        Stretch(Rect(textArea), 10);
        textArea.AddComponent<RectMask2D>();

        TMP_Text placeholder = AddText(textArea, "Placeholder", "AAA", 60);
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(DarkTextColor.r, DarkTextColor.g, DarkTextColor.b, 0.35f);
        Stretch(placeholder.rectTransform, 0);

        TMP_Text text = AddText(textArea, "Text", "", 60);
        text.color = DarkTextColor;
        text.fontStyle = FontStyles.Bold;
        Stretch(text.rectTransform, 0);

        var field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.textViewport = Rect(textArea);
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        // The LeaderboardMenu also sets this at runtime; setting it here shows it in the Inspector.
        field.characterLimit = Leaderboard.MaxInitials;
        field.caretColor = DarkTextColor;
        field.customCaretColor = true;
        return field;
    }
}
