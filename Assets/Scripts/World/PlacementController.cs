using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;


// Turns a drag out of an inventory slot into a terrain remap and/or a spawned entity
public class PlacementController : MonoBehaviour
{
    private static PlacementController instance;
    public static PlacementController Instance { get { return instance; } }


    [SerializeField] private TerrainGrid terrainGrid;
    [SerializeField] private EntityManager entityManager;
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private Camera viewCamera;

    [Header("Drop target")]
    [SerializeField] private LayerMask terrainMask;
    [SerializeField] private float dropRayDistance = 1000.0f;

    [Header("Preview")]
    [SerializeField] private Material ghostMaterial;


    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
    private PointerEventData uiRaycastPointer;
    private EventSystem uiRaycastSystem;

    private GameItem dragging;
    private GameObject ghost;

    private readonly List<GameItem> residents = new List<GameItem>();

    private bool warnedAboutMissingCursor;

    private Vector3Int previewCell;
    private bool hasPreview;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one PlacementController in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (terrainGrid == null) terrainGrid = GetComponent<TerrainGrid>();
        if (entityManager == null) entityManager = GetComponent<EntityManager>();
        if (viewCamera == null) viewCamera = Camera.main;

        if (terrainGrid == null) Debug.LogError("PlacementController: no TerrainGrid, so a drop has no cell to land in", this);
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
        bool placed = false;

        if (TryResolveTarget(screenPosition, out cell))
        {
            placed = Commit(item, cell, out instance);
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

        PlayPlacementAnimation(item, cell, instance);

        return true;
    }


    public void CancelDrag()
    {
        ShowCursor(CursorType.Default);

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

        CursorManager.Instance.SetCursor(cursorType);
    }


    // MARK: target


    // Everything that makes a placement invalid is checked here
    // Commit therefore always assumes the placement is valid
    private bool TryResolveTarget(Vector2 screenPosition, out Vector3Int cell)
    {
        cell = default;

        if (dragging == null || terrainGrid == null || viewCamera == null) return false;

        // Releasing over the inventory panel will cancel the placement
        if (IsPointerOverUI(screenPosition)) return false;

        Ray ray = viewCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, dropRayDistance, terrainMask, QueryTriggerInteraction.Ignore)) return false;

        // Only the top of a column is ever a target
        // a wall face resolves to the tile that produced it
        if (!terrainGrid.TryGetTopCellAt(hit.point, ray.direction, out cell)) return false;

        TerrainTile target = terrainGrid.GetTile(cell);
        if (target == null) return false;

        // already that tile, cannot place
        if (dragging.Tile != null && target == dragging.Tile) return false;

        // already an entity at that location
        if (dragging.Prefab != null && entityManager != null && entityManager.HasEntityAt(dragging, cell)) return false;

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


    private bool Commit(GameItem item, Vector3Int cell, out GameObject instance)
    {
        instance = null;

        // Tile first, because it can fail but spawn cannot
        if (item.Tile != null)
        {
            if (terrainGrid == null || !terrainGrid.Remap(cell, item.Tile)) return false;
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

        if (dragging.Tile != null && terrainGrid != null) terrainGrid.PreviewCell(cell, dragging.Tile);

        if (ghost != null && terrainGrid != null)
        {
            ghost.transform.position = terrainGrid.CellToWorld(cell);
            ghost.SetActive(true);
        }
    }


    private void ClearPreview()
    {
        if (ghost != null) ghost.SetActive(false);

        if (!hasPreview) return;

        hasPreview = false;

        if (terrainGrid != null) terrainGrid.ClearPreview(previewCell);
    }


    private void CreateGhost()
    {
        if (dragging == null || dragging.Prefab == null) return;

        ghost = Instantiate(dragging.Prefab);
        ghost.name = dragging.Prefab.name + " (ghost)";

        // a preview must not answer the drop raycast, and must not be mistaken for a real entity
        foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

        if (ghostMaterial == null)
        {
            Debug.LogWarning($"PlacementController: no ghostMaterial assigned, so the preview of " +
                $"'{dragging.name}' renders with the prefab's own material", this);
        }
        else
        {
            ApplyGhostMaterial(ghost);
        }

        ghost.SetActive(false);
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


    // Reached only from a player placement, never from EntityManager's load path, so loading a save
    // does not replay every spawn animation.
    //
    // Everything is committed by the time this runs: the tile is remapped, the instance is live, the
    // entity is in the save, the item is consumed and the file is flushed. An animation started here
    // can therefore assume the world is already in its final state and owes nobody a callback when
    // it finishes - if it needs to hide the instance first, it does that to itself.
    //
    // instance is null for a tile-only item.
    private void PlayPlacementAnimation(GameItem item, Vector3Int cell, GameObject instance)
    {
        // TODO: play the placement animation for `item` at `cell` on `instance`
    }


    // MARK: UI hit test


    // Live UI hit test at a screen position
    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        if (uiRaycastPointer == null || uiRaycastSystem != eventSystem)
        {
            uiRaycastSystem = eventSystem;
            uiRaycastPointer = new PointerEventData(eventSystem);
        }

        uiRaycastPointer.position = screenPosition;

        // RaycastAll clears the results list itself
        eventSystem.RaycastAll(uiRaycastPointer, uiRaycastResults);

        return uiRaycastResults.Count > 0;
    }
}
