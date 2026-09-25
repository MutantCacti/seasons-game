using System;
using System.Collections.Generic;
using DG.Tweening;
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
//
// Feel: the animal under the pointer grows a little and shows the hand cursor, so animals read as
// clickable before they are clicked (the same rect test, every frame; there are only a handful). A
// focused animal hops, the ring pops in under it and shrinks away when focus ends.
//
// It also keeps every starred animal glowing (AnimalGlow), bursting when the star is switched on.
// The glow is drawn even for a hidden animal, and counts as something it draws, so a starred worm
// can be clicked while it is burrowed and an unstarred one can't.
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
    [SerializeField] private Material markerMaterial;       // the placement ghost, so the ring is see-through
    [SerializeField] private float markerWidthScale = 0.85f; // the ring's width, as a multiple of the animal's sprite's world width
    [SerializeField] private float markerPulse = 0.08f;     // fraction of the size it swells by
    [SerializeField] private float markerPulseSpeed = 4.0f; // radians per second
    [SerializeField] private float markerLift = 0.01f;      // world units above the ground, so it never z-fights it
    [SerializeField] private float markerPopOvershoot = 1.15f;
    [SerializeField] private float markerPopDuration = 0.22f;
    [SerializeField] private float markerHideDuration = 0.14f;


    [Header("Sound")]
    [SerializeField] private SoundObject focusSound;         // optional, played at the animal as it is focused


    [Header("Highlight Glow")]
    [SerializeField] private Shader glowShader;              // Seasons/SpriteGlow; referenced here so builds include it
    [SerializeField] private Color glowColour = new Color(240.0f / 255.0f, 205.0f / 255.0f, 110.0f / 255.0f, 1.0f);   // #F0CD6E, the star's gold
    [SerializeField] private float glowRadius = 0.15f;       // world units the halo reaches past the sprite
    [SerializeField] private float glowWidth = 0.04f;        // world units the bright core pulses out to
    [SerializeField] private float glowIntensity = 0.8f;
    [SerializeField] private float glowPulseSpeed = 8.0f;    // as the book's and the tabs' glows, so everything pulses together
    [SerializeField] [Range(0.0f, 1.0f)] private float glowHiddenFill = 0.55f;
    [SerializeField] private float glowBurstWidth = 0.12f;   // world units the core bursts out by when starred
    [SerializeField] private float glowBurstDuration = 0.6f;


    private AnimalController focused;
    private float markerWidth;      // world units across, measured from the focused animal when focus lands
    private Transform markerTransform;
    private SpriteRenderer marker;

    private bool pressed;
    private bool pressIsCandidate;
    private Vector2 pressPosition;

    // The camera starts a drag on the same press as a click, and the first jitter of it ends the
    // follow. Mid click that's held here instead: a real drag ends the focus once it passes the slop,
    // and a click decides on release, so clicking the focused animal again never flickers its panel
    private bool pendingClear;

    private BookAnimator subscribedBook;
    private Camera viewCamera;

    private readonly List<Renderer> scratchRenderers = new List<Renderer>();

    private float markerPresence;
    private Tween markerTween;

    private AnimalController hovered;
    private bool ownsCursor;

    // the highlight each animal had when last looked at, so only a change made in play bursts
    private readonly Dictionary<AnimalController, bool> knownHighlights = new Dictionary<AnimalController, bool>();
    private readonly List<AnimalController> scratchForget = new List<AnimalController>();
    private bool warnedAboutGlowShader;


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
        if (markerTween != null) markerTween.Kill();

        ReleaseCursor();

        if (instance == this) instance = null;
    }


    private void LateUpdate()
    {
        TrackBook();

        if (pendingClear && pressed && MovedPastSlop())
        {
            pendingClear = false;
            Clear();
        }

        // Unity-null: the animal was destroyed under us
        if (focused == null && !ReferenceEquals(focused, null)) Clear();

        UpdateGlows();
        UpdateHover();
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

        // clicked again: its follow may have ended with the press, so take it back up
        if (animal == focused)
        {
            if (cameraController != null) cameraController.Follow(animal.transform);
            return;
        }

        // let go of the old one quietly, so its end doesn't read as the player breaking focus
        if (focused != null && cameraController != null) cameraController.StopFollowing(focused.transform);

        focused = animal;

        if (cameraController != null) cameraController.Follow(animal.transform);

        markerWidth = markerWidthScale * SpriteWorldWidth(animal);
        ShowMarker();

        animal.OnFocused();

        if (focusSound != null && AudioManager.Instance != null) AudioManager.Instance.PlaySFX(focusSound, animal.transform.position);

        FocusChanged?.Invoke(focused);
    }


    public void Clear()
    {
        if (ReferenceEquals(focused, null)) return;

        if (focused != null && cameraController != null) cameraController.StopFollowing(focused.transform);

        focused = null;

        HideMarker();

        FocusChanged?.Invoke(null);
    }


    // the player took the camera: a drag, WASD, or a ping's pan. During a click it waits, see pendingClear
    private void OnFollowEnded()
    {
        if (pressed && pressIsCandidate)
        {
            pendingClear = true;
            return;
        }

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


    // MARK: glows


    // A glow for every starred animal, off for every other. First sight of an animal (a load) shows
    // its glow quietly; a change after that is the player starring it, which bursts
    private void UpdateGlows()
    {
        AnimalManager animals = AnimalManager.Instance;
        if (animals == null) return;

        for (int i = 0; i < animals.LiveCount; i++)
        {
            AnimalController animal = animals.LiveAt(i);
            if (animal == null) continue;

            bool on = animal.Highlighted;
            bool seen = knownHighlights.TryGetValue(animal, out bool was);

            if (seen && was == on) continue;

            knownHighlights[animal] = on;

            AnimalGlow glow = animal.GetComponent<AnimalGlow>();

            if (!on)
            {
                if (glow != null) glow.Hide();
                continue;
            }

            if (glow == null) glow = AddGlow(animal);
            if (glow != null) glow.Show(seen);
        }

        // animals that have gone away
        if (knownHighlights.Count <= animals.LiveCount) return;

        scratchForget.Clear();
        foreach (AnimalController known in knownHighlights.Keys)
        {
            if (known == null) scratchForget.Add(known);
        }

        for (int i = 0; i < scratchForget.Count; i++) knownHighlights.Remove(scratchForget[i]);
    }


    private AnimalGlow AddGlow(AnimalController animal)
    {
        Shader shader = glowShader != null ? glowShader : Shader.Find("Seasons/SpriteGlow");

        if (shader == null)
        {
            if (!warnedAboutGlowShader)
            {
                warnedAboutGlowShader = true;
                Debug.LogError("AnimalSelection: no glowShader, so starred animals cannot glow", this);
            }

            return null;
        }

        AnimalGlow glow = animal.gameObject.AddComponent<AnimalGlow>();

        AnimalGlow.Settings settings = new AnimalGlow.Settings
        {
            shader = shader,
            colour = glowColour,
            radius = glowRadius,
            width = glowWidth,
            intensity = glowIntensity,
            pulseSpeed = glowPulseSpeed,
            hiddenFill = glowHiddenFill,
            flareWidth = glowBurstWidth,
            flareDuration = glowBurstDuration,
        };

        if (glow.Init(settings)) return glow;

        Destroy(glow);
        return null;
    }


    // MARK: hover


    // Only while nothing else could be going on: no button held (a drag, a placement), not over UI,
    // not on the book, not paused. The cursor is only overridden while this set it, so a placement's
    // own cursor is never trampled
    private void UpdateHover()
    {
        AnimalController under = null;

        InputManager input = InputManager.Instance;

        // pressed covers every press, a slot drag's included, and a drag is not a hover
        if (input != null && input.HasPointerPosition && !pressed && IsClickCandidate(input.PointerPosition))
        {
            under = PickAt(input.PointerPosition);
        }

        if (under == hovered) return;

        if (hovered != null) hovered.SetHovered(false);
        hovered = under;
        if (hovered != null) hovered.SetHovered(true);

        if (hovered != null) TakeCursor();
        else ReleaseCursor();
    }


    private void TakeCursor()
    {
        if (CursorManager.Instance == null) return;

        CursorManager.Instance.SetOverride(CursorType.Pointer);
        ownsCursor = true;
    }


    private void ReleaseCursor()
    {
        if (!ownsCursor) return;

        ownsCursor = false;
        if (CursorManager.Instance != null) CursorManager.Instance.ClearOverride();
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

        bool wasPendingClear = pendingClear;
        pendingClear = false;

        bool isClick = pressIsCandidate && (pointer - pressPosition).sqrMagnitude <= clickSlop * clickSlop;

        if (!isClick)
        {
            if (wasPendingClear) Clear();
            return;
        }

        // on empty world this is null, which ends the focus
        Focus(PickAt(pointer));
    }


    private bool MovedPastSlop()
    {
        InputManager input = InputManager.Instance;
        if (input == null || !input.HasPointerPosition) return false;

        return (input.PointerPosition - pressPosition).sqrMagnitude > clickSlop * clickSlop;
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


    // The screen rect of everything the animal draws right now, its glow included. An animal drawing
    // nothing (an unstarred burrowed worm) has no rect and can't be clicked
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

        // material before sprite, as PlacementController does, so a lit material (the ghost) picks the
        // sprite's texture up
        marker = holder.AddComponent<SpriteRenderer>();
        if (markerMaterial != null) marker.sharedMaterial = markerMaterial;
        marker.sprite = markerSprite;
        marker.enabled = false;
    }


    // pops in with an overshoot; a new focus while it is still showing pops it again from where it is
    private void ShowMarker()
    {
        if (marker == null) return;

        marker.enabled = true;

        if (markerTween != null) markerTween.Kill();

        Sequence pop = DOTween.Sequence();
        pop.Append(DOTween.To(() => markerPresence, value => markerPresence = value, markerPopOvershoot, markerPopDuration * 0.6f).SetEase(Ease.OutQuad));
        pop.Append(DOTween.To(() => markerPresence, value => markerPresence = value, 1.0f, markerPopDuration * 0.4f).SetEase(Ease.InOutQuad));
        pop.SetLink(gameObject);

        markerTween = pop;
    }


    // shrinks away where it last was, then hides
    private void HideMarker()
    {
        if (marker == null || !marker.enabled) return;

        if (markerTween != null) markerTween.Kill();

        markerTween = DOTween.To(() => markerPresence, value => markerPresence = value, 0.0f, markerHideDuration)
            .SetEase(Ease.InBack)
            .OnComplete(() => { if (marker != null) marker.enabled = false; })
            .SetLink(gameObject);
    }


    // on the top surface of the column the animal is over: under a crawling worm, above a burrowed one.
    // With no focus it stays where it was, shrinking away
    private void UpdateMarker()
    {
        if (marker == null || !marker.enabled) return;

        if (focused == null)
        {
            ScaleMarker();
            return;
        }

        Vector3 position = focused.transform.position;
        float groundY = position.y;

        TerrainManager terrain = TerrainManager.Instance;

        if (terrain != null)
        {
            int top = terrain.GetTopLevel(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
            if (top >= 0) groundY = terrain.GetLevelHeight(top);
        }

        markerTransform.position = new Vector3(position.x, groundY + markerLift, position.z);

        ScaleMarker();
    }


    private void ScaleMarker()
    {
        float spriteWidth = markerSprite != null ? markerSprite.bounds.size.x : 1.0f;
        if (spriteWidth <= 0.0f) spriteWidth = 1.0f;

        float pulse = 1.0f + markerPulse * Mathf.Sin(Time.time * markerPulseSpeed);
        float scale = markerWidth / spriteWidth * pulse * markerPresence;

        markerTransform.localScale = new Vector3(scale, scale, scale);
    }
}
