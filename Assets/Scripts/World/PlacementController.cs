using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;


// Turns a drag out of an inventory slot into a terrain remap and/or a spawned entity
public class PlacementController : MonoBehaviour
{
    private static PlacementController instance;
    public static PlacementController Instance { get { return instance; } }

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


    // Raised once a placement is fully committed and saved
    public event Action<GameItem, Vector3Int, GameObject> ItemPlaced;


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

        if (item.Tile == null && item.Prefab == null)
        {
            Debug.LogError($"PlacementController: '{item.name}' has neither a tile nor a prefab, so there is nothing to place", item);
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

        if (TryResolveTarget(screenPosition, out cell))
        {
            placed = Commit(item, cell, out instance, out previousTile);
        }

        // clears the tile patch and destroys the ghost
        CancelDrag();

        if (!placed) return false;

        // We have to consume last because the slot will deactivate when count is 0
        if (!inventoryManager.TryRemove(item, 1))
        {
            Debug.LogError($"PlacementController: placed '{item.name}' but could not consume it", this);
        }

        FlushSave();

        ItemPlaced?.Invoke(item, cell, instance);

        PlayPlacementAnimation(item, cell, instance, previousTile);

        return true;
    }


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

        TerrainTile target = terrainManager.GetTile(cell);
        if (target == null) return false;

        // already that tile, cannot place
        if (dragging.Tile != null && target == dragging.Tile) return false;

        // already an entity at that location
        if (dragging.Prefab != null && entityManager != null && entityManager.HasEntityAt(cell)) return false;

        // Changing the ground must not invalidate the placement rules of entities
        // already at that cell
        if (!ResidentsAllow(dragging.Tile, cell)) return false;

        return dragging.IsPlaceableOn(target);
    }


    // Check if an entity already at this tile will allow the tile to change
    private bool ResidentsAllow(TerrainTile newTile, Vector3Int cell)
    {
        if (newTile == null || entityManager == null) return true;

        entityManager.CollectEntitiesAt(cell, residents);

        for (int i = 0; i < residents.Count; i++)
        {
            if (residents[i].IsPlaceableOn(newTile)) continue;

            return false;
        }

        return true;
    }


    // MARK: commit


    // previousTile is what the cell held before a remap, for the tile transition. It comes from the
    // state, which a drag preview never touches, so it's the real old tile and not the previewed one
    private bool Commit(GameItem item, Vector3Int cell, out GameObject instance, out TerrainTile previousTile)
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
            instance = entityManager.Spawn(item, cell);

            if (instance == null)
            {
                Debug.LogError($"PlacementController: '{item.name}' remapped the terrain but its entity could not be spawned", this);
            }
        }

        return true;
    }


    private void FlushSave()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
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
    // instance is null for a tile-only item.
    private void PlayPlacementAnimation(GameItem item, Vector3Int cell, GameObject instance, TerrainTile previousTile)
    {
        Vector3 worldPosition = terrainManager != null ? terrainManager.CellToWorld(cell) : Vector3.zero;

        if (instance != null) GrowFromBase(instance);

        if (item.Tile != null)
        {
            SpawnRipple(worldPosition);
            PlayTileTransition(cell, previousTile);
        }

        PlayPlaceSound(item, worldPosition);

        if (cameraController != null) cameraController.Shake(shakeMagnitude, shakeDuration);

        if (instance != null)
        {
            PlacementAnimation[] animations = instance.GetComponentsInChildren<PlacementAnimation>(true);

            for (int i = 0; i < animations.Length; i++) animations[i].Play();
        }
    }


    // Ensure that the root stays at the tile level
    private void GrowFromBase(GameObject instance)
    {
        Transform root = instance.transform;

        root.localScale = Vector3.zero;

        root.DOScale(Vector3.one * growOvershoot, growDuration)
            .SetEase(Ease.OutQuad)
            .OnComplete(() => root.DOScale(Vector3.one, growSettleDuration).SetEase(Ease.InQuad));
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
    // the new tile is read back from the terrain, so it works for any old -> new pair
    public void PlayTileTransition(Vector3Int cell, TerrainTile from)
    {
        if (terrainManager == null || from == null) return;

        TerrainTile to = terrainManager.GetTile(cell);
        if (to == null || to == from) return;

        if (tileTransitionMaterial == null)
        {
            WarnOnce(ref warnedAboutMissingTransition, "no tileTransitionMaterial assigned, so tile changes pop instead of transitioning");
            return;
        }

        Sprite oldSprite = terrainManager.ResolveCellSprite(cell, from);
        Sprite newSprite = terrainManager.ResolveCellSprite(cell, to);
        if (oldSprite == null || newSprite == null) return;

        // one shared material means one atlas; a sprite from anywhere else would draw the wrong texels
        if (!IsOnTransitionAtlas(from, oldSprite) || !IsOnTransitionAtlas(to, newSprite)) return;

        // no baked top quads means nothing on screen to cover (e.g. a stale bake)
        if (!terrainManager.TryGetTopTexelRects(cell, transitionRects, out int pixelsPerTile)) return;

        Vector3 cellOrigin = new Vector3(cell.x, terrainManager.GetLevelHeight(cell.y), cell.z);

        TileTransition.Spawn(cellOrigin, oldSprite, newSprite, transitionRects, pixelsPerTile, tileTransitionMaterial,
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
        SoundObject sound = item.PlaceSound != null ? item.PlaceSound : defaultPlaceSound;

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
