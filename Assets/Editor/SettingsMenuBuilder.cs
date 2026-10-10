using System.Linq;
using Match3;
using Match3.Accounts;
using Match3.Online;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds the Settings menu UI (issues #17, #49 and #53): the three option
/// prefabs, the gear button in the side panel, and the modal Settings panel (including the
/// sound set picker, volume slider and screen shake toggle), with every Inspector reference wired up. Run it from the menu bar: Match3 > Build Settings Menu, then save the
/// scene. It also adds the PreferencesSync (issue #70) that copies the settings to the player's
/// account, if the scene has an AccountManager (Match3 > Build Account Menu makes one).
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
    private const string SoundSetOptionPath = PrefabFolder + "/SoundSetOption.prefab";
    private const string GearSpritePath = "Assets/Sprites/UI/Gear.png";
    private const string PlaySpritePath = "Assets/Sprites/UI/Play.png";

    private static readonly Color OptionColor = new Color(0.22f, 0.24f, 0.32f);

    // The size of one option in each section's grid. The window's content is 820 wide (900
    // minus 40 of padding each side) and the grids leave 16 between cells, so 262 fits three to
    // a row and 190 fits four. Keeping every section on one row matters: the window grows with
    // its content, and a second row anywhere would push it past the bottom of a 1080-high screen.
    private static readonly Vector2 TokenStyleOptionSize = new Vector2(262, 130);
    private static readonly Vector2 BoardThemeOptionSize = new Vector2(190, 200);
    private static readonly Vector2 SoundSetOptionSize = new Vector2(262, 72);

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

        Sprite gearSprite = ImportUISprite(GearSpritePath);
        Sprite playSprite = ImportUISprite(PlaySpritePath);
        TokenStyleOption tokenStyleOptionPrefab = BuildTokenStyleOptionPrefab();
        BoardThemeOption boardThemeOptionPrefab = BuildBoardThemeOptionPrefab();
        SoundSetOption soundSetOptionPrefab = BuildSoundSetOptionPrefab(playSprite);

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(canvas.transform, "Settings");
        DestroyChild(sidePanel, "SettingsButton");

        Button gearButton = BuildGearButton(sidePanel, gearSprite);
        BuildSettingsPanel(canvas.transform, themeManager, board, audioManager, gearButton,
            tokenStyleOptionPrefab, boardThemeOptionPrefab, soundSetOptionPrefab, out SettingsMenu settingsMenu);

        SetReference(gameManager, "settingsMenu", settingsMenu);
        FillSoundSetsIfEmpty(audioManager);
        AddPreferencesSync(settingsMenu, themeManager, audioManager);

        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = settingsMenu.gameObject;
        Debug.Log("Settings menu built. Save the scene (Ctrl+S) to keep it.");
    }

    /// <summary>
    /// The Sounds section shows one option per entry in the AudioManager's Sound Sets list, so
    /// an empty list means an empty section. If nobody has filled the list in yet, this fills
    /// it with every SoundSet asset in the project: the set already in use first, then the rest
    /// by name. A list that has entries is left alone, so a hand-picked order survives a rebuild.
    /// </summary>
    private static void FillSoundSetsIfEmpty(AudioManager audioManager)
    {
        var serialized = new SerializedObject(audioManager);
        if (serialized.FindProperty("soundSets").arraySize > 0)
        {
            return;
        }

        // FindAssets searches the project ("t:SoundSet" = every asset of that type) and returns
        // GUIDs, which are turned back into paths and then loaded.
        SoundSet current = audioManager.SoundSet;
        SoundSet[] sets = AssetDatabase.FindAssets("t:" + nameof(SoundSet))
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<SoundSet>)
            .Where(set => set != null)
            // false sorts before true, so the current set (where this is false) comes first.
            .OrderBy(set => set != current)
            .ThenBy(set => set.DisplayName)
            .ToArray();
        SetArray(audioManager, "soundSets", sets);
        Debug.Log($"Build Settings Menu: the AudioManager's Sound Sets list was empty, so it now has all {sets.Length} sound set(s) in the project.", audioManager);
    }

    /// <summary>
    /// Puts the PreferencesSync next to the SettingsMenu. That object is always active, so the
    /// sync's coroutines keep running while the panel is hidden.
    /// </summary>
    private static void AddPreferencesSync(SettingsMenu settingsMenu, ThemeManager themeManager, AudioManager audioManager)
    {
        var accountManager = Object.FindAnyObjectByType<AccountManager>();
        if (accountManager == null)
        {
            Debug.LogWarning("Build Settings Menu: the scene has no AccountManager, so settings won't sync to accounts. Run Match3 > Build Account Menu, then this again.");
            return;
        }
        var sync = settingsMenu.gameObject.AddComponent<PreferencesSync>();
        SetReference(sync, "accountManager", accountManager);
        SetReference(sync, "themeManager", themeManager);
        SetReference(sync, "audioManager", audioManager);
        SetReference(sync, "settingsMenu", settingsMenu);
    }

    // ---- Assets ---------------------------------------------------------------------------

    /// <summary>Makes sure the PNG at <paramref name="path"/> is imported as a UI sprite, and returns it.</summary>
    private static Sprite ImportUISprite(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static TokenStyleOption BuildTokenStyleOptionPrefab()
    {
        GameObject root = NewUI("TokenStyleOption", null);
        Size(root, TokenStyleOptionSize.x, TokenStyleOptionSize.y);
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
        // Six 32-pixel icons with 10 between them are 242 wide, which exactly fits the 242 the
        // row has (the option's width minus 10 of margin each side).
        var serialized = new SerializedObject(option);
        serialized.FindProperty("iconSize").floatValue = 32;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root, TokenStyleOptionPath).GetComponent<TokenStyleOption>();
    }

    /// <summary>
    /// A sound set option is a short bar: the set's name on the left and a small play button
    /// on the right for hearing the set without choosing it.
    /// </summary>
    private static SoundSetOption BuildSoundSetOptionPrefab(Sprite playSprite)
    {
        GameObject root = NewUI("SoundSetOption", null);
        Size(root, SoundSetOptionSize.x, SoundSetOptionSize.y);
        var option = root.AddComponent<SoundSetOption>();
        AddOptionFrame(root, out GameObject highlight);

        // Stretched across the option, starting 24 in from the left and stopping 76 short of
        // the right edge so a long name can't run under the play button.
        TMP_Text label = AddText(root, "Label", "Sounds", 28);
        label.alignment = TextAlignmentOptions.Left;
        Anchor(label.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(-100, 0));

        // A Button inside the option's own Button. It's created after (so drawn on top of) the
        // option's background, which means a click here lands on this button and not the option.
        GameObject preview = NewUI("PreviewButton", root.transform);
        Anchor(Rect(preview), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(48, 48));
        Image previewBackground = AddImage(preview, ButtonColor, UISprite());
        previewBackground.raycastTarget = true;
        var previewButton = preview.AddComponent<Button>();
        previewButton.targetGraphic = previewBackground;

        GameObject icon = NewUI("Icon", preview.transform);
        Stretch(Rect(icon), 12);
        AddImage(icon, Color.white, playSprite).preserveAspect = true;

        WireOption(option, label, highlight);
        SetReference(option, "previewButton", previewButton);
        return SavePrefab(root, SoundSetOptionPath).GetComponent<SoundSetOption>();
    }

    private static BoardThemeOption BuildBoardThemeOptionPrefab()
    {
        const int columns = 3;
        const float cellSize = 32;
        const float swatchPadding = 12;
        float swatchSize = columns * cellSize + 2 * swatchPadding;

        GameObject root = NewUI("BoardThemeOption", null);
        Size(root, BoardThemeOptionSize.x, BoardThemeOptionSize.y);
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
        TokenStyleOption tokenStyleOptionPrefab, BoardThemeOption boardThemeOptionPrefab,
        SoundSetOption soundSetOptionPrefab, out SettingsMenu menu)
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

        RectTransform tokenStyleList = AddSection(window, "TokenStyle", "Token Style", TokenStyleOptionSize);
        RectTransform boardThemeList = AddSection(window, "BoardColors", "Board Colors", BoardThemeOptionSize);
        // The sound section's heading is called "SoundHeader" and its list "SoundSetList".
        RectTransform soundSetList = AddSection(window, "Sound", "Sounds", SoundSetOptionSize, "SoundSetList");
        AddVolumeRow(window, out Slider volumeSlider, out TMP_Text volumeValueText, out Button testSoundButton);
        Toggle screenShakeToggle = AddToggleRow(window, "ScreenShake", "Screen shake when gems blast");

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
        SetReference(menu, "soundSetList", soundSetList);
        SetReference(menu, "soundSetOptionPrefab", soundSetOptionPrefab);
        SetReference(menu, "volumeSlider", volumeSlider);
        SetReference(menu, "volumeValueText", volumeValueText);
        SetReference(menu, "testSoundButton", testSoundButton);
        SetReference(menu, "screenShakeToggle", screenShakeToggle);

        // Hidden until the gear is clicked.
        modal.SetActive(false);
        Undo.RegisterCreatedObjectUndo(root, "Build Settings Menu");
    }

    /// <summary>
    /// Adds the last row of the "Sounds" section, below the sound set options: a "Volume"
    /// label, the volume slider, its value as a percentage, and a Test button that plays a
    /// sample sound.
    /// </summary>
    private static void AddVolumeRow(GameObject window, out Slider slider, out TMP_Text valueText, out Button testButton)
    {
        GameObject row = NewUI("SoundRow", window.transform);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 24;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        PreferredHeight(row, 80);

        TMP_Text label = AddText(row, "VolumeLabel", "Volume", 34);
        label.alignment = TextAlignmentOptions.Left;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 130;

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
    /// Adds a row with a checkbox and a label after it, e.g. "[x] Screen shake when gems blast".
    /// </summary>
    private static Toggle AddToggleRow(GameObject window, string name, string label)
    {
        // DefaultControls.CreateToggle builds what GameObject > UI > Toggle makes: a Toggle with
        // a Background box, a Checkmark inside it, and a Label.
        var resources = new DefaultControls.Resources
        {
            standard = UISprite(),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
        };
        GameObject go = DefaultControls.CreateToggle(resources);
        go.name = name + "Toggle";
        go.transform.SetParent(window.transform, false);
        foreach (Transform part in go.GetComponentsInChildren<Transform>(true))
        {
            part.gameObject.layer = LayerMask.NameToLayer("UI");
        }
        PreferredHeight(go, 60);

        // The default toggle is tiny (20 pixels) with legacy text. Make the box match the menu's
        // scale, and swap the label for TextMeshPro like every other label here.
        var background = (RectTransform)go.transform.Find("Background");
        background.anchorMin = new Vector2(0f, 0.5f);
        background.anchorMax = new Vector2(0f, 0.5f);
        background.pivot = new Vector2(0f, 0.5f);
        background.anchoredPosition = Vector2.zero;
        background.sizeDelta = new Vector2(48, 48);
        background.GetComponent<Image>().color = ButtonColor;
        var checkmark = (RectTransform)background.Find("Checkmark");
        Stretch(checkmark, 6);
        checkmark.GetComponent<Image>().color = HighlightColor;

        Object.DestroyImmediate(go.transform.Find("Label").gameObject);
        TMP_Text text = AddText(go, "Label", label, 34);
        text.alignment = TextAlignmentOptions.Left;
        // AddText turns raycasts off; on here, so a click on the words reaches the Toggle (its
        // parent) and ticks the box too.
        text.raycastTarget = true;
        RectTransform textRect = Rect(text.gameObject);
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(68, 0);
        textRect.offsetMax = Vector2.zero;

        var toggle = go.GetComponent<Toggle>();
        toggle.isOn = true;
        return toggle;
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
    /// The list is named <paramref name="name"/> + "List" unless <paramref name="listName"/> says otherwise.
    /// </summary>
    private static RectTransform AddSection(GameObject window, string name, string heading, Vector2 cellSize, string listName = null)
    {
        TMP_Text header = AddText(window, name + "Header", heading, 34);
        header.alignment = TextAlignmentOptions.Left;
        PreferredHeight(header.gameObject, 48);

        GameObject list = NewUI(listName ?? name + "List", window.transform);
        var grid = list.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = new Vector2(16, 16);
        grid.childAlignment = TextAnchor.UpperLeft;
        return Rect(list);
    }
}
