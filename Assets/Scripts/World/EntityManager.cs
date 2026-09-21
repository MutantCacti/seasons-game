using System.Collections.Generic;
using UnityEngine;


// Owns the instantiated entities in the world and their section of the save
public class EntityManager : MonoBehaviour
{
    private static EntityManager instance;
    public static EntityManager Instance { get { return instance; } }


    [SerializeField] private TerrainGrid terrainGrid;
    [SerializeField] private GameItemRegistry registry;

    // if this is null, EntityManager creates a new empty at the scene root
    [SerializeField] private Transform entityParent;


    private struct LiveEntity
    {
        public GameObject gameObject;
        public GameItem item;
        public Vector3Int cell;
    }


    private readonly List<LiveEntity> live = new List<LiveEntity>();
    private bool loaded;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one EntityManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (terrainGrid == null) terrainGrid = GetComponent<TerrainGrid>();

        if (terrainGrid == null)
        {
            Debug.LogError("EntityManager: no TerrainGrid, so a cell cannot be turned into a world position", this);
        }

        if (registry == null)
        {
            Debug.LogError("EntityManager: no GameItemRegistry, so a saved entity can never be resolved back to its prefab", this);
        }

        if (entityParent == null) entityParent = MakeContainer();

        EnsureLoaded();
    }


    // MARK: load


    // Public and idempotent; you can spawn while entities are loading
    public void EnsureLoaded()
    {
        if (loaded) return;

        // set before spawning so that a spawn during load cannot re-enter
        loaded = true;

        LoadFromSave();
    }


    private void LoadFromSave()
    {
        if (terrainGrid == null || registry == null) return;

        SaveDataManager manager = SaveDataManager.Instance;
        if (manager == null || manager.SaveData == null) return;

        EntitySaveData saved = manager.SaveData.entities;
        if (saved == null || saved.entities.Count == 0) return;

        // spawn in a stable order so the hierarchy does not reshuffle between sessions
        saved.Sort();

        foreach (EntitySave entry in saved.entities)
        {
            if (string.IsNullOrEmpty(entry.itemId) || !registry.TryGetItemById(entry.itemId, out GameItem item))
            {
                Debug.LogError($"EntityManager: the save holds entity '{entry.itemId}', but no GameItem in the registry has that id - dropping it", this);
                continue;
            }

            if (item.Prefab == null)
            {
                Debug.LogError($"EntityManager: '{item.name}' is saved as an entity but has no prefab, so there is nothing to instantiate", this);
                continue;
            }

            // levels are keyed by height rather than index, so a save survives an elevation being
            // inserted into the terrain, which would shift every index above it
            if (!terrainGrid.TryGetLevelAtMillimetres(entry.levelY, out int level))
            {
                Debug.LogError($"EntityManager: the save holds '{entry.itemId}' at {entry.levelY}mm, " +
                    $"which is not a level in this terrain - dropping it", this);
                continue;
            }

            CreateInstance(item, new Vector3Int(entry.x, level, entry.z));
        }
    }


    // MARK: spawn


    public GameObject Spawn(GameItem item, Vector3Int cell)
    {
        EnsureLoaded();

        if (item == null)
        {
            Debug.LogError("EntityManager: tried to spawn a null item", this);
            return null;
        }

        // a tile-only item is a legal thing to place, it simply has no entity attached to it
        if (item.Prefab == null) return null;

        if (terrainGrid == null) return null;

        if (cell.y < 0 || cell.y >= terrainGrid.LevelCount)
        {
            Debug.LogError($"EntityManager: level {cell.y} is not a level in this terrain " +
                $"(0 to {terrainGrid.LevelCount - 1}), so '{item.name}' cannot be spawned there", this);
            return null;
        }

        // logs and gives up if the item is not in the registry
        if (registry == null || !registry.TryGetIdByItem(item, out string id)) return null;

        GameObject instance = CreateInstance(item, cell);

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager != null && manager.SaveData != null && manager.SaveData.entities != null)
        {
            manager.SaveData.entities.Add(new EntitySave
            {
                itemId = id,
                x = cell.x,
                z = cell.z,
                levelY = TerrainGrid.ToLevelMillimetres(terrainGrid.GetLevelHeight(cell.y))
            });
        }

        return instance;
    }


    public bool HasEntityAt(GameItem item, Vector3Int cell)
    {
        if (item == null) return false;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].item != item) continue;
            if (live[i].cell != cell) continue;

            return true;
        }

        return false;
    }


    // Fills results with the GameItems standing on a cell, so that a change to the ground under them
    // can be tested against their own placement rules. Takes the caller's list because this runs once
    // a frame while a placement drag is live.
    public void CollectEntitiesAt(Vector3Int cell, List<GameItem> results)
    {
        results.Clear();

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].cell != cell) continue;
            if (live[i].item == null) continue;

            results.Add(live[i].item);
        }
    }


    // MARK: internals


    private GameObject CreateInstance(GameItem item, Vector3Int cell)
    {
        GameObject instance = Instantiate(item.Prefab, terrainGrid.CellToWorld(cell), item.Prefab.transform.rotation, entityParent);
        instance.name = item.Prefab.name;

        live.Add(new LiveEntity { gameObject = instance, item = item, cell = cell });

        return instance;
    }


    private Transform MakeContainer()
    {
        return new GameObject("Entities").transform;
    }
}
