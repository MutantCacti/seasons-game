using TMPro;
using UnityEngine;
using UnityEngine.UI;


// The focused animal's status panel: its name (renamable) and its star, hovering beside it on the
// screen space canvas. Opens and closes with AnimalSelection's focus.
//
// Screen space rather than a world canvas: the text field stays crisp and takes keyboard focus the
// usual way, the panel is the same size at every zoom, and terrain or the book can never cover it.
// It follows the animal each frame, a fixed gap beside the edge of its sprite as drawn on screen, so
// a bigger animal pushes the panel out by just its extra size. Clamped to the screen and kept off
// the inventory bar; the camera centres a focused animal, so it settles beside the middle.
//
// Lives on an always-active, full-screen, non-raycasting rect; only the panel child is shown and
// hidden, so this keeps running and nothing is bound while its canvas is inactive.
public class AnimalStatusPanel : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] private RectTransform panel;           // the box itself; a child of this rect
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private Button starButton;
    [SerializeField] private Image starImage;
    [SerializeField] private Button closeButton;


    [Header("Star")]
    [SerializeField] private Sprite starOn;                 // optional: without them the star is tinted instead
    [SerializeField] private Sprite starOff;
    [SerializeField] private Color starOnColour = new Color(232.0f / 255.0f, 184.0f / 255.0f, 58.0f / 255.0f, 1.0f);   // #E8B83A
    [SerializeField] private Color starOffColour = new Color(138.0f / 255.0f, 129.0f / 255.0f, 116.0f / 255.0f, 1.0f); // #8A8174, the compendium's faded ink


    [Header("Placement")]
    [SerializeField] private float gap = 40.0f;                             // canvas units between the sprite's edge and the panel
    [SerializeField] private float margin = 12.0f;                          // canvas units kept clear at the screen edge
    [SerializeField] private RectTransform avoid;                           // the inventory bar, which the panel never covers


    private RectTransform root;
    private Canvas canvas;
    private AnimalController bound;
    private AnimalSelection selection;

    private readonly Vector3[] corners = new Vector3[4];

    private SpriteRenderer boundSprite;


    private void Awake()
    {
        root = transform as RectTransform;
        canvas = GetComponentInParent<Canvas>();

        if (panel == null) Debug.LogError("AnimalStatusPanel: no panel, so the status panel can never show", this);
        if (nameField == null) Debug.LogError("AnimalStatusPanel: no nameField, so animals cannot be renamed", this);
        if (starButton == null) Debug.LogError("AnimalStatusPanel: no starButton, so animals cannot be highlighted", this);
        if (avoid == null) Debug.LogWarning("AnimalStatusPanel: no avoid rect, so the panel may sit on top of the inventory bar", this);

        if (nameField != null)
        {
            nameField.characterLimit = AnimalController.MAX_NAME_LENGTH;
            nameField.lineType = TMP_InputField.LineType.SingleLine;
            nameField.onEndEdit.AddListener(OnNameEdited);
        }

        if (starButton != null) starButton.onClick.AddListener(OnStarClicked);
        if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);

        if (panel != null) panel.gameObject.SetActive(false);
    }


    private void Start()
    {
        selection = AnimalSelection.Instance;

        if (selection == null)
        {
            Debug.LogError("AnimalStatusPanel: no AnimalSelection, so the panel never opens", this);
            return;
        }

        selection.FocusChanged += OnFocusChanged;
        OnFocusChanged(selection.Focused);
    }


    private void OnDestroy()
    {
        if (selection != null) selection.FocusChanged -= OnFocusChanged;

        if (nameField != null) nameField.onEndEdit.RemoveListener(OnNameEdited);
        if (starButton != null) starButton.onClick.RemoveListener(OnStarClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);

        Unbind();
    }


    private void LateUpdate()
    {
        if (bound == null || panel == null) return;

        Place();
    }


    // MARK: binding


    private void OnFocusChanged(AnimalController animal)
    {
        if (animal == bound) return;

        Unbind();

        if (animal == null || panel == null)
        {
            if (panel != null) panel.gameObject.SetActive(false);
            return;
        }

        bound = animal;
        bound.StatusChanged += Refresh;
        boundSprite = animal.GetComponentInChildren<SpriteRenderer>(true);

        panel.gameObject.SetActive(true);
        Refresh();

        // pitfall 2: the layout sizes the panel, and it has not run yet this frame
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        Place();
    }


    // A name still being typed is kept, not lost: disabling the field only deactivates it, and sends
    // no end edit (TMP_InputField.OnDisable), so it is committed here first
    private void Unbind()
    {
        if (bound == null) return;

        CommitName();

        bound.StatusChanged -= Refresh;
        bound = null;
        boundSprite = null;
    }


    private void Refresh()
    {
        if (bound == null) return;

        if (nameField != null && !nameField.isFocused) nameField.SetTextWithoutNotify(bound.DisplayName);

        bool on = bound.Highlighted;

        if (starImage != null)
        {
            Sprite sprite = on ? starOn : starOff;
            if (sprite != null) starImage.sprite = sprite;

            starImage.color = on ? starOnColour : starOffColour;
        }
    }


    // MARK: controls


    private void OnNameEdited(string text)
    {
        CommitName();
    }


    // the animal trims, caps and falls back to the species name; the field then shows what it kept
    private void CommitName()
    {
        if (bound == null || nameField == null) return;

        bound.SetCustomName(nameField.text);
        nameField.SetTextWithoutNotify(bound.DisplayName);
    }


    private void OnStarClicked()
    {
        if (bound != null) bound.SetHighlighted(!bound.Highlighted);
    }


    private void OnCloseClicked()
    {
        if (selection != null) selection.Clear();
    }


    // MARK: placement


    // Beside the sprite, gap from its right edge unless that runs off the screen (then gap from its
    // left), vertically centred on it, then clamped inside the screen and lifted clear of the
    // inventory bar
    private void Place()
    {
        Camera viewCamera = Camera.main;
        if (viewCamera == null || root == null) return;

        if (!TryGetSpriteRect(viewCamera, out Rect sprite)) return;

        Rect area = root.rect;
        Vector2 size = panel.rect.size;
        Vector2 pivot = panel.pivot;

        // the panel's left edge when it sits to the right, and when it has to sit to the left
        float left = sprite.xMax + gap;
        if (left + size.x > area.xMax - margin) left = sprite.xMin - gap - size.x;

        left = Mathf.Clamp(left, area.xMin + margin, Mathf.Max(area.xMin + margin, area.xMax - margin - size.x));

        float bottom = sprite.center.y - size.y * 0.5f;
        bottom = Mathf.Clamp(bottom, area.yMin + margin, Mathf.Max(area.yMin + margin, area.yMax - margin - size.y));

        bottom = KeepOffAvoid(left, bottom, size);

        panel.localPosition = new Vector3(left + pivot.x * size.x, bottom + pivot.y * size.y, 0.0f);
    }


    // The animal's sprite as drawn, in this rect's local space. Read from the sprite rather than the
    // renderer's bounds, so a hidden animal (a burrowed worm) still has a size and the panel doesn't
    // jump when it goes under. An animal with no sprite is a point at its transform
    private bool TryGetSpriteRect(Camera viewCamera, out Rect rect)
    {
        rect = default;

        // an overlay canvas converts with no camera
        Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        Sprite sprite = boundSprite != null ? boundSprite.sprite : null;
        int count = sprite != null ? 4 : 1;

        for (int i = 0; i < count; i++)
        {
            Vector3 world;

            if (sprite != null)
            {
                Bounds local = sprite.bounds;
                Vector3 corner = new Vector3((i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y, 0.0f);

                // flipX mirrors the drawing through the pivot
                if (boundSprite.flipX) corner.x = -corner.x;

                world = boundSprite.transform.TransformPoint(corner);
            }
            else
            {
                world = bound.transform.position;
            }

            Vector3 screen = viewCamera.WorldToScreenPoint(world);
            if (screen.z <= 0.0f) return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, canvasCamera, out Vector2 local2)) return false;

            min = Vector2.Min(min, local2);
            max = Vector2.Max(max, local2);
        }

        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }


    // lifts the panel to just above the avoid rect if the two overlap
    private float KeepOffAvoid(float left, float bottom, Vector2 size)
    {
        if (avoid == null || !avoid.gameObject.activeInHierarchy) return bottom;

        avoid.GetWorldCorners(corners);

        Vector2 min = root.InverseTransformPoint(corners[0]);
        Vector2 max = min;

        for (int i = 1; i < 4; i++)
        {
            Vector2 local = root.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        bool overlaps = left < max.x && left + size.x > min.x && bottom < max.y && bottom + size.y > min.y;

        return overlaps ? max.y + margin : bottom;
    }
}
