using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;


// Turns a drag out of an inventory slot into a terrain remap and/or a spawned entity, and is the one
// path anything else that puts an item into the world takes too (growth, through Replace)
public class PlacementController : MonoBehaviour
{
    // How a committed placement looks and sounds. Player is a drop, Growth is something growing while
    // the player watches, Silent is growth caught up on load, which the player never saw happen
    public enum PlaceFeel
    {
        Player,
        Growth,
        Silent,
    }

    private static PlacementController instance;
    public static PlacementController Instance { get { return instance; } }

    // True while a ground change is still animating. A puzzle can look spent while the tile under it
    // is still growing, so PuzzleManager holds its judgement until this clears
    public bool IsPresenting { get { return presentationsInFlight > 0; } }

    // we can't have ghosts running their prefab behaviours so any component
    // that isn't of this type (or a Transform) gets destroyed
    private static readonly HashSet<Type> ghostWhitelist = new HashSet<Type>()
    {
        typeof(SpriteRenderer),
        typeof(BillboardSprite),
    };


    [SerializeField] private TerrainManager terrainManager;
    [SerializeField] private EntityManager entityManager;
    [SerializeField] private InventoryManager inventoryManager;

    // Resolves an item with no tile and no prefab: one that some item reacts to is a reagent and can
    // be dropped on that entity, anything else has nothing to place
    [SerializeField] private GameItemRegistry registry;

    [SerializeField] private Camera viewCamera;
    [SerializeField] private CameraController cameraController;

    [Header("Drop target")]
    [SerializeField] private LayerMask terrainMask;
    [SerializeField] private float dropRayDistance = 1000.0f;

    [Header("Preview")]
    [SerializeField] private Material ghostMaterial;

    [Header("Placement feedback")]
    [SerializeField] private SoundObject defaultPlaceSound;
    [SerializeField] private Sprite rippleSprite;
    [SerializeField] private Material rippleMaterial;
    [SerializeField] private Color rippleColour = new Color(1.0f, 1.0f, 1.0f, 0.8f);
    [SerializeField] private float rippleDiameter = 1.4f;
    [SerializeField] private float rippleDuration = 0.25f;

    [SerializeField] private float growDuration = 0.08f;
    [SerializeField] private float growSettleDuration = 0.07f;
    [SerializeField] private float growOvershoot = 1.10f;

    [Tooltip("growth: the new thing starts at the old one's sprite height and pops to full size")]
    [SerializeField] private float growthPopDuration = 0.35f;
    [Tooltip("Ease.OutBack overshoot of the growth pop")]
    [SerializeField] private float growthPopOvershoot = 1.7f;
    [SerializeField] private float growthPopMinScale = 0.05f;
    [SerializeField] private float growthPopMaxScale = 5.0f;

    [SerializeField] private float shakeMagnitude = 0.02f;
    [SerializeField] private float shakeDuration = 0.08f;

    [Header("Tile transition")]
    [Tooltip("TerrainTop's twin with Alpha Clipping ticked, sharing its atlas")]
    [SerializeField] private Material tileTransitionMaterial;
    [SerializeField] private float tileTransitionDuration = 0.45f;
    [SerializeField] private float tileTransitionBlockDuration = 0.14f;
    [SerializeField] private float tileTransitionJitter = 0.08f;

    private const float RIPPLE_OFFSET = 0.002f;
    private const float RIPPLE_START_SCALE = 0.2f;


    private GameItem dragging;
    private GameObject ghost;

    private readonly List<GameItem> residents = new List<GameItem>();

    private bool warnedAboutMissingCursor;
    private bool warnedAboutMissingSound;
    private bool warnedAboutMissingRipple;
    private bool warnedAboutMissingTransition;
    private bool warnedAboutTransitionAtlas;
    private Transform ghostStaging;

    private Vector3Int previewCell;
    private bool hasPreview;

    private readonly List<Vector4> transitionRects = new List<Vector4>();

    // tile transitions still playing, so a puzzle is not judged while the ground is changing
    private int presentationsInFlight;


    // what a growth or a reagent drop hands to its animation, measured before the old entity went
    private struct GrowthPop
    {
        public GameItem from;
        public bool hasOldHeight;
        public float oldHeight;
        public float delay;
        public bool withSound;

