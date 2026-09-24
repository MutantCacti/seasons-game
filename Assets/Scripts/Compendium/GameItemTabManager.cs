using System.Collections.Generic;
using UnityEngine;


// The Game Items tab: registry items, three per page, with undiscovered items greyed out and
// newly discovered items pulsing with an outline until the player hovers over them.
public class GameItemTabManager : CompendiumTabManager
{
    private const int ENTRIES_PER_PAGE = 3;

    private static readonly Color SILHOUETTE_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);


    [SerializeField] private GameItemRegistry registry;
    [SerializeField] private GameObject pagePrefab;
    [SerializeField] private Shader silhouetteShader;
    [SerializeField] private Color discoveredColour = new Color(237.0f / 255.0f, 196.0f / 255.0f, 59.0f / 255.0f, 1.0f);  // #EDC43B

    private Material silhouetteMaterial;

    // runtime-only presence tracking, never saved
    private readonly HashSet<GameItem> present = new HashSet<GameItem>();
    private readonly HashSet<GameItem> unseen = new HashSet<GameItem>();
    private bool baselineEmpty;


    public override GameObject PagePrefab { get { return pagePrefab; } }


    public override int PageCount
    {
        get { return (Count + ENTRIES_PER_PAGE - 1) / ENTRIES_PER_PAGE; }
    }


    private void Awake()
    {
        if (registry == null) Debug.LogError("GameItemTabManager: no GameItemRegistry, so the tab has nothing to show", this);
        if (pagePrefab == null) Debug.LogError("GameItemTabManager: no page prefab, so the tab cannot render pages", this);
        if (silhouetteShader == null) Debug.LogError("GameItemTabManager: no silhouette shader, so undiscovered items render normally instead of greyed out", this);

        // Captured now because EntityManager clears IsConstructingNewWorld later this same frame,
        // once it has finished seeding the world fixtures (this compendium included).
        baselineEmpty = SaveDataManager.Instance != null && SaveDataManager.Instance.IsConstructingNewWorld;
    }


    private void Start()
    {
        SubscribeToEvents();

        // A new world starts from an empty baseline, so everything the player begins with is
        // unseen. A loaded world treats its first snapshot as already seen.
        if (baselineEmpty) RecomputePresence();
        else TakeBaselineSnapshot();
    }


    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }


    // MARK: registry


    public int Count { get { return registry != null ? registry.Count : 0; } }


    // False for a null registry, an out of range index or a hole in the list
    public bool TryGetItem(int index, out GameItem item)
    {
        item = null;

        if (registry == null) return false;
        if (index < 0 || index >= registry.Count) return false;

        item = registry.Items[index];

        return item != null;
    }


    // MARK: binding


    public override void BindPage(GameObject page, int pageIndex)
    {
        GameItemPageContainer container = page != null ? page.GetComponent<GameItemPageContainer>() : null;
        if (container == null) return;

        CompendiumEntry[] entries = container.Entries;
        if (entries == null) return;

        int count = Count;
        int first = pageIndex * ENTRIES_PER_PAGE;
        bool hasContent = pageIndex >= 0 && first < count;

        for (int i = 0; i < entries.Length; i++)
        {
            CompendiumEntry entry = entries[i];
            if (entry == null) continue;

            GameItem item = null;
            if (hasContent) TryGetItem(first + i, out item);

            BindEntry(entry, item);
            entry.gameObject.SetActive(item != null);
        }
    }


    private void BindEntry(CompendiumEntry entry, GameItem item)
    {
        if (item == null)
        {
            entry.Clear();
            HideHighlight(entry);
            return;
        }

        if (IsDiscovered(item))
        {
            entry.SetItem(item);

            if (unseen.Contains(item)) ShowHighlight(entry, item);
            else HideHighlight(entry);

            return;
        }

        entry.SetImage(item.Icon);
        entry.SetIconMaterial(SilhouetteMaterial);
        entry.SetNameText("Undiscovered");
        entry.SetDescriptionText(string.Empty);
        HideHighlight(entry);
    }


    private void ShowHighlight(CompendiumEntry entry, GameItem item)
    {
        UnseenHighlight highlight = entry.GetComponent<UnseenHighlight>();
        if (highlight == null) return;

        highlight.Show(discoveredColour, () => unseen.Remove(item));
    }


    private void HideHighlight(CompendiumEntry entry)
    {
        UnseenHighlight highlight = entry.GetComponent<UnseenHighlight>();
        if (highlight != null) highlight.Hide();
    }


    // An item is discovered if it is currently present: held in the inventory, standing in
    // the world as a live entity, or remapped onto at least one terrain cell.
    private bool IsDiscovered(GameItem item)
    {
        if (InventoryManager.Instance != null && InventoryManager.Instance.GetCount(item) > 0) return true;
        if (EntityManager.Instance != null && EntityManager.Instance.HasEntityOf(item)) return true;
        if (item.Tile != null && TerrainManager.Instance != null && TerrainManager.Instance.CountTile(item.Tile) > 0) return true;

        return false;
    }


    // MARK: presence


    private void SubscribeToEvents()
    {
        if (InventoryManager.Instance != null) InventoryManager.Instance.SlotsChanged += OnSlotsChanged;
        if (PlacementController.Instance != null) PlacementController.Instance.ItemPlaced += OnItemPlaced;
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.OnPuzzleUnlocked += OnPuzzleUnlocked;
    }


    private void UnsubscribeFromEvents()
    {
        if (InventoryManager.Instance != null) InventoryManager.Instance.SlotsChanged -= OnSlotsChanged;
        if (PlacementController.Instance != null) PlacementController.Instance.ItemPlaced -= OnItemPlaced;
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.OnPuzzleUnlocked -= OnPuzzleUnlocked;
    }


    private void OnSlotsChanged()
    {
        RecomputePresence();
    }


    private void OnItemPlaced(GameItem item, Vector3Int cell, GameObject instance)
    {
        RecomputePresence();
    }


    private void OnPuzzleUnlocked(Puzzle puzzle)
    {
        RecomputePresence();
    }


    // Diffs the live world against the last known snapshot, marking every absent -> present
    // transition as unseen. Absent items drop out of unseen so a later re-discovery pulses again.
    private void RecomputePresence()
    {
        IReadOnlyList<GameItem> items = registry != null ? registry.Items : null;
        if (items == null) return;

        bool changed = false;

        for (int i = 0; i < items.Count; i++)
        {
            GameItem item = items[i];
            if (item == null) continue;

            if (IsDiscovered(item))
            {
                if (present.Add(item))
                {
                    unseen.Add(item);
                    changed = true;
                }
            }
            else
            {
                if (present.Remove(item)) changed = true;
                if (unseen.Remove(item)) changed = true;
            }
        }

        if (changed) RaiseChanged();
    }


    // The first snapshot of a loaded world: everything currently present counts as seen.
    private void TakeBaselineSnapshot()
    {
        present.Clear();
        unseen.Clear();

        IReadOnlyList<GameItem> items = registry != null ? registry.Items : null;
        if (items == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            GameItem item = items[i];
            if (item == null) continue;

            if (IsDiscovered(item)) present.Add(item);
        }
    }


    private Material SilhouetteMaterial
    {
        get
        {
            if (silhouetteMaterial == null && silhouetteShader != null)
            {
                silhouetteMaterial = new Material(silhouetteShader);
                silhouetteMaterial.SetColor("_SilhouetteColor", SILHOUETTE_COLOR);
            }

            return silhouetteMaterial;
        }
    }
}
