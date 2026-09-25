using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;


// Flies granted ingredients into the hotbar, Islanders style: each unit's icon is tossed up out of the
// entity that caused the unlock, grows as it arcs down, and lands in its slot. The inventory data has
// already changed by then; the slot's shown count is held back (InventoryManager.Add deferDisplay) and
// released here one landing at a time, so a save, a drag or a reload mid-flight can never lose or
// double anything, only skip the show.
//
// Sits on a full-screen "Flight Layer" under the Screen Space - Overlay canvas, drawn after the hotbar.
// In an overlay canvas a RectTransform's world position is its screen position, so everything here
// is worked out in screen pixels.
public class IngredientFlightManager : MonoBehaviour
{
    private static IngredientFlightManager instance;
    public static IngredientFlightManager Instance { get { return instance; } }


    // MARK: references
    [SerializeField] private InventoryManager inventoryManager;
    // a Simple Image, no raycast target, sized like a slot's icon. an optional SpriteGlow child with
    // pulseSpeed 0 gives it a steady glow
    [SerializeField] private Image flierPrefab;
    [SerializeField] private Color glowColour = new Color(1.0f, 0.95f, 0.8f, 1.0f);

    // MARK: rhythm
    [SerializeField] private float stagger = 0.07f;
    [SerializeField] private float flightDuration = 0.75f;
    // past this many icons in one batch the rest ride on each item's last icon, so a huge grant
    // doesn't turn into a queue
    [SerializeField] private int maxIcons = 12;

    // MARK: arc (canvas units, scaled by the canvas like everything else on it)
    [SerializeField] private float arcHeight = 180.0f;
    [Range(0f, 1f)] [SerializeField] private float arcHeightJitter = 0.3f;
    [SerializeField] private float arcLean = 60.0f;
    [SerializeField] private Ease flightEase = Ease.Linear;
    [SerializeField] private float sourceHeightOffset = 1.0f;
    [SerializeField] private float edgeMargin = 48.0f;

    // MARK: look
    // small at the source, grows through the arc, overshoots just before landing, settles to slot size
    [SerializeField] private AnimationCurve sizeCurve = new AnimationCurve(
        new Keyframe(0.0f, 0.35f), new Keyframe(0.85f, 1.15f), new Keyframe(1.0f, 1.0f));
    [SerializeField] private float wobbleDegrees = 14.0f;
    [SerializeField] private float wobbleCycles = 1.5f;
    [SerializeField] private float missFadeDuration = 0.15f;

    // MARK: sound
    [SerializeField] private SoundObject launchSound;       // once per batch
    [SerializeField] private SoundObject landSound;         // every landing
    [SerializeField] private SoundObject newSlotLandSound;  // a landing that pops a new slot in
    // each landing in a batch plays this much higher than the last, so a run climbs
    [SerializeField] private float pitchStepSemitones = 1.0f;
    [SerializeField] private int maxPitchSteps = 12;


    private class Batch
    {
        public EntityId source;
        public List<IngredientGrant> grants;
    }


    private class Flight
    {
        public Image image;
        public RectTransform rect;
        public SpriteGlow glow;
        public GameItem item;
        public int amount;
        public int index;
        public Vector3 start;
        public Vector3 lastTarget;
        public float height;
        public float lean;
        public float phase;
        public Tween tween;
    }


    private readonly List<Batch> queued = new List<Batch>();
    private readonly List<Flight> active = new List<Flight>();
    private readonly List<Flight> pool = new List<Flight>();

    private RectTransform layer;
    private Canvas rootCanvas;
    // when the last queued icon leaves, so a batch landing mid-cascade follows on rather than overlapping
    private float cascadeEndsAt;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("IngredientFlightManager: Found more than one IngredientFlightManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        layer = transform as RectTransform;

        Canvas canvas = GetComponentInParent<Canvas>();
        rootCanvas = canvas != null ? canvas.rootCanvas : null;

        if (layer == null) Debug.LogError("IngredientFlightManager: not on a RectTransform, so icons have no layer to fly on", this);
        if (rootCanvas == null) Debug.LogError("IngredientFlightManager: not under a Canvas, so no icon can be drawn", this);
        if (flierPrefab == null) Debug.LogError("IngredientFlightManager: no flierPrefab, so granted ingredients appear without flying in", this);
    }


    // static event: the pairing is what keeps a reloaded scene from leaving this listening
    private void OnEnable()
    {
        PuzzleManager.IngredientsGranted += OnIngredientsGranted;
    }


    private void OnDisable()
    {
        PuzzleManager.IngredientsGranted -= OnIngredientsGranted;

        // not time-based, so it still runs when the scene goes down while paused (timeScale 0)
        StopAll();
    }


    private void OnDestroy()
    {
        StopAll();

        if (instance == this) instance = null;
    }