        // the player's own reagent drop, rather than something growing while they watch: it shakes the
        // camera, where a growth never does. Its sound and the item it spends travel as the announce's
        // source instead
        public bool shake;
    }


    // Raised once a placement is fully committed and saved
    public event Action<GameItem, Vector3Int, GameObject> ItemPlaced;


    // Raised once a placement has finished being shown: after its tile transition, or straight away
    // when nothing was worth waiting for. A growth and a silent load catch-up raise it too, so a rule
    // that hears this instead of ItemPlaced never misses a placement
    public event Action<GameItem, Vector3Int> ItemShown;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one PlacementController in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (terrainManager == null) terrainManager = TerrainManager.Instance;
        if (entityManager == null) entityManager = EntityManager.Instance;
        if (viewCamera == null) viewCamera = Camera.main;
        if (cameraController == null && viewCamera != null) cameraController = viewCamera.GetComponent<CameraController>();

        if (terrainManager == null) Debug.LogError("PlacementController: no TerrainManager, so a drop has no cell to land in", this);
        if (entityManager == null) Debug.LogError("PlacementController: no EntityManager, so a prefab item cannot be placed", this);
        if (viewCamera == null) Debug.LogError("PlacementController: no camera, so a drop cannot be raycast", this);

        if (registry == null)
        {
            Debug.LogError("PlacementController: no GameItemRegistry, so a reagent cannot be told from an " +
                "item with nothing to place, and every reagent drag is refused", this);
        }

