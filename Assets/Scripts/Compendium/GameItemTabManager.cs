using System.Collections.Generic;
using UnityEngine;


// The Game Items tab: registry items, three per page, with undiscovered items greyed out and
// newly discovered items pulsing with an outline until the player hovers over them. An item is
// discovered the first time it is present (inventory, world or terrain) and stays discovered.
public class GameItemTabManager : CompendiumTabManager
{
    private const int ENTRIES_PER_PAGE = 3;

    private static readonly Color SILHOUETTE_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);


    [SerializeField] private GameItemRegistry registry;
    [SerializeField] private GameObject pagePrefab;
    [SerializeField] private Shader silhouetteShader;
    [SerializeField] private Color discoveredColour = new Color(237.0f / 255.0f, 196.0f / 255.0f, 59.0f / 255.0f, 1.0f);  // #EDC43B

    private Material silhouetteMaterial;

    // Discovered and unseen items are saved through the base class, keyed by GameItem.Id (unseen with
    // kind 0). Both are global, so a checkpoint reset never takes a discovery back
    private readonly List<UnseenSave> savedUnseen = new List<UnseenSave>();
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

        // A new world discovers what the player begins with like anything else, so it is all unseen.
        // A save from before discovery was remembered is seeded quietly from what is present now,
        // so nothing on show goes dark
        if (!baselineEmpty && !DiscoverySeeded) SeedDiscovery();

        // anything present but never discovered is a discovery, which also catches what a load's
        // growth catch-up placed before this tab was listening
        RecomputePresence();

        PruneSavedUnseen();
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

        item = registry.Entries[index];

        return item != null;
    }


    // MARK: unseen


    // everything here is a discovery, so the first unseen item in range settles it
    public override UnseenPriority HighestUnseenInPages(int firstPage, int lastPage)
    {
        int first = Mathf.Max(firstPage, 0) * ENTRIES_PER_PAGE;
        int end = Mathf.Min((lastPage + 1) * ENTRIES_PER_PAGE, Count);

        for (int i = first; i < end; i++)
        {
            if (!TryGetItem(i, out GameItem item)) continue;
            if (TryGetUnseen(item.Id, out int _)) return UnseenPriority.Discovery;
        }

        return UnseenPriority.None;
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

        if (IsDiscovered(item.Id))
        {
            entry.SetItem(item);

            if (TryGetUnseen(item.Id, out int _)) ShowHighlight(entry, item);
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

        highlight.Show(discoveredColour, () => ClearUnseen(item.Id));
    }


    private void HideHighlight(CompendiumEntry entry)
    {
        UnseenHighlight highlight = entry.GetComponent<UnseenHighlight>();
        if (highlight != null) highlight.Hide();
    }


    // Present: held in the inventory, standing in the world as a live entity, or remapped onto at
    // least one terrain cell. Only ever used to discover, never to un-discover
    private bool IsPresent(GameItem item)
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


    // Anything present that has never been discovered becomes discovered and unseen. Absence changes
    // nothing: once seen, it stays in the book
    private void RecomputePresence()
    {
        IReadOnlyList<GameItem> items = registry != null ? registry.Entries : null;
        if (items == null) return;

        bool changed = false;

        for (int i = 0; i < items.Count; i++)
        {
            GameItem item = items[i];
            if (item == null) continue;

            if (IsDiscovered(item.Id)) continue;
            if (!IsPresent(item)) continue;

            MarkDiscovered(item.Id, false);
            MarkUnseen(item.Id, 0, false);
            changed = true;
        }

        MarkDiscoverySeeded();

        // one save for the whole diff rather than one per item
        if (!changed) return;

        SaveCompendium();
        RaiseChanged();
    }


    // An old save's first load: what is present now counts as already seen, so it is discovered but
    // not unseen (whatever it saved as unseen stays unseen)
    private void SeedDiscovery()
    {
        IReadOnlyList<GameItem> items = registry != null ? registry.Entries : null;
        if (items == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            GameItem item = items[i];
            if (item == null) continue;

            if (IsPresent(item)) MarkDiscovered(item.Id, false);
        }

        MarkDiscoverySeeded();

        SaveCompendium();

        // pages can bind before Start, while nothing was discovered yet
        RaiseChanged();
    }


    // Drops unseen ids no item has any more, and any that were never discovered (unseen is always a
    // subset of discovered, so that is only ever a hand-edited or broken save)
    private void PruneSavedUnseen()
    {
        if (registry == null) return;

        CopyUnseen(savedUnseen);

        bool pruned = false;

        for (int i = 0; i < savedUnseen.Count; i++)
        {
            string id = savedUnseen[i].id;

            if (!registry.TryGetById(id, out GameItem _))
            {
                Debug.LogWarning($"GameItemTabManager: the save holds unseen item '{id}', but no item in " +
                    $"'{registry.name}' has that id - dropping it", this);

                ClearUnseen(id, false);
                pruned = true;
                continue;
            }

            if (IsDiscovered(id)) continue;

            ClearUnseen(id, false);
            pruned = true;
        }

        savedUnseen.Clear();

        if (pruned) SaveCompendium();
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
