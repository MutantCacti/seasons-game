using UnityEngine;


// The Game Items tab: registry items, three per page, with undiscovered items greyed out.
public class GameItemTabManager : CompendiumTabManager
{
    private const int ENTRIES_PER_PAGE = 3;

    private static readonly Color SILHOUETTE_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);


    [SerializeField] private GameItemRegistry registry;
    [SerializeField] private GameObject pagePrefab;
    [SerializeField] private Shader silhouetteShader;

    private Material silhouetteMaterial;


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
            return;
        }

        if (IsDiscovered(item))
        {
            entry.SetItem(item);
            return;
        }

        entry.SetImage(item.Icon);
        entry.SetIconMaterial(SilhouetteMaterial);
        entry.SetNameText("Undiscovered");
        entry.SetDescriptionText(string.Empty);
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