        if (terrainMask.value == 0)
        {
            Debug.LogError("PlacementController: terrainMask is empty, so no drop can ever land on " +
                "anything. Set it to the terrain layers.", this);
        }
    }


    private void Start()
    {
        if (inventoryManager == null) inventoryManager = InventoryManager.Instance;

        if (inventoryManager == null)
        {
            Debug.LogError("PlacementController: no InventoryManager, so no slot can start a placement", this);
            return;
        }

        SubscribeToSlots();
    }


    private void OnDisable()
    {
        CancelDrag();
    }


    private void OnDestroy()
    {
        UnsubscribeFromSlots();
    }


    // MARK: slot wiring


    private void SubscribeToSlots()
    {
        IReadOnlyList<ItemInventorySlot> slots = inventoryManager.Slots;
        if (slots == null) return;

        for (int i = 0; i < slots.Count; i++)
        {
            ItemInventorySlot slot = slots[i];
            if (slot == null) continue;

            slot.DragBegan += OnSlotDragBegan;
            slot.Dragged += OnSlotDragged;
            slot.DragEnded += OnSlotDragEnded;
        }
    }


    private void UnsubscribeFromSlots()
    {
        if (inventoryManager == null) return;

        IReadOnlyList<ItemInventorySlot> slots = inventoryManager.Slots;
        if (slots == null) return;

        for (int i = 0; i < slots.Count; i++)
        {
            ItemInventorySlot slot = slots[i];
            if (slot == null) continue;

            slot.DragBegan -= OnSlotDragBegan;
            slot.Dragged -= OnSlotDragged;
            slot.DragEnded -= OnSlotDragEnded;
        }
    }


    private void OnSlotDragBegan(ItemInventorySlot slot, PointerEventData eventData)
    {
        if (cameraController != null) cameraController.CancelPan();

        BeginDrag(slot);
    }


    private void OnSlotDragged(ItemInventorySlot slot, PointerEventData eventData)
    {
        UpdateDrag(eventData.position);
    }


    private void OnSlotDragEnded(ItemInventorySlot slot, PointerEventData eventData)
    {
        EndDrag(eventData.position);
    }


    // MARK: drag


    public bool BeginDrag(ItemInventorySlot slot)
    {
        GameItem item = slot != null ? slot.Item : null;
        if (item == null) return false;

        if (item.IsReagentShape && (registry == null || !registry.IsReagent(item)))
        {
            Debug.LogError($"PlacementController: '{item.name}' has neither a tile nor a prefab, and no item " +
                $"reacts to it, so there is nothing to place", item);
            return false;
        }

        if (inventoryManager == null || inventoryManager.GetCount(item) <= 0) return false;

        // a drag should never overlap another one, but if it does the old preview has to go
        if (dragging != null) CancelDrag();

        dragging = item;
        CreateGhost();

        // the drag owns the cursor from here, so a hovered slot or button can't show the hand
        ShowCursor(CursorType.Default);

        return true;
    }


    public void UpdateDrag(Vector2 screenPosition)
    {
        if (dragging == null) return;

        bool valid = TryResolveTarget(screenPosition, out Vector3Int cell);

        // show that placement is disallowed via the cursor
        ShowCursor(valid ? CursorType.Default : CursorType.Disabled);

        if (!valid)
        {
            ClearPreview();
            return;
        }

        if (hasPreview && cell == previewCell) return;

        ClearPreview();
        ShowPreview(cell);
    }


    public bool EndDrag(Vector2 screenPosition)
    {
        if (dragging == null) return false;

        GameItem item = dragging;
        GameObject instance = null;
        Vector3Int cell = default;
        TerrainTile previousTile = null;
        bool placed = false;

        bool resolved = TryResolveTarget(screenPosition, out cell);

        // Everything a commit needs is captured by now, so the drag lets go of the cursor and of its
        // preview before the world changes. A failure below leaves it released, exactly as a refused
        // drop always has
        CancelDrag();

        if (!resolved) return false;

        // A reagent has neither a tile nor a prefab, so nothing is placed at the cell: the entity
        // standing there becomes the reaction's result instead. Replace consumes the reagent, so
        // nothing after this may consume it again
        if (item.IsReagentShape)
        {
            return ResolveReaction(item, cell);
        }

        placed = Commit(item, cell, default, out instance, out previousTile);

        if (!placed) return false;

        Announce(item, cell, instance, previousTile, true, PlaceFeel.Player, default);

        return true;
    }


    // MARK: commit


    public void CancelDrag()
    {
        // Only a live drag owns the cursor. The guard matters on teardown, where this runs after the
        // CursorManager has already been destroyed and would warn about a manager nothing needed.
        if (dragging != null) ReleaseCursor();

        ClearPreview();

        if (ghost != null) Destroy(ghost);
        ghost = null;

        dragging = null;
    }


    private void ShowCursor(CursorType cursorType)
    {
        if (CursorManager.Instance == null)
        {
            if (warnedAboutMissingCursor) return;

            warnedAboutMissingCursor = true;
            Debug.LogWarning("PlacementController: no CursorManager in the scene", this);
            return;
        }

        CursorManager.Instance.SetOverride(cursorType);
    }


    // hands the cursor back to hover/default
    private void ReleaseCursor()
    {
        if (CursorManager.Instance == null) return;

        CursorManager.Instance.ClearOverride();
    }


    // MARK: target


    // Everything that makes a placement invalid is checked here
    // Commit therefore always assumes the placement is valid
    private bool TryResolveTarget(Vector2 screenPosition, out Vector3Int cell)
    {
        cell = default;

        if (dragging == null || terrainManager == null || viewCamera == null) return false;

        // Releasing over the inventory panel will cancel the placement
        if (PointerRaycastManager.IsPointerOverUI(screenPosition)) return false;

        // the hit is cached by the manager, but the ray direction is still needed below
        // to resolve which top cell a wall face belongs to
        Ray ray = viewCamera.ScreenPointToRay(screenPosition);

        if (!PointerRaycastManager.TryGetHit(screenPosition, terrainMask, dropRayDistance, QueryTriggerInteraction.Ignore, out RaycastHit hit)) return false;

        // Only the top of a column is ever a target
        // a wall face resolves to the tile that produced it
        if (!terrainManager.TryGetTopCellAt(hit.point, ray.direction, out cell)) return false;

        return CanPlace(dragging, cell);
    }


    // Whether item can go into cell, by the world's rules alone (no pointer, no inventory). replacing
    // is an entity at the cell that is about to make way for it, so it neither occupies the cell nor
    // gets a say over the ground
    public bool CanPlace(GameItem item, Vector3Int cell, EntityId replacing = null)
    {
        if (item == null || terrainManager == null) return false;

        TerrainTile target = terrainManager.GetTile(cell);
        if (target == null) return false;

        // An item with neither a tile nor a prefab has nothing of its own to place, so the only drop it
        // allows is a reagent on an entity that reacts to it. The reaction's result is what has to fit
        // the cell, tested as if it were replacing that entity, so the entity itself doesn't occupy the
        // cell and doesn't get a say over the ground
        if (item.IsReagentShape)
        {
            if (registry == null) return false;

            if (!TryResolveReactionTarget(item, cell, out EntityManager.LiveEntity reacting)) return false;

            GameItem becomes = reacting.item.ReactionInto(item);

            return becomes != null && CanPlace(becomes, cell, replacing: reacting.entityId);
        }

        // already that tile, cannot place
        if (item.Tile != null && target == item.Tile) return false;

        // already an entity at that location
        if (item.Prefab != null && entityManager != null && entityManager.HasEntityAt(cell, replacing)) return false;

        // Changing the ground must not invalidate the placement rules of entities
        // already at that cell
        if (!ResidentsAllow(item.Tile, cell, replacing)) return false;

        return item.IsPlaceableOn(target);
    }


    // Check if an entity already at this tile will allow the tile to change
    private bool ResidentsAllow(TerrainTile newTile, Vector3Int cell, EntityId replacing)
    {
        if (newTile == null || entityManager == null) return true;

        entityManager.CollectEntitiesAt(cell, residents, replacing);

        for (int i = 0; i < residents.Count; i++)
        {
            if (residents[i].IsPlaceableOn(newTile)) continue;

            return false;
        }

        return true;
    }


    // MARK: commit


    // previousTile is what the cell held before a remap, for the tile transition. It comes from the
    // state, which a drag preview never touches, so it's the real old tile and not the previewed one.
    // spawn is default for a drop; a growth carries its guid, fixture flag and stage stamp through it
    private bool Commit(GameItem item, Vector3Int cell, EntityManager.SpawnInfo spawn, out GameObject instance, out TerrainTile previousTile)
    {
        instance = null;
        previousTile = null;

        // Tile first, because it can fail but spawn cannot
        if (item.Tile != null)
        {
            if (terrainManager == null) return false;

            previousTile = terrainManager.GetTile(cell);

            if (!terrainManager.Remap(cell, item.Tile)) return false;
        }

        if (item.Prefab != null && entityManager != null)
        {
            instance = entityManager.Spawn(item, cell, spawn);

            if (instance == null)
            {
                Debug.LogError($"PlacementController: '{item.name}' remapped the terrain but its entity could not be spawned", this);
            }
        }

        return true;
    }


    // Everything after a commit, shared by every placement so a drop and a growth can't drift apart:
    // consume (a drop only), save, tell the world, then show it.
    //
    // source is the item the placement came from, when it isn't the one being placed: a reagent drop
    // places `item` but spends and sounds like the reagent. Null means item
    private void Announce(GameItem item, Vector3Int cell, GameObject instance, TerrainTile previousTile,
        bool fromInventory, PlaceFeel feel, GrowthPop pop, GameItem source = null)
    {
        if (fromInventory)
        {
            GameItem spend = source != null ? source : item;

            // We have to consume last because the slot will deactivate when count is 0
            if (!inventoryManager.TryRemove(spend, 1))
            {
                Debug.LogError($"PlacementController: placed '{item.name}' but could not consume '{spend.name}'", this);
            }
        }

        FlushSave();

        ItemPlaced?.Invoke(item, cell, instance);

        TileTransition transition = null;

        switch (feel)
        {
            case PlaceFeel.Player: transition = PlayPlacementAnimation(item, cell, instance, previousTile, source); break;
            case PlaceFeel.Growth: PlayGrowthAnimation(item, cell, instance, pop); break;

            // Silent: it's simply there
        }

        // Only a real ground change is worth waiting for. A growth has no tile to grow, and a skipped
        // transition is over before it began, so both are shown at once
        if (transition == null)
        {
            ItemShown?.Invoke(item, cell);
            return;
        }

        // in flight until the cover says it has finished, which is also when listeners hear ItemShown.
        // A scene unload kills the cover mid-tween and leaves this counted; the controller dies with
        // that same scene, so nobody is left waiting on it
        presentationsInFlight++;

        transition.Finished += () => CompletePresentation(item, cell);
    }


    // The cover is gone, so the ground finally shows what the world already holds. The count drops
    // first, so a soft-lock judgement queued from ItemShown sees the ground as settled
    private void CompletePresentation(GameItem item, Vector3Int cell)
    {
        presentationsInFlight = Mathf.Max(0, presentationsInFlight - 1);

        ItemShown?.Invoke(item, cell);
    }


    private void FlushSave()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
    }


    // MARK: replace


    // Swaps a live entity for another item in the same cell, through the same commit and announce as a
    // drop: one save holds both the removal and the addition, and the world hears ItemPlaced for the
    // new item only. The new entity keeps the old one's guid (so puzzle credit and pings follow it) and
    // fixture flag, and is stamped with stageDay/stageCycleTime. False, with the old one untouched and
    // nothing consumed, when the new item can't go there right now.
    //
    // delay and withSound only shape a Growth feel: many growths on one moment stagger and share sounds.
    // consume and reagent are the reagent drop's: it spends one of reagent and shows itself as the
    // player's own placement, which is a shake the growth feel never gets
    public bool Replace(EntityId existing, GameItem into, int stageDay, float stageCycleTime, PlaceFeel feel,
        float delay = 0.0f, bool withSound = true, bool consume = false, GameItem reagent = null)
    {
        if (existing == null || into == null || entityManager == null) return false;

        // growing into a tile could fail its remap after the old entity is gone, so it is refused up front
        if (into.Prefab == null || into.Tile != null)
        {
            Debug.LogError($"PlacementController: '{into.name}' can't replace an entity: it needs a prefab and no tile", into);
            return false;
        }

        // A commit that cannot spend the reagent must not take the old entity with it: a drop that
        // placed nothing would still have eaten the fertiliser. Nothing else may consume it, so this
        // refusal is also the only place a reaction's consumption can be turned down. A warning, not an
        // error: the slot can empty during the drag, which is not a broken setup
        if (consume && (inventoryManager == null || !inventoryManager.Has(reagent)))
        {
            Debug.LogWarning($"PlacementController: refused to replace an entity for '{reagent?.name}': " +
                $"there is none left to consume", this);
            return false;
        }

        if (!entityManager.TryGetLive(existing, out EntityManager.LiveEntity old)) return false;

        if (!CanPlace(into, old.cell, existing)) return false;

        GrowthPop pop = new GrowthPop
        {
            from = old.item,
            delay = delay,
            withSound = withSound,
            shake = consume,
        };

        // measured while the old one is still whole: its height is where the new one starts. A reagent
        // drop is not height-matched: a flat ground sprite's height is its thickness, so it would pop
        // from a fraction of itself, and the base growth is what a player's own drop looks like anyway
        pop.hasOldHeight = !consume && TryMeasureSpriteHeight(old.gameObject, out pop.oldHeight);

        EntityManager.SpawnInfo spawn = new EntityManager.SpawnInfo
        {
            guid = existing.Guid,
            fixture = old.fixture,
            stageDay = stageDay,
            stageCycleTime = stageCycleTime,
        };

        entityManager.Despawn(existing);

        // no tile, so this can't fail; a spawn that does logs inside and the announce goes ahead, as a drop's would
        Commit(into, old.cell, spawn, out GameObject instance, out TerrainTile previousTile);

        // the reagent is what the placement spent, so it is what the announce consumes and sounds like
        Announce(into, old.cell, instance, previousTile, consume, feel, pop, consume ? reagent : null);

        return true;
    }


    // MARK: reaction


    // The entity a reagent drop would land on: the entity at the cell, when its item reacts to reagent.
    // A cell is one entity's, by the same assumption CanPlace and HasEntityAt make
    private bool TryResolveReactionTarget(GameItem reagent, Vector3Int cell, out EntityManager.LiveEntity target)
    {
        target = default;

        if (entityManager == null) return false;
        if (!entityManager.TryGetLiveAt(cell, out target)) return false;
        if (target.item == null) return false;

        // a reagent that nothing reacts to is not a reagent, so this is also what makes CanPlace refuse
        // a plain drop of an item with no tile and no prefab
        return target.item.ReactionInto(reagent) != null;
    }


    // Drops a reagent onto the entity that reacts to it. Replace does the whole placement, including
    // consuming the reagent, so this only decides whether it is worth trying at all
    private bool ResolveReaction(GameItem reagent, Vector3Int cell)
    {
        if (!TryResolveReactionTarget(reagent, cell, out EntityManager.LiveEntity target)) return false;

        GameItem into = target.item.ReactionInto(reagent);
        if (into == null) return false;

        // stage 0 re-stamps the new entity at the clock as it stands, as a fresh drop is stamped: the
        // sprout then ages from the moment it was fertilised
        return Replace(target.entityId, into, 0, 0.0f, PlaceFeel.Player,
            delay: 0.0f, withSound: true, consume: true, reagent: reagent);
    }


    // MARK: preview


    private void ShowPreview(Vector3Int cell)
    {
        previewCell = cell;
        hasPreview = true;

        if (dragging.Tile != null && terrainManager != null) terrainManager.PreviewCell(cell, dragging.Tile);

        if (ghost != null && terrainManager != null)
        {
            ghost.transform.position = terrainManager.CellToWorld(cell);
            ghost.SetActive(true);
        }
    }


    private void ClearPreview()
    {
        if (ghost != null) ghost.SetActive(false);

        if (!hasPreview) return;

        hasPreview = false;

        if (terrainManager != null) terrainManager.ClearPreview(previewCell);
    }


    private void CreateGhost()
    {
        if (dragging == null || dragging.Prefab == null) return;

        // Instantiated into an inactive parent on purpose, see GhostStaging
        ghost = Instantiate(dragging.Prefab, GhostStaging);
        ghost.name = dragging.Prefab.name + " (ghost)";

        PruneGhost(ghost);

        if (ghostMaterial == null)
        {
            Debug.LogWarning($"PlacementController: no ghostMaterial assigned, so the preview of " +
                $"'{dragging.name}' renders with the prefab's own material", this);
        }
        else
        {
            ApplyGhostMaterial(ghost);
        }

        ghost.transform.SetParent(null);

        ghost.SetActive(false);
    }


    // A preview has to be a picture and nothing more, so anything off the whitelist is destroyed
    private void PruneGhost(GameObject instance)
    {
        foreach (Component component in instance.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform) continue;
            if (ghostWhitelist.Contains(component.GetType())) continue;
            DestroyImmediate(component);
        }
    }


    // we don't want ghosts running Awake or Start methods either, so here's a fun solution
    // make an empty staging GO that's always inactive and instantiate ghosts onto it
    // before castrating their undesirable behaviours (lol that sounds so weird)
    private Transform GhostStaging
    {
        get
        {
            if (ghostStaging == null)
            {
                GameObject staging = new GameObject("Ghost Staging");
                staging.SetActive(false);

                ghostStaging = staging.transform;
            }

            return ghostStaging;
        }
    }


    private void ApplyGhostMaterial(GameObject instance)
    {
        bool warned = false;

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] original = renderer.sharedMaterials;

            // assigning an empty array would leave the renderer with no material at all
            if (original == null || original.Length == 0)
            {
                if (warned) continue;

                warned = true;
                Debug.LogWarning($"PlacementController: a renderer on the preview of '{dragging.name}' has no materials", this);
                continue;
            }

            Material[] ghosted = new Material[original.Length];
            for (int i = 0; i < ghosted.Length; i++) ghosted[i] = ghostMaterial;

            if (renderer is SpriteRenderer spriteRenderer)
            {
                Sprite sprite = spriteRenderer.sprite;

                spriteRenderer.sprite = null;
                spriteRenderer.sharedMaterials = ghosted;
                spriteRenderer.sprite = sprite;

                continue;
            }

            renderer.sharedMaterials = ghosted;
        }
    }


    // MARK: animation hook


    // Everything is committed by the time this runs: An animation started here
    // can therefore assume the world is already in its final state
    // instance is null for a tile-only item. source is the item the drop came from, for a reagent, and
    // null for a plain drop of item itself. Returns the ground change still playing, or null for
    // everything that has nothing left to wait for
    private TileTransition PlayPlacementAnimation(GameItem item, Vector3Int cell, GameObject instance, TerrainTile previousTile,
        GameItem source)
    {
        Vector3 worldPosition = terrainManager != null ? terrainManager.CellToWorld(cell) : Vector3.zero;

        if (instance != null) GrowFromBase(instance);

        TileTransition transition = null;

        if (item.Tile != null)
        {
            SpawnRipple(worldPosition);
            transition = PlayTileTransition(cell, previousTile);
        }

        // a reagent is what the player placed, so it is what they hear. The chain is the growth one,
        // with the reagent standing in for the old entity
        PlaySound(GrowthSound(source != null ? source : item, item), worldPosition);

        if (cameraController != null) cameraController.Shake(shakeMagnitude, shakeDuration);

        if (instance != null)
        {
            PlacementAnimation[] animations = instance.GetComponentsInChildren<PlacementAnimation>(true);

            for (int i = 0; i < animations.Length; i++) animations[i].Play();
        }

        return transition;
    }


    // Ensure that the root stays at the tile level
    private void GrowFromBase(GameObject instance, float delay = 0.0f)
    {
        Transform root = instance.transform;

        root.localScale = Vector3.zero;

        root.DOScale(Vector3.one * growOvershoot, growDuration)
            .SetEase(Ease.OutQuad)
            .SetDelay(delay)
            .SetLink(instance)
            .OnComplete(() => root.DOScale(Vector3.one, growSettleDuration).SetEase(Ease.InQuad).SetLink(instance));
    }


    // The old thing was destroyed this frame, so the new one takes its place at the old one's height
    // and pops to its own: a seed swells into a flower, and a big old thing settles into a smaller one,
    // all the same way. Then the ripple and the sound, but no shake: the player didn't do this - a
    // reagent drop did, and that one shakes
    private void PlayGrowthAnimation(GameItem item, Vector3Int cell, GameObject instance, GrowthPop pop)
    {
        Vector3 worldPosition = terrainManager != null ? terrainManager.CellToWorld(cell) : Vector3.zero;

        if (instance != null)
        {
            if (pop.hasOldHeight && TryMeasureSpriteHeight(instance, out float newHeight))
            {
                Transform root = instance.transform;

                root.localScale = Vector3.one * Mathf.Clamp(pop.oldHeight / newHeight, growthPopMinScale, growthPopMaxScale);

                root.DOScale(Vector3.one, growthPopDuration)
                    .SetEase(Ease.OutBack, growthPopOvershoot)
                    .SetDelay(pop.delay)
                    .SetLink(instance);
            }
            else
            {
                GrowFromBase(instance, pop.delay);
            }
        }

        // the rest lands with the pop, which a stagger may hold back
        TweenCallback flourish = () =>
        {
            SpawnRipple(worldPosition);

            if (pop.withSound) PlaySound(GrowthSound(pop.from, item), worldPosition);

            if (pop.shake && cameraController != null) cameraController.Shake(shakeMagnitude, shakeDuration);

            if (instance == null) return;

            PlacementAnimation[] animations = instance.GetComponentsInChildren<PlacementAnimation>(true);

            for (int i = 0; i < animations.Length; i++) animations[i].Play();
        };

        if (pop.delay <= 0.0f || instance == null)
        {
            flourish();
            return;
        }

        DOVirtual.DelayedCall(pop.delay, flourish, false).SetLink(instance);
    }


    // the source's own grow sound, then the grown item's place sound, then the default. A reagent drop
    // passes the reagent as `from`, so its place sound leads the same chain
    private SoundObject GrowthSound(GameItem from, GameItem into)
    {
        if (from != null && from.GrowSound != null) return from.GrowSound;

        return into.PlaceSound != null ? into.PlaceSound : defaultPlaceSound;
    }


    // The tallest sprite in the hierarchy, in world units: sprite height x lossy scale. Not
    // renderer.bounds, because BillboardSprite tilts sprites toward the camera, so world bounds change
    // with the camera angle
    private static bool TryMeasureSpriteHeight(GameObject root, out float height)
    {
        height = 0.0f;
        if (root == null) return false;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(false);

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer candidate = renderers[i];
            if (candidate == null || !candidate.enabled || candidate.sprite == null) continue;

            float local = candidate.drawMode == SpriteDrawMode.Simple ? candidate.sprite.bounds.size.y : candidate.size.y;

            height = Mathf.Max(height, local * Mathf.Abs(candidate.transform.lossyScale.y));
        }

        return height > 0.0f;
    }


    // A one shot quad at the cell that scales out and fades
    private void SpawnRipple(Vector3 worldPosition)
    {
        if (rippleSprite == null)
        {
            WarnOnce(ref warnedAboutMissingRipple, "no rippleSprite assigned");
            return;
        }

        GameObject ripple = new GameObject("Placement Ripple");
        ripple.transform.position = worldPosition + Vector3.up * RIPPLE_OFFSET;

        ripple.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

        SpriteRenderer rippleRenderer = ripple.AddComponent<SpriteRenderer>();
        rippleRenderer.sprite = rippleSprite;
        rippleRenderer.color = rippleColour;

        if (rippleMaterial != null) rippleRenderer.sharedMaterial = rippleMaterial;

        ripple.transform.localScale = Vector3.one * (rippleDiameter * RIPPLE_START_SCALE);

        ripple.transform.DOScale(Vector3.one * rippleDiameter, rippleDuration).SetEase(Ease.OutQuad);
        rippleRenderer.DOFade(0.0f, rippleDuration).SetEase(Ease.InQuad).OnComplete(() => Destroy(ripple));
    }


    // Covers a cell that has JUST been remapped with a picture of the tile it held before, which the
    // new one then grows over. Call it after TerrainManager.Remap, from anything that changes a tile:
    // the new tile is read back from the terrain, so it works for any old -> new pair. Returns the
    // cover still playing, or null when there is nothing to wait for
    public TileTransition PlayTileTransition(Vector3Int cell, TerrainTile from)
    {
        if (terrainManager == null || from == null) return null;

        TerrainTile to = terrainManager.GetTile(cell);
        if (to == null || to == from) return null;

        if (tileTransitionMaterial == null)
        {
            WarnOnce(ref warnedAboutMissingTransition, "no tileTransitionMaterial assigned, so tile changes pop instead of transitioning");
            return null;
        }

        Sprite oldSprite = terrainManager.ResolveCellSprite(cell, from);
        Sprite newSprite = terrainManager.ResolveCellSprite(cell, to);
        if (oldSprite == null || newSprite == null) return null;

        // one shared material means one atlas; a sprite from anywhere else would draw the wrong texels
        if (!IsOnTransitionAtlas(from, oldSprite) || !IsOnTransitionAtlas(to, newSprite)) return null;

        // no baked top quads means nothing on screen to cover (e.g. a stale bake)
        if (!terrainManager.TryGetTopTexelRects(cell, transitionRects, out int pixelsPerTile)) return null;

        Vector3 cellOrigin = new Vector3(cell.x, terrainManager.GetLevelHeight(cell.y), cell.z);

        return TileTransition.Spawn(cellOrigin, oldSprite, newSprite, transitionRects, pixelsPerTile, tileTransitionMaterial,
            tileTransitionDuration, tileTransitionBlockDuration, tileTransitionJitter);
    }


    private bool IsOnTransitionAtlas(TerrainTile tile, Sprite sprite)
    {
        if (sprite.texture == tileTransitionMaterial.mainTexture) return true;

        if (!warnedAboutTransitionAtlas)
        {
            warnedAboutTransitionAtlas = true;
            Debug.LogError($"PlacementController: '{tile.name}' sprite '{sprite.name}' is on " +
                $"'{sprite.texture.name}', not the transition material's atlas, so its transitions " +
                $"are skipped. Move the art into the terrain atlas", tile);
        }

        return false;
    }


    private void PlayPlaceSound(GameItem item, Vector3 worldPosition)
    {
        PlaySound(item.PlaceSound != null ? item.PlaceSound : defaultPlaceSound, worldPosition);
    }


    private void PlaySound(SoundObject sound, Vector3 worldPosition)
    {
        if (sound == null)
        {
            WarnOnce(ref warnedAboutMissingSound, "no placement sound assigned");
            return;
        }

        if (AudioManager.Instance == null) return;

        AudioManager.Instance.PlaySFX(sound, worldPosition);
    }


    private void WarnOnce(ref bool warned, string message)
    {
        if (warned) return;

        warned = true;
        Debug.LogWarning($"PlacementController: {message}", this);
    }
}
