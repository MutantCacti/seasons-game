using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

public class AnimatedBackground : MonoBehaviour
{
    public enum SpriteSizing { Fit, Fill, Native }

    [Header("Setup")]
    public SpriteAtlas spriteAtlas;
    public RectTransform viewport;      // RectMask2D panel (visible window)
    public GameObject cellPrefab;       // bare GameObject with RectTransform (optional RectMask2D)

    [Header("Grid")]
    public float cellSize = 100f;
    public float spacing = 8f;
    public float scrollSpeed = 50f;

    [Header("Sprite Sizing")]
    public SpriteSizing sizing = SpriteSizing.Fit;

    [Tooltip("Clamp the computed scale to at most this value. 1 = never upscale (only shrink).")]
    public float maxScale = 1f;

    [Tooltip("Only used when sizing = Native. Multiplies the sprite's native pixel size.")]
    public float nativeScale = 1f;

    private Sprite[] sprites;
    private RectTransform[] slotRects;   // the moving slots
    private Image[] images;              // the child Images we draw into
    private RectTransform[] imageRects;  // the child Images' RectTransforms
    private float pitch;
    private int columns, rows;
    private Vector2 velocity;

    void Start()
    {
        if (spriteAtlas == null || cellPrefab == null || viewport == null)
        {
            Debug.LogError("AnimatedBackground: assign spriteAtlas, cellPrefab and viewport.");
            enabled = false;
            return;
        }

        sprites = new Sprite[spriteAtlas.spriteCount];
        spriteAtlas.GetSprites(sprites);
        if (sprites.Length == 0)
        {
            Debug.LogError("AnimatedBackground: atlas contains no sprites.");
            enabled = false;
            return;
        }

        pitch = cellSize + spacing;
        velocity = new Vector2(scrollSpeed, -scrollSpeed);

        columns = Mathf.CeilToInt(viewport.rect.width  / pitch) + 3;
        rows    = Mathf.CeilToInt(viewport.rect.height / pitch) + 3;

        int count = columns * rows;
        slotRects  = new RectTransform[count];
        images     = new Image[count];
        imageRects = new RectTransform[count];

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int i = y * columns + x;
                GameObject slot = Instantiate(cellPrefab, viewport);

                RectTransform slotRT = slot.GetComponent<RectTransform>();
                if (slotRT == null) slotRT = slot.AddComponent<RectTransform>();

                // Slot is the uniform grid cell. Point-anchored top-left, never resized.
                slotRT.anchorMin = new Vector2(0f, 1f);
                slotRT.anchorMax = new Vector2(0f, 1f);
                slotRT.pivot     = new Vector2(0f, 1f);
                slotRT.sizeDelta = new Vector2(cellSize, cellSize);
                slotRT.anchoredPosition = new Vector2(
                    -pitch + x * pitch,
                     pitch - y * pitch
                );

                // Child Image renders the sprite; we control it completely.
                GameObject child = new GameObject("Sprite", typeof(RectTransform), typeof(Image));
                RectTransform childRT = child.GetComponent<RectTransform>();
                childRT.SetParent(slotRT, false);

                Image img = child.GetComponent<Image>();
                img.raycastTarget = false;

                childRT.anchorMin = new Vector2(0f, 1f);
                childRT.anchorMax = new Vector2(0f, 1f);
                childRT.pivot     = new Vector2(0.5f, 0.5f);
                childRT.anchoredPosition = new Vector2(cellSize * 0.5f, -cellSize * 0.5f);

                slotRects[i]  = slotRT;
                images[i]     = img;
                imageRects[i] = childRT;

                AssignRandomSprite(i);
            }
        }
    }

    void Update()
    {
        float dx = velocity.x * Time.deltaTime;
        float dy = velocity.y * Time.deltaTime;

        float rightEdge  = viewport.rect.width;
        float leftEdge   = 0f;
        float topEdge    = 0f;
        float bottomEdge = -viewport.rect.height;

        for (int i = 0; i < slotRects.Length; i++)
        {
            RectTransform rt = slotRects[i];
            Vector2 p = rt.anchoredPosition;
            p.x += dx;
            p.y += dy;

            if (dx > 0f && p.x >= rightEdge + spacing)
            {
                p.x -= columns * pitch;
                AssignRandomSprite(i);
            }
            else if (dx < 0f && p.x + cellSize <= leftEdge - spacing)
            {
                p.x += columns * pitch;
                AssignRandomSprite(i);
            }

            if (dy < 0f && p.y - cellSize <= bottomEdge - spacing)
            {
                p.y += rows * pitch;
                AssignRandomSprite(i);
            }
            else if (dy > 0f && p.y >= topEdge + spacing)
            {
                p.y -= rows * pitch;
                AssignRandomSprite(i);
            }

            rt.anchoredPosition = p;
        }
    }

    void AssignRandomSprite(int index)
    {
        Image img = images[index];
        RectTransform imgRT = imageRects[index];

        Sprite s = sprites[Random.Range(0, sprites.Length)];
        img.sprite = s;
        img.color = Color.white;

        float sw = s.rect.width;
        float sh = s.rect.height;
        if (sw <= 0f || sh <= 0f) return;

        Vector2 renderedSize;
        switch (sizing)
        {
            case SpriteSizing.Fit:
            {
                // Fit entirely inside the cell, but never enlarge beyond maxScale.
                float scale = Mathf.Min(cellSize / sw, cellSize / sh);
                scale = Mathf.Min(scale, maxScale);
                renderedSize = new Vector2(sw * scale, sh * scale);
                break;
            }
            case SpriteSizing.Fill:
            {
                // Cover the cell (crop overflow), but never enlarge beyond maxScale.
                float scale = Mathf.Max(cellSize / sw, cellSize / sh);
                scale = Mathf.Min(scale, maxScale);
                renderedSize = new Vector2(sw * scale, sh * scale);
                break;
            }
            case SpriteSizing.Native:
            default:
            {
                renderedSize = new Vector2(sw * nativeScale, sh * nativeScale);
                break;
            }
        }

        imgRT.sizeDelta = renderedSize;
    }
}