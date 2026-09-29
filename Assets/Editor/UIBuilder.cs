using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small helpers shared by the Editor tools that build UI from code
/// (<see cref="SettingsMenuBuilder"/>, <see cref="LeaderboardMenuBuilder"/>): creating UI
/// objects, positioning them, and setting private [SerializeField] references.
///
/// The builders say "using static UIBuilder;", which lets them call these as if they were
/// their own methods (NewUI(...) rather than UIBuilder.NewUI(...)).
/// </summary>
public static class UIBuilder
{
    public static readonly Color WindowColor = new Color(0.11f, 0.12f, 0.17f, 0.98f);
    public static readonly Color HighlightColor = new Color(1f, 0.8f, 0.25f);
    public static readonly Color ButtonColor = new Color(0.15f, 0.16f, 0.22f);
    public static readonly Color DarkTextColor = new Color(0.2f, 0.2f, 0.2f);

    /// <summary>Creates a UI GameObject (one with a RectTransform) on the UI layer.</summary>
    public static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }
        return go;
    }

    public static RectTransform Rect(GameObject go) => (RectTransform)go.transform;

    public static void Size(GameObject go, float width, float height) => Rect(go).sizeDelta = new Vector2(width, height);

    public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    /// <summary>Stretches to fill the parent, leaving <paramref name="inset"/> pixels on every side.</summary>
    public static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    public static void PreferredHeight(GameObject go, float height)
    {
        go.AddComponent<LayoutElement>().preferredHeight = height;
    }

    /// <summary>Unity's built-in rounded-rectangle UI sprite (the one default Buttons use).</summary>
    public static Sprite UISprite() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    public static Image AddImage(GameObject go, Color color, Sprite sprite)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        // Off by default; only backdrops and buttons turn it back on.
        image.raycastTarget = false;
        return image;
    }

    public static TMP_Text AddText(GameObject parent, string name, string text, float fontSize)
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

    /// <summary>
    /// Adds a white rounded button with a dark centered label, sized by a Layout Element (so
    /// it's meant to go inside a layout group).
    /// </summary>
    public static Button AddButton(Transform parent, string name, string label, float width, float height)
    {
        GameObject go = NewUI(name, parent);
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;
        Image image = AddImage(go, Color.white, UISprite());
        image.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text text = AddText(go, "Label", label, 36);
        text.color = DarkTextColor;
        Stretch(text.rectTransform, 0);
        return button;
    }

    /// <summary>
    /// Adds a row that lays its children out side by side, centered, at their preferred sizes
    /// (e.g. a row of buttons).
    /// </summary>
    public static GameObject AddCenteredRow(GameObject parent, string name, float height, float spacing)
    {
        GameObject row = NewUI(name, parent.transform);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        PreferredHeight(row, height);
        return row;
    }

    public static void DestroyChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    // [SerializeField] private fields can't be set from another class directly, but a
    // SerializedObject edits the saved data the Inspector shows, private or not.
    public static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"UI Builder: {target.GetType().Name} has no field '{field}'.", target);
            return;
        }
        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    public static void SetArray(Object target, string field, Object[] values)
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
