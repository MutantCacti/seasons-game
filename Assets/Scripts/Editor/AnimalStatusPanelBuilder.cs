using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;


// Tools > UI > Build Animal Status Panel. Builds the status panel's hierarchy in the open scene and
// wires its fields, so nobody has to lay it out by hand. Only what is missing is created, found by
// name, so running it again after restyling adds new parts without touching the old ones.
//
// It goes under the screen space overlay canvas that holds the selection, or else the first one in
// the scene. The art and fonts are the compendium's; swap anything afterwards.
public static class AnimalStatusPanelBuilder
{
    private const string ROOT_NAME = "Animal Status Panel";
    private const string PANEL_NAME = "Panel";

    private const string PANEL_SPRITE = "Assets/Art/UI/outline.png";
    private const string CLOSE_SPRITE = "Assets/Art/UI/close-icon.png";
    private const string FONT = "Assets/TextMesh Pro/Resources/Fonts & Materials/Nunito SDF.asset";
    private const string TIP_FONT = "Assets/TextMesh Pro/Resources/Fonts & Materials/Nunito-Italic SDF.asset";

    private const float PANEL_WIDTH = 320.0f;

    // the compendium's paper and inks
    private static readonly Color PAPER_COLOUR = new Color(236.0f / 255.0f, 228.0f / 255.0f, 205.0f / 255.0f, 1.0f);    // #ECE4CD
    private static readonly Color FIELD_COLOUR = new Color(221.0f / 255.0f, 212.0f / 255.0f, 189.0f / 255.0f, 1.0f);    // #DDD4BD, the paper a shade darker
    private static readonly Color TEXT_COLOUR = new Color(50.0f / 255.0f, 50.0f / 255.0f, 50.0f / 255.0f, 1.0f);        // #323232
    private static readonly Color FADED_COLOUR = new Color(138.0f / 255.0f, 129.0f / 255.0f, 116.0f / 255.0f, 1.0f);    // #8A8174


    [MenuItem("Tools/UI/Build Animal Status Panel")]
    private static void Build()
    {
        Canvas canvas = FindCanvas();

        if (canvas == null)
        {
            Debug.LogError("AnimalStatusPanelBuilder: no screen space overlay canvas in the open scene, so there is nowhere to build the panel");
            return;
        }

        RectTransform root = FindOrCreate(canvas.transform, ROOT_NAME);
        Stretch(root);

        AnimalStatusPanel component = root.GetComponent<AnimalStatusPanel>();
        if (component == null) component = Undo.AddComponent<AnimalStatusPanel>(root.gameObject);

        RectTransform panel = BuildPanel(root);
        RectTransform header = BuildHeader(panel);

        TMP_InputField nameField = BuildNameField(header);
        Button star = BuildIconButton(header, "Star", null, 32.0f);
        Button close = BuildIconButton(header, "Close", LoadSprite(CLOSE_SPRITE), 24.0f);

        HappinessMeter meter = BuildMeter(panel);
        RectTransform tips = BuildTips(panel, out TMP_Text tipTemplate);

        SerializedObject serialized = new SerializedObject(component);
        SetIfEmpty(serialized, "panel", panel);
        SetIfEmpty(serialized, "nameField", nameField);
        SetIfEmpty(serialized, "starButton", star);
        SetIfEmpty(serialized, "starImage", star.GetComponent<Image>());
        SetIfEmpty(serialized, "closeButton", close);
        SetIfEmpty(serialized, "happinessMeter", meter);
        SetIfEmpty(serialized, "tipsContainer", tips);
        SetIfEmpty(serialized, "tipTemplate", tipTemplate);
        serialized.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Selection.activeGameObject = root.gameObject;

        Debug.Log($"AnimalStatusPanelBuilder: built '{ROOT_NAME}' under '{canvas.name}'. Assign the star sprites and the avoid rect (the inventory bar) on it", component);
    }


    // MARK: parts


    // pivoted on its left middle, so the panel's position is the point beside the animal
    private static RectTransform BuildPanel(RectTransform root)
    {
        RectTransform panel = FindOrCreate(root, PANEL_NAME, out bool created);
        if (!created) return panel;

        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.0f, 0.5f);
        panel.sizeDelta = new Vector2(PANEL_WIDTH, 80.0f);

        Image background = Undo.AddComponent<Image>(panel.gameObject);
        background.sprite = LoadSprite(PANEL_SPRITE);
        background.type = Image.Type.Sliced;
        background.color = PAPER_COLOUR;

        VerticalLayoutGroup layout = Undo.AddComponent<VerticalLayoutGroup>(panel.gameObject);
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 8.0f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // a fixed width, and as tall as its contents
        ContentSizeFitter fitter = Undo.AddComponent<ContentSizeFitter>(panel.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return panel;
    }


    private static RectTransform BuildHeader(RectTransform panel)
    {
        RectTransform header = FindOrCreate(panel, "Header", out bool created);
        if (!created) return header;

        HorizontalLayoutGroup layout = Undo.AddComponent<HorizontalLayoutGroup>(header.gameObject);
        layout.spacing = 8.0f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        LayoutElement element = Undo.AddComponent<LayoutElement>(header.gameObject);
        element.minHeight = 36.0f;

        return header;
    }


    private static TMP_InputField BuildNameField(RectTransform header)
    {
        Transform existing = header.Find("Name");
        if (existing != null) return existing.GetComponent<TMP_InputField>();

        GameObject field = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        field.name = "Name";
        Undo.RegisterCreatedObjectUndo(field, "Build Animal Status Panel");
        field.transform.SetParent(header, false);
        field.transform.SetSiblingIndex(0);

        Image background = field.GetComponent<Image>();
        if (background != null) background.color = FIELD_COLOUR;

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT);
        if (font == null) Debug.LogWarning($"AnimalStatusPanelBuilder: no font at '{FONT}', so the name keeps TMP's default font");

