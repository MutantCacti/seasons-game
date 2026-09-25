using System;
using System.Collections.Generic;
using UnityEngine;


// Clicking an animal focuses it: the camera follows it, a pulsing ring marks the ground under it,
// and (from the status panel's slice on) its panel opens.
//
// Picking is in screen space, so animals need no colliders or layers: on release, each live animal's
// drawn bounds are projected to a screen rect and the nearest rect under the pointer wins. It only
// runs for a click, never per frame. A click is a press and release that barely moved, didn't start
// over UI and wasn't on the book, so a camera drag across an animal never selects it and the book
// always wins its own clicks.
//
// Focus ends on a camera drag or WASD (the follow ends), clicking empty world, Escape, the book
// opening, or the animal going away. Clicking another animal moves the focus to it.
public class AnimalSelection : MonoBehaviour
{
    private static AnimalSelection instance;
    public static AnimalSelection Instance { get { return instance; } }


    [Header("References")]
    [SerializeField] private CameraController cameraController;


    [Header("Picking")]
    [SerializeField] private float clickSlop = 8.0f;       // pixels the pointer may move and still be a click
    [SerializeField] private float pickPadding = 8.0f;     // pixels added around an animal's screen rect


    [Header("Marker")]
    [SerializeField] private Sprite markerSprite;           // the ring, laid flat on the ground
    [SerializeField] private Material markerMaterial;       // an unlit sprite material, like the worm's
    [SerializeField] private float markerWidthScale = 0.85f; // the ring's width, as a multiple of the animal's sprite's world width
    [SerializeField] private float markerPulse = 0.08f;     // fraction of the size it swells by
    [SerializeField] private float markerPulseSpeed = 4.0f; // radians per second
    [SerializeField] private float markerLift = 0.01f;      // world units above the ground, so it never z-fights it


    private AnimalController focused;
    private float markerWidth;      // world units across, measured from the focused animal when focus lands
    private Transform markerTransform;
    private SpriteRenderer marker;

    private bool pressed;
    private bool pressIsCandidate;
    private Vector2 pressPosition;

    private BookAnimator subscribedBook;
    private Camera viewCamera;

    private readonly List<Renderer> scratchRenderers = new List<Renderer>();


    public AnimalController Focused { get { return focused; } }
    public bool HasFocus { get { return focused != null; } }


