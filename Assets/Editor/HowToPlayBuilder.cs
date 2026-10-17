using System.Collections.Generic;
using Match3;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds the How to play card (issue #112). Run it from the menu bar:
/// Match3 > Build How To Play (after the other menu builders), then save the scene. Running it
/// again replaces what it built before.
///
/// It builds:
///   - the card itself, saved as a prefab (Assets/Prefabs/UI/HowToPlay.prefab): a window with
///     one row per rule, each with a picture made of <see cref="HowToPlayIcon"/> cells;
///   - a How to play button in the side panel (a row above Achievements) and another on the
///     pause overlay, under Resume;
/// then puts a copy of the prefab in the scene and points it (and the GameManager) at them.
///
/// The prefab holds everything that's the same in every scene; the scene's copy adds the
/// references to things only the scene has (the buttons, the ThemeManager, the Board). Unity
/// saves those as "overrides" on the copy.
/// </summary>
public static class HowToPlayBuilder
{
    private const string PrefabFolder = "Assets/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/HowToPlay.prefab";

    private const float WindowWidth = 900f;
    private const float RowHeight = 100f;
    // The picture takes the left of each row and the text the rest.
    private const float PictureWidth = 300f;
    private const float TextLeft = PictureWidth + 20f;
    // Board cells in the shape pictures, and the power gem each shape makes.
    private const float LineCell = 34f;
    private const float ShapeCell = 30f;
    private const float GemSize = 60f;
    private const float ArrowX = 200f;
    private const float ResultX = 226f;

    private static readonly Color RowColor = new Color(0f, 0f, 0f, 0.25f);
    private static readonly Color BodyTextColor = new Color(0.86f, 0.88f, 0.94f);
    // The timer's warning red (GameManager's timerWarningColor), for the "0:00" picture.
    private static readonly Color TimeUpColor = new Color(1f, 0.3f, 0.3f);

    [MenuItem("Match3/Build How To Play")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        var themeManager = Object.FindAnyObjectByType<ThemeManager>();
        var board = Object.FindAnyObjectByType<Board>();
        Transform sidePanel = canvas != null ? canvas.transform.Find("ScorePanel") : null;
        Transform pauseOverlay = canvas != null ? canvas.transform.Find("PauseOverlay") : null;
        if (canvas == null || gameManager == null || themeManager == null || board == null ||
            sidePanel == null || pauseOverlay == null)
        {
            Debug.LogError("Build How To Play: open the Game scene first (needs Canvas/ScorePanel, Canvas/PauseOverlay, GameManager, ThemeManager and Board).");
            return;
        }

        GameObject prefab = BuildPrefab();

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(canvas.transform, "HowToPlay");
        DestroyChild(sidePanel, "HowToPlayButton");
        DestroyChild(pauseOverlay, "HowToPlayButton");

        Button sideButton = BuildSidePanelButton(sidePanel);
        Button pauseButton = BuildPauseButton(pauseOverlay);

        // InstantiatePrefab (rather than Object.Instantiate) keeps the copy linked to the
        // prefab, so rebuilding the prefab updates the scene too.
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        // Last child of the Canvas, so it draws on top of everything, the pause overlay included.
        root.transform.SetAsLastSibling();
        var menu = root.GetComponent<HowToPlayMenu>();
        SetReference(menu, "themeManager", themeManager);
        SetReference(menu, "board", board);
        SetReference(menu, "sidePanelButton", sideButton);
        SetReference(menu, "pauseButton", pauseButton);
        SetReference(gameManager, "howToPlayMenu", menu);

        Undo.RegisterCreatedObjectUndo(root, "Build How To Play");
        // The portrait layout (issue #114) moves the side panel button and fits the window.
        ResponsiveLayoutBuilder.RewireIfPresent();
        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = root;
        Debug.Log("How to play built. Save the scene (Ctrl+S) to keep it.");
    }

    // ---- Prefab ---------------------------------------------------------------------------

    private static GameObject BuildPrefab()
    {
        GameObject root = NewUI("HowToPlay", null);
        Stretch(Rect(root), 0);
        var menu = root.AddComponent<HowToPlayMenu>();

        GameObject modal = AddModal(root, "Panel", out GameObject window);
        Rect(window).sizeDelta = new Vector2(WindowWidth, 0);
        window.GetComponent<VerticalLayoutGroup>().spacing = 8;

        TMP_Text title = AddText(window, "Title", "How to play", 56);
        title.fontStyle = FontStyles.Bold;
        PreferredHeight(title.gameObject, 76);

        var icons = new List<HowToPlayIcon>();

        // Each picture uses its own colour (the number is a position in the Board's list of
        // token types), so neighbouring rows don't all look the same.
        GameObject flame = AddRow(window, "FlameRow", RowHeight,
            "<b>Line of 4: Flame.</b> Clears the 3x3 around it.");
        for (int x = 0; x < 4; x++)
        {
            icons.Add(AddCell(flame, $"Cell{x}", x * LineCell, 0, LineCell, x % 2 == 0, 0, PowerKind.None));
        }
        AddSymbol(flame, "»", ArrowX);
        icons.Add(AddCell(flame, "Flame", ResultX, 0, GemSize, true, 0, PowerKind.Flame));

        GameObject star = AddRow(window, "StarRow", RowHeight,
            "<b>L, T or +: Star.</b> Clears its row and column.");
        // An L: down the left column, then along the bottom row. The other cells are empty, so
        // the shape reads as part of a board. (y counts up from the bottom, like the canvas.)
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                bool inL = x == 0 || y == 0;
                icons.Add(AddCell(star, $"Cell{x}{y}", x * ShapeCell + 30, (y - 1) * ShapeCell, ShapeCell,
                    (x + y) % 2 == 0, inL ? 1 : -1, PowerKind.None));
            }
        }
        AddSymbol(star, "»", ArrowX);
        icons.Add(AddCell(star, "Star", ResultX, 0, GemSize, true, 1, PowerKind.Star));

        GameObject cube = AddRow(window, "HypercubeRow", RowHeight,
            "<b>Line of 5: Hypercube.</b> Swap it with a gem to clear that colour.");
        for (int x = 0; x < 5; x++)
        {
            icons.Add(AddCell(cube, $"Cell{x}", x * LineCell, 0, LineCell, x % 2 == 0, 2, PowerKind.None));
        }
        AddSymbol(cube, "»", ArrowX);
        icons.Add(AddCell(cube, "Hypercube", ResultX, 0, GemSize, true, 2, PowerKind.Hypercube));

        GameObject combo = AddRow(window, "ComboRow", RowHeight + 40,
            "<b>Swap two power gems together</b> for a bigger blast. A Hypercube with a Flame or Star turns their whole colour; two Hypercubes clear the board.");
        icons.Add(AddCell(combo, "Hypercube", 30, 0, GemSize, true, 3, PowerKind.Hypercube));
        AddSymbol(combo, "+", 120);
        icons.Add(AddCell(combo, "Flame", 150, 0, GemSize, false, 3, PowerKind.Flame));

        GameObject cascade = AddRow(window, "CascadeRow", RowHeight,
            "<b>Cascades</b> multiply points, up to x5.");
        TMP_Text multiplier = AddPictureText(cascade, "Multiplier", "x5", 30, 180, 64);
        multiplier.color = HighlightColor;
        multiplier.fontStyle = FontStyles.Bold;

        GameObject hurrah = AddRow(window, "LastHurrahRow", RowHeight,
            "<b>Last Hurrah:</b> when time runs out, leftover power gems go off.");
        AddPictureText(hurrah, "Time", "0:00", 0, 120, 44).color = TimeUpColor;
        icons.Add(AddCell(hurrah, "Flame", 140, 0, GemSize, true, 4, PowerKind.Flame));
        icons.Add(AddCell(hurrah, "Star", 225, 0, GemSize, false, 5, PowerKind.Star));

        GameObject closeRow = AddCenteredRow(window, "CloseRow", 90, 0);
        Button closeButton = AddButton(closeRow.transform, "CloseButton", "Close", 300, 80);

        SetReference(menu, "panel", modal);
        SetReference(menu, "closeButton", closeButton);
        SetArray(menu, "icons", icons.ToArray());

        // Hidden until opened.
        modal.SetActive(false);

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }
        // SaveAsPrefabAsset writes the object out as a .prefab file (replacing any old one at
        // that path, which keeps its GUID so existing references survive). The temporary
        // copy it was made from is then no longer needed.
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// One rule: a darker strip with room for a picture on the left (where the cells and
    /// symbols go, placed from the strip's left middle) and the rule's text on the right.
    /// </summary>
    private static GameObject AddRow(GameObject window, string name, float height, string text)
    {
        GameObject row = NewUI(name, window.transform);
        PreferredHeight(row, height);
        AddImage(row, RowColor, UISprite());

        GameObject picture = NewUI("Picture", row.transform);
        Anchor(Rect(picture), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(16, 0), new Vector2(PictureWidth, height));

        TMP_Text body = AddText(row, "Text", text, 28);
        body.color = BodyTextColor;
        body.alignment = TextAlignmentOptions.Left;
        // Auto Size shrinks the text (down to the minimum) if it doesn't fit in the row.
        body.enableAutoSizing = true;
        body.fontSizeMin = 20;
        body.fontSizeMax = 28;
        // From TextLeft to 16 in from the right, 8 in from the top and bottom.
        Rect(body.gameObject).anchorMin = Vector2.zero;
        Rect(body.gameObject).anchorMax = Vector2.one;
        Rect(body.gameObject).offsetMin = new Vector2(TextLeft, 8);
        Rect(body.gameObject).offsetMax = new Vector2(-16, -8);
        return picture;
    }

    /// <summary>
    /// A board cell in a picture, <paramref name="x"/> from the picture's left edge and
    /// <paramref name="y"/> above its middle. A <paramref name="tokenIndex"/> of -1 leaves it
    /// empty (no gem).
    /// </summary>
    private static HowToPlayIcon AddCell(GameObject picture, string name, float x, float y, float size,
        bool lightCell, int tokenIndex, PowerKind power)
    {
        GameObject go = NewUI(name, picture.transform);
        Anchor(Rect(go), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(x, y), new Vector2(size, size));
        // Plain squares, like the board's cells. Its colour comes from the theme when shown.
        Image cell = AddImage(go, Color.gray, null);
        var icon = go.AddComponent<HowToPlayIcon>();
        SetReference(icon, "cell", cell);
        SetValue(icon, "lightCell", p => p.boolValue = lightCell);
        SetValue(icon, "power", p => p.enumValueIndex = (int)power);

        if (tokenIndex >= 0)
        {
            SetValue(icon, "tokenIndex", p => p.intValue = tokenIndex);

            GameObject gemObject = NewUI("Gem", go.transform);
            // A little inside the cell, the way tokens sit on the board.
            Stretch(Rect(gemObject), size * 0.06f);
            Image gem = AddImage(gemObject, Color.white, null);
            gem.preserveAspect = true;
            SetReference(icon, "gem", gem);

            if (power == PowerKind.Flame || power == PowerKind.Star)
            {
                // The overlay is bigger than the gem by the same amount it is on the board, so
                // the glow rings the gem and the sparkle's points poke out past it.
                float scale = power == PowerKind.Flame ? FlameGlow.SizeInCells : StarSparkle.SizeInCells;
                GameObject overlayObject = NewUI("Overlay", gemObject.transform);
                Anchor(Rect(overlayObject), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.one * size * scale);
                SetReference(icon, "overlay", AddImage(overlayObject, Color.white, null));
            }
        }
        return icon;
    }

    /// <summary>An arrow or plus sign between the parts of a picture, centered on <paramref name="x"/>.</summary>
    private static void AddSymbol(GameObject picture, string symbol, float x)
    {
        TMP_Text text = AddPictureText(picture, "Symbol", symbol, x - 20, 40, 40);
        text.color = BodyTextColor;
    }

    private static TMP_Text AddPictureText(GameObject picture, string name, string text, float x, float width, float fontSize)
    {
        TMP_Text tmp = AddText(picture, name, text, fontSize);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        Anchor(tmp.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(x, 0), new Vector2(width, RowHeight));
        return tmp;
    }

    // SetReference only does object references; this sets any other kind of saved field.
    private static void SetValue(Object target, string field, System.Action<SerializedProperty> set)
    {
        var serialized = new SerializedObject(target);
        set(serialized.FindProperty(field));
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- Scene ----------------------------------------------------------------------------

    /// <summary>
    /// The side panel's button: bottom right, in a row of its own above Achievements, and the
    /// same size. (The Start Round button ends 660 units down the panel, well above it.)
    /// </summary>
    private static Button BuildSidePanelButton(Transform sidePanel)
    {
        GameObject go = NewUI("HowToPlayButton", sidePanel);
        // Achievements is 80 tall, 216 up from the bottom; leave the same 16 gap above it.
        // The pivot is the middle, so the pulse grows it evenly in every direction.
        Anchor(Rect(go), new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f),
            new Vector2(-24 - 376 / 2f, 312 + 80 / 2f), new Vector2(376, 80));
        Image background = AddImage(go, ButtonColor, UISprite());
        background.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = background;

        TMP_Text label = AddText(go, "Label", "How to play", 34);
        Stretch(label.rectTransform, 12);

        Undo.RegisterCreatedObjectUndo(go, "Build How To Play");
        return button;
    }

    /// <summary>The pause overlay's button: under Resume (which is 80 below the middle), and styled like it.</summary>
    private static Button BuildPauseButton(Transform pauseOverlay)
    {
        Button button = AddButton(pauseOverlay, "HowToPlayButton", "How to play", 300, 80);
        Anchor(Rect(button.gameObject), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -180), new Vector2(300, 80));
        Undo.RegisterCreatedObjectUndo(button.gameObject, "Build How To Play");
        return button;
    }
}
