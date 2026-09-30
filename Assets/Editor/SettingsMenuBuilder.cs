using Match3;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds the Settings menu UI (issues #17 and #49): the two option
/// prefabs, the gear button in the side panel, and the modal Settings panel (including the
/// sound volume slider), with every Inspector reference wired up. Run it from the menu bar: Match3 > Build Settings Menu, then save the
/// scene.
///
/// Scripts in a folder named "Editor" are compiled only for the Unity Editor and never ship in
/// a build, so they're the place for tools like this. It's written as code (rather than
/// clicked together by hand) so it's repeatable: running it again replaces the old UI. Once
/// built, everything is ordinary GameObjects and prefabs you can inspect and tweak by hand.
/// </summary>
public static class SettingsMenuBuilder
{
    private const string PrefabFolder = "Assets/Prefabs/UI";
    private const string TokenStyleOptionPath = PrefabFolder + "/TokenStyleOption.prefab";
    private const string BoardThemeOptionPath = PrefabFolder + "/BoardThemeOption.prefab";
    private const string GearSpritePath = "Assets/Sprites/UI/Gear.png";

    private static readonly Color OptionColor = new Color(0.22f, 0.24f, 0.32f);

    // [MenuItem] adds this method to Unity's menu bar.
    [MenuItem("Match3/Build Settings Menu")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        var themeManager = Object.FindAnyObjectByType<ThemeManager>();
        var board = Object.FindAnyObjectByType<Board>();
        var audioManager = Object.FindAnyObjectByType<AudioManager>();
        Transform sidePanel = canvas != null ? canvas.transform.Find("ScorePanel") : null;
        if (canvas == null || gameManager == null || themeManager == null || board == null ||
            audioManager == null || sidePanel == null)
        {
            Debug.LogError("Build Settings Menu: open the Game scene first (needs Canvas/ScorePanel, GameManager, ThemeManager, Board and AudioManager).");
            return;
        }

        Sprite gearSprite = ImportGearSprite();
        TokenStyleOption tokenStyleOptionPrefab = BuildTokenStyleOptionPrefab();
        BoardThemeOption boardThemeOptionPrefab = BuildBoardThemeOptionPrefab();

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(canvas.transform, "Settings");
        DestroyChild(sidePanel, "SettingsButton");

        Button gearButton = BuildGearButton(sidePanel, gearSprite);
        BuildSettingsPanel(canvas.transform, themeManager, board, audioManager, gearButton,
            tokenStyleOptionPrefab, boardThemeOptionPrefab, out SettingsMenu settingsMenu);

        SetReference(gameManager, "settingsMenu", settingsMenu);

        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = settingsMenu.gameObject;
        Debug.Log("Settings menu built. Save the scene (Ctrl+S) to keep it.");
    }

    // ---- Assets ---------------------------------------------------------------------------

    /// <summary>Makes sure Gear.png is imported as a UI sprite, and returns it.</summary>
    private static Sprite ImportGearSprite()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(GearSpritePath);
        if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(GearSpritePath);
    }

    private static TokenStyleOption BuildTokenStyleOptionPrefab()
    {
        GameObject root = NewUI("TokenStyleOption", null);
        Size(root, 400, 130);
        var option = root.AddComponent<TokenStyleOption>();
        AddOptionFrame(root, out GameObject highlight);

        TMP_Text label = AddText(root, "Label", "Style", 28);
        Anchor(label.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(-20, 40));

        GameObject previewRow = NewUI("PreviewRow", root.transform);
        Anchor(Rect(previewRow), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(-20, 60));
        var row = previewRow.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 10;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        WireOption(option, label, highlight);
        SetReference(option, "previewRow", Rect(previewRow));
        return SavePrefab(root, TokenStyleOptionPath).GetComponent<TokenStyleOption>();
    }

    private static BoardThemeOption BuildBoardThemeOptionPrefab()
    {
        const int columns = 3;
        const float cellSize = 32;
        const float swatchPadding = 12;
        float swatchSize = columns * cellSize + 2 * swatchPadding;

        GameObject root = NewUI("BoardThemeOption", null);
        Size(root, 190, 200);
        var option = root.AddComponent<BoardThemeOption>();
        AddOptionFrame(root, out GameObject highlight);

        GameObject swatch = NewUI("Swatch", root.transform);
        Anchor(Rect(swatch), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(swatchSize, swatchSize));
        Image swatchBackground = AddImage(swatch, Color.gray, null);
        var grid = swatch.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset((int)swatchPadding, (int)swatchPadding, (int)swatchPadding, (int)swatchPadding);
        grid.cellSize = new Vector2(cellSize, cellSize);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;

        var cells = new Image[columns * columns];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = AddImage(NewUI("Cell" + i, swatch.transform), Color.white, null);
        }

        TMP_Text label = AddText(root, "Label", "Theme", 26);
        Anchor(label.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(-20, 40));

        WireOption(option, label, highlight);
        SetReference(option, "swatchBackground", swatchBackground);
        SetArray(option, "swatchCells", cells);
        var serialized = new SerializedObject(option);
        serialized.FindProperty("swatchColumns").intValue = columns;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root, BoardThemeOptionPath).GetComponent<BoardThemeOption>();
    }

    /// <summary>
    /// Adds the parts every option shares: a gold highlight filling the whole option, and the
    /// option's background inset a few pixels on top of it, so the gold shows as a border.
    /// </summary>
    private static void AddOptionFrame(GameObject root, out GameObject highlight)
    {
        highlight = NewUI("SelectedHighlight", root.transform);
        Stretch(Rect(highlight), 0);
        AddImage(highlight, HighlightColor, UISprite());

        GameObject backgroundObject = NewUI("Background", root.transform);
        Stretch(Rect(backgroundObject), 5);
        Image background = AddImage(backgroundObject, OptionColor, UISprite());
        // The background is what gets clicked, so it's the one graphic that catches raycasts.
        background.raycastTarget = true;

        var button = root.AddComponent<Button>();
        button.targetGraphic = background;
    }

    private static void WireOption(SettingsOption option, TMP_Text label, GameObject highlight)
    {
        SetReference(option, "button", option.GetComponent<Button>());
        SetReference(option, "label", label);
        SetReference(option, "selectedHighlight", highlight);
    }

    private static GameObject SavePrefab(GameObject root, string path)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }
        // SaveAsPrefabAsset writes the object out as a .prefab file (replacing any old one at
        // that path, which keeps its GUID so existing references survive). The temporary
        // scene copy is then no longer needed.
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---- Scene ----------------------------------------------------------------------------

    private static Button BuildGearButton(Transform sidePanel, Sprite gearSprite)
    {
        GameObject gear = NewUI("SettingsButton", sidePanel);
        // Bottom-right corner of the side panel.
        Anchor(Rect(gear), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(80, 80));
        Image background = AddImage(gear, ButtonColor, UISprite());
        background.raycastTarget = true;
        var button = gear.AddComponent<Button>();
        button.targetGraphic = background;

        GameObject icon = NewUI("Icon", gear.transform);
        Stretch(Rect(icon), 12);
        AddImage(icon, Color.white, gearSprite).preserveAspect = true;

        Undo.RegisterCreatedObjectUndo(gear, "Build Settings Menu");
        return button;
    }

    private static void BuildSettingsPanel(Transform canvas, ThemeManager themeManager, Board board,
        AudioManager audioManager, Button gearButton,
        TokenStyleOption tokenStyleOptionPrefab, BoardThemeOption boardThemeOptionPrefab, out SettingsMenu menu)
    {
        // Root: always active, holds the SettingsMenu script. Last child of the Canvas, so it
        // draws on top of everything else.
        GameObject root = NewUI("Settings", canvas);
        Stretch(Rect(root), 0);
        root.transform.SetAsLastSibling();
        menu = root.AddComponent<SettingsMenu>();

        // Modal: a dimmed full-screen backdrop that swallows clicks. Toggled open/closed.
        GameObject modal = NewUI("Modal", root.transform);
        Stretch(Rect(modal), 0);
        AddImage(modal, new Color(0, 0, 0, 0.6f), null).raycastTarget = true;

        // Window: the panel itself. A Vertical Layout Group stacks its children top to bottom,
        // and the Content Size Fitter grows the window to fit them, so adding more options
        // (or whole new sections later) needs no manual resizing.
        GameObject window = NewUI("Window", modal.transform);
        Anchor(Rect(window), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 0));
        AddImage(window, WindowColor, UISprite());
        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 30, 40);
        layout.spacing = 16;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = window.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text title = AddText(window, "Title", "Settings", 56);
        title.fontStyle = FontStyles.Bold;
        PreferredHeight(title.gameObject, 76);

        RectTransform tokenStyleList = AddSection(window, "TokenStyle", "Token Style", new Vector2(400, 130));
        RectTransform boardThemeList = AddSection(window, "BoardColors", "Board Colors", new Vector2(190, 200));
        AddSoundSection(window, out Slider volumeSlider, out TMP_Text volumeValueText, out Button testSoundButton);

        // Close button, centered in its own row.
        GameObject closeRow = AddCenteredRow(window, "CloseRow", 90, 0);
        Button closeButton = AddButton(closeRow.transform, "CloseButton", "Close", 300, 80);

        SetReference(menu, "themeManager", themeManager);
        SetReference(menu, "board", board);
        SetReference(menu, "openButton", gearButton);
        SetReference(menu, "modal", modal);
        SetReference(menu, "closeButton", closeButton);
        SetReference(menu, "tokenStyleList", tokenStyleList);
        SetReference(menu, "tokenStyleOptionPrefab", tokenStyleOptionPrefab);
        SetReference(menu, "boardThemeList", boardThemeList);
        SetReference(menu, "boardThemeOptionPrefab", boardThemeOptionPrefab);
        SetReference(menu, "audioManager", audioManager);
        SetReference(menu, "volumeSlider", volumeSlider);
        SetReference(menu, "volumeValueText", volumeValueText);
        SetReference(menu, "testSoundButton", testSoundButton);

        // Hidden until the gear is clicked.
        modal.SetActive(false);
        Undo.RegisterCreatedObjectUndo(root, "Build Settings Menu");
    }

    /// <summary>
    /// Adds the "Sound Volume" heading and a row below it: the volume slider, its value as a
    /// percentage, and a Test button that plays a sample sound.
    /// </summary>
    private static void AddSoundSection(GameObject window, out Slider slider, out TMP_Text valueText, out Button testButton)
    {
        TMP_Text header = AddText(window, "SoundHeader", "Sound Volume", 34);
        header.alignment = TextAlignmentOptions.Left;
        PreferredHeight(header.gameObject, 48);

        GameObject row = NewUI("SoundRow", window.transform);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 24;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        PreferredHeight(row, 80);

        slider = AddSlider(row.transform, "VolumeSlider");
        // 0-100 in whole numbers means each arrow-key press moves the volume by 1%.
        slider.minValue = 0;
        slider.maxValue = 100;
        slider.wholeNumbers = true;
        slider.value = AudioManager.DefaultVolume * 100;
        // flexibleWidth makes the slider take whatever width the label and button leave over.
        var sliderLayout = slider.gameObject.AddComponent<LayoutElement>();
        sliderLayout.flexibleWidth = 1;
        sliderLayout.preferredHeight = 40;

        valueText = AddText(row, "VolumeValue", "80%", 34);
        valueText.alignment = TextAlignmentOptions.Right;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 110;

        testButton = AddButton(row.transform, "TestSoundButton", "Test", 180, 70);
    }

    /// <summary>
    /// Adds a standard Unity Slider: the same object GameObject > UI > Slider makes, built by
    /// the same code (<see cref="DefaultControls"/>), with Unity's built-in sprites. A Slider is
    /// a Background bar, a Fill that grows from the left to the value, and a Handle to drag.
    /// </summary>
    private static Slider AddSlider(Transform parent, string name)
    {
        var resources = new DefaultControls.Resources
        {
            standard = UISprite(),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
        };
        GameObject go = DefaultControls.CreateSlider(resources);
        go.name = name;
        go.transform.SetParent(parent, false);
        foreach (Transform part in go.GetComponentsInChildren<Transform>(true))
        {
            part.gameObject.layer = LayerMask.NameToLayer("UI");
        }

        // Match the rest of the menu: a dark track, a gold fill, a white handle.
        var slider = go.GetComponent<Slider>();
        go.transform.Find("Background").GetComponent<Image>().color = ButtonColor;
        slider.fillRect.GetComponent<Image>().color = HighlightColor;
        return slider;
    }

    /// <summary>
    /// Adds a section heading and, below it, an empty list the options get created in. The list
    /// uses a Grid Layout Group: fixed-size cells that wrap onto a new row when one fills up.
    /// </summary>
    private static RectTransform AddSection(GameObject window, string name, string heading, Vector2 cellSize)
    {
        TMP_Text header = AddText(window, name + "Header", heading, 34);
        header.alignment = TextAlignmentOptions.Left;
        PreferredHeight(header.gameObject, 48);

        GameObject list = NewUI(name + "List", window.transform);
        var grid = list.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = new Vector2(16, 16);
        grid.childAlignment = TextAnchor.UpperLeft;
        return Rect(list);
    }
}