        foreach (TMP_Text text in field.GetComponentsInChildren<TMP_Text>(true))
        {
            if (font != null) text.font = font;
            text.fontSize = 22.0f;
            text.color = text.name == "Placeholder" ? FADED_COLOUR : TEXT_COLOUR;
            if (text.name == "Placeholder") text.text = "Name";
        }

        LayoutElement element = field.AddComponent<LayoutElement>();
        element.flexibleWidth = 1.0f;
        element.preferredHeight = 36.0f;

        return field.GetComponent<TMP_InputField>();
    }


    private static Button BuildIconButton(RectTransform header, string name, Sprite sprite, float size)
    {
        RectTransform rect = FindOrCreate(header, name, out bool created);
        if (!created) return rect.GetComponent<Button>();

        Image image = Undo.AddComponent<Image>(rect.gameObject);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = sprite != null ? FADED_COLOUR : Color.white;

        Button button = Undo.AddComponent<Button>(rect.gameObject);
        button.targetGraphic = image;

        LayoutElement element = Undo.AddComponent<LayoutElement>(rect.gameObject);
        element.preferredWidth = size;
        element.preferredHeight = size;

        return button;
    }


    // A bar: a darker paper background, and the fill the meter draws its gradient into, inset a little.
    // Swap the background for the real art and add a frame image on top if it has one
    private static HappinessMeter BuildMeter(RectTransform panel)
    {
        RectTransform meterRect = FindOrCreate(panel, "Happiness", out bool created);
        HappinessMeter meter = meterRect.GetComponent<HappinessMeter>();
        if (!created && meter != null) return meter;

        LayoutElement element = Undo.AddComponent<LayoutElement>(meterRect.gameObject);
        element.preferredHeight = 16.0f;
        element.minHeight = 16.0f;

        RectTransform background = FindOrCreate(meterRect, "Background");
        Stretch(background);

        Image backgroundImage = Undo.AddComponent<Image>(background.gameObject);
        backgroundImage.sprite = LoadSprite(PANEL_SPRITE);
        backgroundImage.type = Image.Type.Sliced;
        backgroundImage.pixelsPerUnitMultiplier = 6.0f;   // the panel art's 48px corners, shrunk to suit a thin bar
        backgroundImage.color = FIELD_COLOUR;
        backgroundImage.raycastTarget = false;

        RectTransform fill = FindOrCreate(meterRect, "Fill");
        Stretch(fill);
        fill.offsetMin = new Vector2(3.0f, 3.0f);
        fill.offsetMax = new Vector2(-3.0f, -3.0f);

        Image fillImage = Undo.AddComponent<Image>(fill.gameObject);
        fillImage.raycastTarget = false;

        meter = Undo.AddComponent<HappinessMeter>(meterRect.gameObject);

        SerializedObject serialized = new SerializedObject(meter);
        serialized.FindProperty("fill").objectReferenceValue = fillImage;
        serialized.ApplyModifiedProperties();

        return meter;
    }


    // a column of one line tips, with an inactive template line the panel clones
    private static RectTransform BuildTips(RectTransform panel, out TMP_Text template)
    {
        RectTransform tips = FindOrCreate(panel, "Tips", out bool created);

        if (created)
        {
            VerticalLayoutGroup layout = Undo.AddComponent<VerticalLayoutGroup>(tips.gameObject);
            layout.spacing = 2.0f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        Transform existing = tips.Find("Tip");

        if (existing != null)
        {
            template = existing.GetComponent<TMP_Text>();
            return tips;
        }

        GameObject line = new GameObject("Tip", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(line, "Build Animal Status Panel");
        line.transform.SetParent(tips, false);

        TextMeshProUGUI text = line.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TIP_FONT);
        if (font != null) text.font = font;
        text.fontSize = 18.0f;
        text.color = FADED_COLOUR;
        text.text = "What is bothering it";
        // a long tip wraps rather than being cut off; each is still one item in the list
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        line.SetActive(false);

        template = text;
        return tips;
    }


    // MARK: helpers


    private static Canvas FindCanvas()
    {
        GameObject selected = Selection.activeGameObject;
        Canvas fromSelection = selected != null ? selected.GetComponentInParent<Canvas>() : null;

        if (fromSelection != null && fromSelection.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return fromSelection.rootCanvas;

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay) return canvas;
        }

        return null;
    }


    private static RectTransform FindOrCreate(Transform parent, string name)
    {
        return FindOrCreate(parent, name, out bool _);
    }


    private static RectTransform FindOrCreate(Transform parent, string name, out bool created)
    {
        Transform existing = parent.Find(name);
        created = existing == null;

        if (!created) return existing as RectTransform;

        GameObject child = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(child, "Build Animal Status Panel");
        child.transform.SetParent(parent, false);

        return child.transform as RectTransform;
    }


    // full screen, so the panel can be placed anywhere on it. No graphic, so it never takes a click
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }


    private static void SetIfEmpty(SerializedObject serialized, string property, Object value)
    {
        SerializedProperty field = serialized.FindProperty(property);

        if (field == null)
        {
            Debug.LogError($"AnimalStatusPanelBuilder: AnimalStatusPanel has no field '{property}'");
            return;
        }

        if (field.objectReferenceValue == null) field.objectReferenceValue = value;
    }


    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"AnimalStatusPanelBuilder: no sprite at '{path}'");
        return sprite;
    }
}
