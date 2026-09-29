using Match3;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor-only helper that builds the Settings menu UI (issue #17): the two option prefabs,
/// the gear button in the side panel, and the modal Settings panel, with every Inspector
/// reference wired up. Run it from the menu bar: Match3 > Build Settings Menu, then save the
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

    private static readonly Color WindowColor = new Color(0.11f, 0.12f, 0.17f, 0.98f);
    private static readonly Color OptionColor = new Color(0.22f, 0.24f, 0.32f);
    private static readonly Color HighlightColor = new Color(1f, 0.8f, 0.25f);
    private static readonly Color ButtonColor = new Color(0.15f, 0.16f, 0.22f);

    // [MenuItem] adds this method to Unity's menu bar.
    [MenuItem("Match3/Build Settings Menu")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        var themeManager = Object.FindAnyObjectByType<ThemeManager>();
        var board = Object.FindAnyObjectByType<Board>();
        Transform sidePanel = canvas != null ? canvas.transform.Find("ScorePanel") : null;
        if (canvas == null || gameManager == null || themeManager == null || board == null || sidePanel == null)
        {
            Debug.LogError("Build Settings Menu: open the Game scene first (needs Canvas/ScorePanel, GameManager, ThemeManager and Board).");
            return;
        }

        Sprite gearSprite = ImportGearSprite();
        TokenStyleOption tokenStyleOptionPrefab = BuildTokenStyleOptionPrefab();
        BoardThemeOption boardThemeOptionPrefab = BuildBoardThemeOptionPrefab();

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(canvas.transform, "Settings");
        DestroyChild(sidePanel, "SettingsButton");

        Button gearButton = BuildGearButton(sidePanel, gearSprite);
        BuildSettingsPanel(canvas.transform, themeManager, board, gearButton,
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

    private static void BuildSettingsPanel(Transform canvas, ThemeManager themeManager, Board board, Button gearButton,
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

        // Close button, centered in its own row.
        GameObject closeRow = NewUI("CloseRow", window.transform);
        var rowLayout = closeRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        PreferredHeight(closeRow, 90);

        GameObject close = NewUI("CloseButton", closeRow.transform);
        var closeLayout = close.AddComponent<LayoutElement>();
        closeLayout.preferredWidth = 300;
        closeLayout.preferredHeight = 80;
        Image closeImage = AddImage(close, Color.white, UISprite());
        closeImage.raycastTarget = true;
        var closeButton = close.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        TMP_Text closeLabel = AddText(close, "Label", "Close", 36);
        closeLabel.color = new Color(0.2f, 0.2f, 0.2f);
        Stretch(closeLabel.rectTransform, 0);

        SetReference(menu, "themeManager", themeManager);
        SetReference(menu, "board", board);
        SetReference(menu, "openButton", gearButton);
        SetReference(menu, "modal", modal);
        SetReference(menu, "closeButton", closeButton);
        SetReference(menu, "tokenStyleList", tokenStyleList);
        SetReference(menu, "tokenStyleOptionPrefab", tokenStyleOptionPrefab);
        SetReference(menu, "boardThemeList", boardThemeList);
        SetReference(menu, "boardThemeOptionPrefab", boardThemeOptionPrefab);

        // Hidden until the gear is clicked.
        modal.SetActive(false);
        Undo.RegisterCreatedObjectUndo(root, "Build Settings Menu");
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

    // ---- Small helpers --------------------------------------------------------------------

    /// <summary>Creates a UI GameObject (one with a RectTransform) on the UI layer.</summary>
    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }
        return go;
    }

    private static RectTransform Rect(GameObject go) => (RectTransform)go.transform;

    private static void Size(GameObject go, float width, float height) => Rect(go).sizeDelta = new Vector2(width, height);

    private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    /// <summary>Stretches to fill the parent, leaving <paramref name="inset"/> pixels on every side.</summary>
    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private static void PreferredHeight(GameObject go, float height)
    {
        go.AddComponent<LayoutElement>().preferredHeight = height;
    }

    /// <summary>Unity's built-in rounded-rectangle UI sprite (the one default Buttons use).</summary>
    private static Sprite UISprite() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    private static Image AddImage(GameObject go, Color color, Sprite sprite)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        // Off by default; only backdrops and buttons turn it back on.
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text AddText(GameObject parent, string name, string text, float fontSize)
    {
        GameObject go = NewUI(name, parent.transform);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void DestroyChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    // [SerializeField] private fields can't be set from another class directly, but a
    // SerializedObject edits the saved data the Inspector shows, private or not.
    private static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"Build Settings Menu: {target.GetType().Name} has no field '{field}'.", target);
            return;
        }
        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    private static void SetArray(Object target, string field, Object[] values)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        serialized.ApplyModifiedProperties();
    }
}