    private void Start()
    {
        if (inventoryManager == null) inventoryManager = InventoryManager.Instance;

        if (inventoryManager == null)
        {
            Debug.LogError("IngredientFlightManager: no InventoryManager, so nothing can land", this);
        }
    }


    // MARK: queue


    // raised from PuzzleManager, possibly inside its Start before the hotbar has laid out, so launching
    // waits for LateUpdate
    private void OnIngredientsGranted(EntityId source, IReadOnlyList<IngredientGrant> grants)
    {
        if (grants == null || grants.Count == 0) return;

        // copied: the list belongs to the batch that raised it
        queued.Add(new Batch { source = source, grants = new List<IngredientGrant>(grants) });
    }


    private void LateUpdate()
    {
        if (queued.Count == 0) return;

        // slots bound this frame have to be laid out before they can be aimed at (pitfall 2)
        Canvas.ForceUpdateCanvases();

        for (int i = 0; i < queued.Count; i++)
        {
            Launch(queued[i]);
        }

        queued.Clear();
    }


    // MARK: launch


    private void Launch(Batch batch)
    {
        if (inventoryManager == null) inventoryManager = InventoryManager.Instance;

        if (flierPrefab == null || layer == null || rootCanvas == null || inventoryManager == null)
        {
            ReleaseImmediately(batch);
            return;
        }

        Vector3 start = StartPoint(batch.source);
        float scale = rootCanvas.scaleFactor;

        float delayBase = Mathf.Max(0.0f, cascadeEndsAt - Time.time);
        int index = 0;
        int budget = Mathf.Max(1, maxIcons);

        for (int g = 0; g < batch.grants.Count; g++)
        {
            IngredientGrant grant = batch.grants[g];
            if (grant.item == null || grant.count <= 0) continue;

            // at least one icon per item; once the budget runs out the last icon carries the rest
            int icons = Mathf.Clamp(budget - index, 1, grant.count);

            for (int i = 0; i < icons; i++)
            {
                int amount = i < icons - 1 ? 1 : grant.count - (icons - 1);

                Fly(grant.item, amount, index, start, delayBase + index * stagger, scale);
                index++;
            }
        }

        cascadeEndsAt = Time.time + delayBase + index * stagger;

        PlayLaunch(batch.source);
    }


    private void Fly(GameItem item, int amount, int index, Vector3 start, float delay, float scale)
    {
        Flight flight = Take();

        flight.item = item;
        flight.amount = amount;
        flight.index = index;
        flight.start = start;
        flight.lastTarget = start;
        flight.height = arcHeight * scale * (1.0f + Random.Range(-arcHeightJitter, arcHeightJitter));
        flight.lean = Random.Range(-arcLean, arcLean) * scale;
        flight.phase = Random.Range(0.0f, Mathf.PI * 2.0f);

        flight.image.sprite = item.Icon;
        flight.image.color = Color.white;

        // the glow reads the parent's sprite, and only rebuilds when told to
        if (flight.glow != null)
        {
            flight.glow.Show(glowColour);
            flight.glow.SetVerticesDirty();
            flight.glow.SetMaterialDirty();
        }

        active.Add(flight);

        flight.tween = DOVirtual.Float(0.0f, 1.0f, flightDuration, t => Step(flight, t))
            .SetDelay(delay)
            .SetEase(flightEase)
            .SetLink(gameObject)
            .OnStart(() =>
            {
                Step(flight, 0.0f);
                flight.rect.gameObject.SetActive(true);
            })
            .OnComplete(() => Arrive(flight));
    }


    // MARK: flight


    // quadratic bezier, control point above the midpoint. with an even t it traces a parabola, so it
    // slows at the top and speeds up into the slot like something tossed. the end is re-read every
    // update because the hotbar re-centres as slots appear
    private void Step(Flight flight, float t)
    {
        Vector3 end = Target(flight);
        Vector3 control = (flight.start + end) * 0.5f + new Vector3(flight.lean, flight.height, 0.0f);

        float u = 1.0f - t;
        flight.rect.position = u * u * flight.start + 2.0f * u * t * control + t * t * end;

        flight.rect.localScale = Vector3.one * sizeCurve.Evaluate(t);

        float wobble = Mathf.Sin(t * wobbleCycles * Mathf.PI * 2.0f + flight.phase) * wobbleDegrees * u;
        flight.rect.localRotation = Quaternion.Euler(0.0f, 0.0f, wobble);
    }


    // the slot pivot is its centre, so its position is where the icon lands. a concealed slot is at
    // scale 0 but keeps its laid-out position
    private Vector3 Target(Flight flight)
    {
        if (inventoryManager != null && inventoryManager.TryGetSlot(flight.item, out ItemInventorySlot slot))
        {
            flight.lastTarget = slot.transform.position;
        }

        return flight.lastTarget;
    }