    // raised with the new focus, or null when focus ends
    public event Action<AnimalController> FocusChanged;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one AnimalSelection in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }


    private void Start()
    {
        if (cameraController == null) cameraController = FindAnyObjectByType<CameraController>();

        if (cameraController == null) Debug.LogError("AnimalSelection: no CameraController, so a focused animal cannot be followed", this);
        else cameraController.FollowEnded += OnFollowEnded;

        viewCamera = cameraController != null ? cameraController.GetComponent<Camera>() : null;
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) Debug.LogError("AnimalSelection: no camera, so no animal can be clicked", this);

        if (InputManager.Instance != null) InputManager.Instance.onLeftClickDown += OnLeftClick;
        else Debug.LogError("AnimalSelection: no InputManager, so no animal can be clicked", this);

        if (markerSprite == null) Debug.LogError("AnimalSelection: no markerSprite, so a focused animal has no ring under it", this);
        if (markerMaterial == null) Debug.LogError("AnimalSelection: no markerMaterial, so the ring draws with the default sprite material", this);

        CreateMarker();
    }


    private void OnDestroy()
    {
        if (cameraController != null) cameraController.FollowEnded -= OnFollowEnded;
        if (InputManager.Instance != null) InputManager.Instance.onLeftClickDown -= OnLeftClick;
        if (subscribedBook != null) subscribedBook.Opened -= OnBookOpened;

        if (markerTransform != null) Destroy(markerTransform.gameObject);

        if (instance == this) instance = null;
    }


    private void LateUpdate()
    {
        TrackBook();

        // Unity-null: the animal was destroyed under us
        if (focused == null)
        {
            if (!ReferenceEquals(focused, null)) Clear();
            return;
        }

        UpdateMarker();
    }


    // MARK: focus


    public void Focus(AnimalController animal)
    {
        if (animal == null)
        {
            Clear();
            return;
        }

        if (animal == focused) return;

        // let go of the old one quietly, so its end doesn't read as the player breaking focus
        if (focused != null && cameraController != null) cameraController.StopFollowing(focused.transform);

        focused = animal;

        if (cameraController != null) cameraController.Follow(animal.transform);

        markerWidth = markerWidthScale * SpriteWorldWidth(animal);

        if (marker != null) marker.enabled = true;
        UpdateMarker();

        FocusChanged?.Invoke(focused);
    }


    public void Clear()
    {
        if (ReferenceEquals(focused, null)) return;

        if (focused != null && cameraController != null) cameraController.StopFollowing(focused.transform);

        focused = null;

        if (marker != null) marker.enabled = false;

        FocusChanged?.Invoke(null);
    }


    // the player took the camera: a drag, WASD, or a ping's pan
    private void OnFollowEnded()
    {
        Clear();
    }


    private void OnBookOpened()
    {
        Clear();
    }


    // the book is an entity, so it may arrive after this does
    private void TrackBook()
    {
        BookAnimator book = BookAnimator.Instance;
        if (book == subscribedBook) return;

        if (subscribedBook != null) subscribedBook.Opened -= OnBookOpened;
        subscribedBook = book;
        if (subscribedBook != null) subscribedBook.Opened += OnBookOpened;
    }


    // MARK: clicking


    private void OnLeftClick(bool down)
    {
        InputManager input = InputManager.Instance;
        if (input == null || !input.HasPointerPosition) return;

        Vector2 pointer = input.PointerPosition;

        if (down)
        {
            pressed = true;
            pressPosition = pointer;
            pressIsCandidate = IsClickCandidate(pointer);
            return;
        }

        if (!pressed) return;
        pressed = false;

        if (!pressIsCandidate) return;
        if ((pointer - pressPosition).sqrMagnitude > clickSlop * clickSlop) return;

        // on empty world this is null, which ends the focus
        Focus(PickAt(pointer));
    }


    // not frozen behind a menu, not on UI, not on the book (which acts on this same press)
    private bool IsClickCandidate(Vector2 pointer)
    {
        if (Time.timeScale <= 0.0f) return false;
        if (PointerRaycastManager.IsPointerOverUI(pointer)) return false;

        BookAnimator book = BookAnimator.Instance;
        if (book != null && (book.IsPointerHovering || book.IsReading)) return false;

        return true;
    }


    // the nearest animal whose padded screen rect holds the pointer
    private AnimalController PickAt(Vector2 pointer)
    {
        AnimalManager animals = AnimalManager.Instance;
        if (animals == null || viewCamera == null) return null;

        AnimalController best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < animals.LiveCount; i++)
        {
            AnimalController candidate = animals.LiveAt(i);
            if (candidate == null) continue;

            if (!TryGetScreenRect(candidate, out Rect rect)) continue;

            rect.xMin -= pickPadding;
            rect.yMin -= pickPadding;
            rect.xMax += pickPadding;
            rect.yMax += pickPadding;

            if (!rect.Contains(pointer)) continue;

            float distance = (rect.center - pointer).sqrMagnitude;
            if (distance >= bestDistance) continue;

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }


    // The screen rect of everything the animal draws right now. An animal drawing nothing (a burrowed
    // worm) has no rect and can't be clicked
    private bool TryGetScreenRect(AnimalController animal, out Rect rect)
    {
        rect = default;

        animal.GetComponentsInChildren(false, scratchRenderers);

        bool any = false;
        Bounds bounds = default;

        for (int i = 0; i < scratchRenderers.Count; i++)
        {
            Renderer candidate = scratchRenderers[i];
            if (candidate == null || !candidate.enabled) continue;

            if (!any)
            {
                bounds = candidate.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(candidate.bounds);
            }
        }

        if (!any) return false;

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        float xMin = float.PositiveInfinity, yMin = float.PositiveInfinity;
        float xMax = float.NegativeInfinity, yMax = float.NegativeInfinity;

        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = new Vector3(
                (corner & 1) == 0 ? min.x : max.x,
                (corner & 2) == 0 ? min.y : max.y,
                (corner & 4) == 0 ? min.z : max.z);

            Vector3 screen = viewCamera.WorldToScreenPoint(point);

            // behind the camera, so the projection is meaningless
            if (screen.z <= 0.0f) return false;

            xMin = Mathf.Min(xMin, screen.x);
            yMin = Mathf.Min(yMin, screen.y);
            xMax = Mathf.Max(xMax, screen.x);
            yMax = Mathf.Max(yMax, screen.y);
        }

        rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }


    // MARK: marker


    // The world width of the sprite the animal draws, read from the sprite rather than the renderer so
    // a hidden animal (a burrowed worm) still measures. Measured once per focus: the worm's burrow
    // sprite swapping in shouldn't make the ring jump. 1 when the animal has no sprite
    private static float SpriteWorldWidth(AnimalController animal)
    {
        SpriteRenderer spriteRenderer = animal.GetComponentInChildren<SpriteRenderer>(true);
        if (spriteRenderer == null || spriteRenderer.sprite == null) return 1.0f;

        float width = spriteRenderer.sprite.bounds.size.x * Mathf.Abs(spriteRenderer.transform.lossyScale.x);

        return width > 0.0f ? width : 1.0f;
    }


    private void CreateMarker()
    {
        // at the scene root, so nothing this sits under can scale or tilt it
        GameObject holder = new GameObject("Animal Focus Marker");

        markerTransform = holder.transform;

        // lying flat, facing up
        markerTransform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

        marker = holder.AddComponent<SpriteRenderer>();
        marker.sprite = markerSprite;
        if (markerMaterial != null) marker.sharedMaterial = markerMaterial;
        marker.enabled = false;
    }


    // on the top surface of the column the animal is over: under a crawling worm, above a burrowed one
    private void UpdateMarker()
    {
        if (marker == null || focused == null) return;

        Vector3 position = focused.transform.position;
        float groundY = position.y;

        TerrainManager terrain = TerrainManager.Instance;

        if (terrain != null)
        {
            int top = terrain.GetTopLevel(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
            if (top >= 0) groundY = terrain.GetLevelHeight(top);
        }

        markerTransform.position = new Vector3(position.x, groundY + markerLift, position.z);

        float spriteWidth = markerSprite != null ? markerSprite.bounds.size.x : 1.0f;
        if (spriteWidth <= 0.0f) spriteWidth = 1.0f;

        float pulse = 1.0f + markerPulse * Mathf.Sin(Time.time * markerPulseSpeed);
        float scale = markerWidth / spriteWidth * pulse;

        markerTransform.localScale = new Vector3(scale, scale, scale);
    }
}
