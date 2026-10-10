using System.Collections.Generic;
using System.IO;
using Match3;
using Match3.Accounts;
using Match3.Achievements;
using Match3.Online;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds everything achievements need in the scene (issue #71):
///   - the Achievements button in the side panel (a row above Account) and its window, a
///     scrolling list of rows made from a prefab this also builds;
///   - the "Achievement unlocked" toast;
///   - the AchievementsClient (talks to the server) and the RoundStatsTracker (counts what
///     happens in a round, for the leaderboard client to send with the score);
///   - the AchievementIcons asset, from the sprites in Assets/Sprites/Achievements.
/// Run it from the menu bar: Match3 > Build Achievements Menu, then save the scene.
///
/// It needs the Account and Leaderboard menus to exist already. Like the other builders,
/// running it again replaces what it built before.
/// </summary>
public static class AchievementsMenuBuilder
{
    private const string IconFolder = "Assets/Sprites/Achievements";
    private const string DataFolder = "Assets/Data/Achievements";
    private const string IconsPath = DataFolder + "/AchievementIcons.asset";
    private const string PrefabFolder = "Assets/Prefabs/UI";
    private const string RowPrefabPath = PrefabFolder + "/AchievementRow.prefab";
    // The icon used for a name the game has no picture for.
    private const string FallbackIcon = "trophy";

    private static readonly Color SecondaryTextColor = new Color(0.7f, 0.72f, 0.8f);
    private static readonly Color ListColor = new Color(0f, 0f, 0f, 0.25f);
    private static readonly Color BarColor = new Color(0f, 0f, 0f, 0.45f);

    [MenuItem("Match3/Build Achievements Menu")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        var board = Object.FindAnyObjectByType<Board>();
        var audioManager = Object.FindAnyObjectByType<AudioManager>();
        Transform sidePanel = canvas != null ? canvas.transform.Find("ScorePanel") : null;
        if (canvas == null || gameManager == null || sidePanel == null || board == null || audioManager == null)
        {
            Debug.LogError("Build Achievements Menu: open the Game scene first (needs Canvas/ScorePanel, GameManager, Board and AudioManager).");
            return;
        }
        var accountManager = Object.FindAnyObjectByType<AccountManager>();
        var online = Object.FindAnyObjectByType<OnlineLeaderboardClient>();
        if (accountManager == null || online == null)
        {
            Debug.LogError("Build Achievements Menu: run Match3 > Build Account Menu and Match3 > Build Leaderboard Menu first.");
            return;
        }

        AchievementIcons icons = BuildIcons();
        AchievementRow rowPrefab = BuildRowPrefab();

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(canvas.transform, "Achievements");
        DestroyChild(sidePanel, "AchievementsButton");

        Button openButton = BuildOpenButton(sidePanel);

        // Root: always active, so the client's and the toast's coroutines keep running while
        // the window is hidden. Last child of the Canvas, so it draws on top of everything.
        GameObject root = NewUI("Achievements", canvas.transform);
        Stretch(Rect(root), 0);
        root.transform.SetAsLastSibling();

        var client = root.AddComponent<AchievementsClient>();
        SetReference(client, "accountManager", accountManager);
        SetReference(client, "online", online);

        var tracker = root.AddComponent<RoundStatsTracker>();
        SetReference(tracker, "board", board);
        SetReference(online, "roundStats", tracker);

        var menu = root.AddComponent<AchievementsMenu>();
        SetReference(menu, "client", client);
        SetReference(menu, "icons", icons);
        SetReference(menu, "openButton", openButton);
        SetReference(menu, "rowPrefab", rowPrefab);
        BuildWindow(root, menu);
        SetReference(gameManager, "achievementsMenu", menu);

        // After the window, so the toast draws over it.
        var toast = root.AddComponent<AchievementToast>();
        SetReference(toast, "client", client);
        SetReference(toast, "audioManager", audioManager);
        SetReference(toast, "icons", icons);
        BuildToast(root, toast, icons);

        Undo.RegisterCreatedObjectUndo(root, "Build Achievements Menu");
        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = root;
        Debug.Log("Achievements menu built. Save the scene (Ctrl+S) to keep it.");
    }

    // ---- Assets ---------------------------------------------------------------------------

    /// <summary>
    /// Creates or updates the AchievementIcons asset: one entry per sprite in the icon folder,
    /// named after its file ("score.png" is the icon "score").
    /// </summary>
    private static AchievementIcons BuildIcons()
    {
        if (!AssetDatabase.IsValidFolder(DataFolder))
        {
            AssetDatabase.CreateFolder("Assets/Data", "Achievements");
        }
        var icons = AssetDatabase.LoadAssetAtPath<AchievementIcons>(IconsPath);
        if (icons == null)
        {
            // A ScriptableObject only becomes a file in the project once CreateAsset saves it.
            icons = ScriptableObject.CreateInstance<AchievementIcons>();
            AssetDatabase.CreateAsset(icons, IconsPath);
        }

        var names = new List<string>();
        var sprites = new List<Sprite>();
        Sprite fallback = null;
        if (AssetDatabase.IsValidFolder(IconFolder))
        {
            // FindAssets answers with GUIDs (an asset's permanent id); turn each into a path.
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Sprite sprite = LoadAsSprite(path);
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (sprite == null || names.Contains(name))
                {
                    continue;
                }
                names.Add(name);
                sprites.Add(sprite);
                if (name == FallbackIcon)
                {
                    fallback = sprite;
                }
            }
        }
        if (sprites.Count == 0)
        {
            Debug.LogWarning($"Build Achievements Menu: no icons in {IconFolder}. Run Art/Icons/build_achievement_icons.py; achievements will show without pictures until then.");
        }

        // "entries" is an array of structs, so each element has its own "name" and "sprite"
        // properties, reached with FindPropertyRelative.
        var serialized = new SerializedObject(icons);
        SerializedProperty entries = serialized.FindProperty("entries");
        entries.arraySize = sprites.Count;
        for (int i = 0; i < sprites.Count; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("name").stringValue = names[i];
            entry.FindPropertyRelative("sprite").objectReferenceValue = sprites[i];
        }
        serialized.FindProperty("fallback").objectReferenceValue = fallback;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        return icons;
    }

    /// <summary>
    /// The sprite in the image at <paramref name="path"/>, first making sure Unity imports the
    /// image as one sprite (a new PNG might be imported as a plain texture).
    /// </summary>
    private static Sprite LoadAsSprite(string path)
    {
        // Every asset has an importer holding its import settings (what the Inspector shows
        // when you select the file). Those settings are what's saved in the .meta file.
        if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
            (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>
    /// The prefab for one line of the list. Everything in it is placed with anchors, so it
    /// stretches to whatever width the list gives it; its height is fixed by a Layout Element.
    /// </summary>
    private static AchievementRow BuildRowPrefab()
    {
        const float textLeft = 100;
        const float textRight = 20;
        const float progressWidth = 220;

        GameObject root = NewUI("AchievementRow", null);
        Size(root, 700, 104);
        PreferredHeight(root, 104);
        AddImage(root, ButtonColor, UISprite());
        var group = root.AddComponent<CanvasGroup>();
        var row = root.AddComponent<AchievementRow>();

        GameObject iconObject = NewUI("Icon", root.transform);
        Anchor(Rect(iconObject), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(64, 64));
        Image icon = AddImage(iconObject, Color.white, null);
        icon.preserveAspect = true;

        // Anchored along the top edge (min.y = max.y = 1), stretching from the left to the
        // right. For a stretched axis, "size" is how much smaller than the parent to be.
        TMP_Text nameText = AddText(root, "Name", "Achievement", 30);
        nameText.fontStyle = FontStyles.Bold;
        LeftAligned(nameText);
        Anchor(nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(textLeft, -10), new Vector2(-(textLeft + textRight + progressWidth), 38));

        TMP_Text progress = AddText(root, "Progress", "0/1", 26);
        progress.alignment = TextAlignmentOptions.Right;
        progress.textWrappingMode = TextWrappingModes.NoWrap;
        Anchor(progress.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-textRight, -12), new Vector2(progressWidth, 34));

        TMP_Text description = AddText(root, "Description", "How to earn it", 22);
        description.color = SecondaryTextColor;
        LeftAligned(description);
        Anchor(description.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(textLeft, -50), new Vector2(-(textLeft + textRight), 28));

        // The progress bar: a dark track along the bottom, and a gold fill inside it. The
        // fill's right-hand anchor is what AchievementRow moves to show progress.
        GameObject bar = NewUI("ProgressBar", root.transform);
        Anchor(Rect(bar), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),
            new Vector2(textLeft, 12), new Vector2(-(textLeft + textRight), 8));
        AddImage(bar, BarColor, null);
        GameObject fill = NewUI("Fill", bar.transform);
        Stretch(Rect(fill), 0);
        AddImage(fill, HighlightColor, null);

        SetReference(row, "icon", icon);
        SetReference(row, "nameText", nameText);
        SetReference(row, "descriptionText", description);
        SetReference(row, "progressText", progress);
        SetReference(row, "progressFill", Rect(fill));
        SetReference(row, "group", group);

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }
        // SaveAsPrefabAsset writes the object out as a .prefab file (replacing any old one at
        // that path, which keeps its GUID so existing references survive). The temporary
        // scene copy is then no longer needed.
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RowPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<AchievementRow>();
    }

    // One line, cut short with "..." if it doesn't fit, and shown as typed (the text comes
    // from the server, so it isn't read as rich-text formatting).
    private static void LeftAligned(TMP_Text text)
    {
        text.alignment = TextAlignmentOptions.Left;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.richText = false;
    }

    // ---- Scene ----------------------------------------------------------------------------

    /// <summary>
    /// The "Achievements" button: bottom right of the side panel, in a row of its own above the
    /// Account button, and the same size.
    /// </summary>
    private static Button BuildOpenButton(Transform sidePanel)
    {
        GameObject go = NewUI("AchievementsButton", sidePanel);
        // Account is 80 tall, 120 up from the bottom; leave the same 16 gap above it.
        Anchor(Rect(go), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 216), new Vector2(376, 80));
        Image background = AddImage(go, ButtonColor, UISprite());
        background.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = background;

        TMP_Text label = AddText(go, "Label", "Achievements", 34);
        Stretch(label.rectTransform, 12);

        Undo.RegisterCreatedObjectUndo(go, "Build Achievements Menu");
        return button;
    }

    private static void BuildWindow(GameObject root, AchievementsMenu menu)
    {
        GameObject modal = AddModal(root, "Panel", out GameObject window);
        // Wider than the other windows: each row has a name, a description and a progress count.
        Rect(window).sizeDelta = new Vector2(860, 0);

        TMP_Text title = AddText(window, "Title", "Achievements", 56);
        title.fontStyle = FontStyles.Bold;
        PreferredHeight(title.gameObject, 76);

        TMP_Text status = AddText(window, "Status", "", 30);
        status.color = HighlightColor;
        PreferredHeight(status.gameObject, 44);

        // A Scroll Rect is three objects: the scroll view itself, a "viewport" that hides
        // whatever is outside it, and the "content" that's taller than the viewport and slides
        // up and down inside it.
        GameObject list = NewUI("List", window.transform);
        PreferredHeight(list, 560);
        // The scroll wheel only reaches a Scroll Rect through something under the pointer that
        // catches clicks, and the rows don't, so the list's own background does.
        AddImage(list, ListColor, UISprite()).raycastTarget = true;
        var scroll = list.AddComponent<ScrollRect>();

        GameObject viewport = NewUI("Viewport", list.transform);
        Stretch(Rect(viewport), 8);
        // Hides the parts of its children outside its rectangle.
        viewport.AddComponent<RectMask2D>();

        GameObject content = NewUI("Content", viewport.transform);
        // Pinned to the top of the viewport and as wide as it; the Content Size Fitter makes it
        // as tall as its rows need.
        Anchor(Rect(content), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = Rect(viewport);
        scroll.content = Rect(content);
        scroll.horizontal = false;
        scroll.vertical = true;
        // Clamped: stops dead at the ends instead of the default rubber-band bounce.
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40;

        GameObject closeRow = AddCenteredRow(window, "CloseRow", 90, 0);
        Button closeButton = AddButton(closeRow.transform, "CloseButton", "Close", 300, 80);

        SetReference(menu, "panel", modal);
        SetReference(menu, "statusText", status);
        SetReference(menu, "listContent", Rect(content));
        SetReference(menu, "scroll", scroll);
        SetReference(menu, "closeButton", closeButton);

        // Hidden until opened.
        modal.SetActive(false);
    }

    /// <summary>
    /// The toast's card: pinned to the top of the screen, centered over the board (the side
    /// panel takes up the right-hand edge), with its top edge as its pivot so the script can
    /// slide it in from above.
    /// </summary>
    private static void BuildToast(GameObject root, AchievementToast toast, AchievementIcons icons)
    {
        const float textLeft = 108;
        const float textRight = 20;
        float sidePanelWidth = ((RectTransform)root.transform.parent.Find("ScorePanel")).rect.width;

        GameObject card = NewUI("Toast", root.transform);
        Anchor(Rect(card), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
            new Vector2(-sidePanelWidth / 2f, -24), new Vector2(600, 116));
        // A gold card with the window color inset on top of it, so the gold shows as a border.
        AddImage(card, HighlightColor, UISprite());
        GameObject inner = NewUI("Background", card.transform);
        Stretch(Rect(inner), 3);
        AddImage(inner, WindowColor, UISprite());

        GameObject iconObject = NewUI("Icon", card.transform);
        Anchor(Rect(iconObject), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(68, 68));
        Image icon = AddImage(iconObject, HighlightColor, icons.Get(FallbackIcon));
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;

        TMP_Text heading = AddText(card, "Heading", "Achievement unlocked", 20);
        heading.color = HighlightColor;
        LeftAligned(heading);
        Anchor(heading.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(textLeft, -12), new Vector2(-(textLeft + textRight), 26));

        TMP_Text nameText = AddText(card, "Name", "Achievement", 32);
        nameText.fontStyle = FontStyles.Bold;
        LeftAligned(nameText);
        Anchor(nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(textLeft, -38), new Vector2(-(textLeft + textRight), 40));

        TMP_Text description = AddText(card, "Description", "How it was earned", 20);
        description.color = SecondaryTextColor;
        LeftAligned(description);
        Anchor(description.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(textLeft, -78), new Vector2(-(textLeft + textRight), 26));

        SetReference(toast, "card", Rect(card));
        SetReference(toast, "icon", icon);
        SetReference(toast, "nameText", nameText);
        SetReference(toast, "descriptionText", description);

        // Hidden until something unlocks.
        card.SetActive(false);
    }
}