    private void Arrive(Flight flight)
    {
        flight.tween = null;

        ItemInventorySlot slot = null;
        bool hasSlot = inventoryManager != null && inventoryManager.TryGetSlot(flight.item, out slot);
        bool popsSlot = hasSlot && slot.IsConcealed;

        if (inventoryManager != null) inventoryManager.ReleaseDisplay(flight.item, flight.amount);

        PlayLand(popsSlot, flight.index);

        // placed down to nothing mid-flight: there is no slot to land in, so it just fades at the hotbar
        if (!hasSlot)
        {
            flight.tween = flight.image.DOFade(0.0f, missFadeDuration)
                .SetLink(gameObject)
                .OnComplete(() => Recycle(flight));
            return;
        }

        Recycle(flight);
    }


    // MARK: start point


    private Vector3 StartPoint(EntityId source)
    {
        Camera viewCamera = Camera.main;
        float margin = edgeMargin * (rootCanvas != null ? rootCanvas.scaleFactor : 1.0f);

        // nobody to credit (the starting puzzles): rise from just below the bottom centre
        if (source == null || viewCamera == null) return new Vector3(Screen.width * 0.5f, -margin, 0.0f);

        Vector3 screen = viewCamera.WorldToScreenPoint(source.transform.position + Vector3.up * sourceHeightOffset);
        bool behind = screen.z < 0.0f;

        if (!behind && screen.x >= 0.0f && screen.x <= Screen.width && screen.y >= 0.0f && screen.y <= Screen.height)
        {
            return new Vector3(screen.x, screen.y, 0.0f);
        }

        // off-screen: come in from the edge nearest it, just outside. behind the camera the
        // projection is mirrored, so flip it back
        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 direction = new Vector2(screen.x, screen.y) - centre;
        if (behind) direction = -direction;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;

        float reachX = direction.x != 0.0f ? (centre.x + margin) / Mathf.Abs(direction.x) : float.MaxValue;
        float reachY = direction.y != 0.0f ? (centre.y + margin) / Mathf.Abs(direction.y) : float.MaxValue;

        Vector2 edge = centre + direction * Mathf.Min(reachX, reachY);
        return new Vector3(edge.x, edge.y, 0.0f);
    }


    // MARK: sound


    private void PlayLaunch(EntityId source)
    {
        if (launchSound == null || AudioManager.Instance == null) return;

        AudioManager.Instance.PlaySFX(launchSound, source != null ? source.transform.position : ListenerPosition());
    }


    private void PlayLand(bool popsSlot, int index)
    {
        if (AudioManager.Instance == null) return;

        SoundObject sound = popsSlot && newSlotLandSound != null ? newSlotLandSound : landSound;
        if (sound == null) return;

        float semitones = Mathf.Min(index, Mathf.Max(0, maxPitchSteps)) * pitchStepSemitones;
        AudioManager.Instance.PlaySFX(sound, ListenerPosition(), Mathf.Pow(2.0f, semitones / 12.0f));
    }


    // landing sounds belong to the player, not to a place in the world
    private Vector3 ListenerPosition()
    {
        Camera viewCamera = Camera.main;
        return viewCamera != null ? viewCamera.transform.position : Vector3.zero;
    }


    // MARK: pool


    private Flight Take()
    {
        if (pool.Count > 0)
        {
            Flight reused = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            return reused;
        }

        Image image = Instantiate(flierPrefab, layer);
        image.raycastTarget = false;
        image.gameObject.SetActive(false);

        return new Flight
        {
            image = image,
            rect = image.rectTransform,
            glow = image.GetComponentInChildren<SpriteGlow>(true)
        };
    }


    private void Recycle(Flight flight)
    {
        active.Remove(flight);

        flight.tween = null;
        flight.item = null;

        if (flight.image == null) return;

        flight.rect.gameObject.SetActive(false);
        pool.Add(flight);
    }


    // MARK: teardown


    private void ReleaseImmediately(Batch batch)
    {
        if (inventoryManager == null) return;

        for (int i = 0; i < batch.grants.Count; i++)
        {
            IngredientGrant grant = batch.grants[i];
            if (grant.item != null) inventoryManager.ReleaseDisplay(grant.item, grant.count);
        }
    }


    // every held unit is shown again, so nothing is left concealed when flights can't finish
    private void StopAll()
    {
        for (int i = 0; i < active.Count; i++)
        {
            Tween tween = active[i].tween;
            if (tween != null && tween.IsActive()) tween.Kill();
            active[i].tween = null;
        }

        for (int i = active.Count - 1; i >= 0; i--)
        {
            Recycle(active[i]);
        }

        queued.Clear();
        cascadeEndsAt = 0.0f;

        // unity's null check: on teardown the inventory may already be gone, and its held counts with it
        if (inventoryManager != null) inventoryManager.ReleaseAllDisplay();
    }
}
