using System.Collections.Generic;
using UnityEngine;


// Owns the instantiated entities in the world and their section of the save
public class EntityManager : MonoBehaviour
{
    private static EntityManager instance;
    public static EntityManager Instance { get { return instance; } }


    [SerializeField] private TerrainManager terrainManager;
    [SerializeField] private GameItemRegistry registry;

    // if this is null, EntityManager creates a new empty at the scene root
    [SerializeField] private Transform entityParent;

    // The entities a brand new world starts with
    [SerializeField] private WorldFixtures worldFixtures;


    private struct LiveEntity
    {
        public GameObject gameObject;
        public GameItem item;
        public Vector3Int cell;
    }


    private readonly List<LiveEntity> live = new List<LiveEntity>();
    private bool loaded;

    // Cached for the same reason TerrainManager caches it: SaveData.world is the one object every
    // manager writes into, and only a new game or a checkpoint restore ever swaps it out
    private WorldState world;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one EntityManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (terrainManager == null) terrainManager = GetComponent<TerrainManager>();

        if (terrainManager == null)
        {
            Debug.LogError("EntityManager: no TerrainManager, so a cell cannot be turned into a world position", this);
        }

        if (registry == null)
        {
            Debug.LogError("EntityManager: no GameItemRegistry, so a saved entity can never be resolved back to its prefab", this);
        }

        if (entityParent == null) entityParent = MakeContainer();

        ResolveWorld();

        EnsureLoaded();
    }


    // The save managers own the live world; this only falls back to a throwaway one so that a scene
    // without a SaveDataManager still runs, at the cost of its entities never reaching disk
    private void ResolveWorld()
    {
        SaveDataManager manager = SaveDataManager.Instance;
        world = manager != null && manager.SaveData != null ? manager.SaveData.world : null;

        if (world == null)
        {
            Debug.LogWarning("EntityManager: no SaveDataManager in the scene, so spawned entities " +
                "will not be saved", this);
            world = new WorldState();
        }

        world.EnsureSections();
    }


    // resolving a fixture's level needs TerrainManager's state, which it builds in
    // its own Awake
    private void Start()
    {
        SeedWorldFixtures();
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
        if (terrainManager == null || registry == null) return;
        if (world == null) return;

        EntitySaveData saved = world.entities;
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
            if (!terrainManager.TryGetLevelAtMillimetres(entry.levelY, out int level))
            {
                Debug.LogError($"EntityManager: the save holds '{entry.itemId}' at {entry.levelY}mm, " +
                    $"which is not a level in this terrain - dropping it", this);
                continue;
            }

            CreateInstance(item, new Vector3Int(entry.x, level, entry.z));
        }
    }


    // MARK: world creation


    // A new world starts as an empty save
    // authored fixtures are seeded into it here
    private void SeedWorldFixtures()
    {
        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || !manager.IsConstructingNewWorld) return;

        if (worldFixtures == null)
        {
            Debug.LogError("EntityManager: no WorldFixtures assigned, so a new world starts with no entities", this);
            return;
        }

        if (terrainManager == null) return;

        int seeded = 0;

        for (int i = 0; i < worldFixtures.Count; i++)
        {
            WorldFixtures.Fixture fixture = worldFixtures.Entries[i];

            if (fixture.item == null)
            {
                Debug.LogError($"EntityManager: world fixture {i} has no item", this);
                continue;
            }

            int level = terrainManager.GetTopLevel(fixture.column.x, fixture.column.y);

            if (level < 0)
            {
                Debug.LogError($"EntityManager: world fixture '{fixture.item.name}' is authored at column " +
                    $"({fixture.column.x}, {fixture.column.y}), which the tilemaps leave empty", this);
                continue;
            }

            if (Spawn(fixture.item, new Vector3Int(fixture.column.x, level, fixture.column.y)) != null) seeded++;
        }

        manager.AcknowledgeNewWorld();

        if (seeded == 0) return;

        // write the new world straight away
        manager.Save();
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

        if (terrainManager == null) return null;

        if (cell.y < 0 || cell.y >= terrainManager.LevelCount)
        {
            Debug.LogError($"EntityManager: level {cell.y} is not a level in this terrain " +
                $"(0 to {terrainManager.LevelCount - 1}), so '{item.name}' cannot be spawned there", this);
            return null;
        }

        // logs and gives up if the item is not in the registry
        if (registry == null || !registry.TryGetIdByItem(item, out string id)) return null;

        GameObject instance = CreateInstance(item, cell);

        if (world != null && world.entities != null)
        {
            world.entities.Add(new EntitySave
            {
                itemId = id,
                x = cell.x,
                z = cell.z,
                levelY = TerrainManager.ToLevelMillimetres(terrainManager.GetLevelHeight(cell.y))
            });
        }

        return instance;
    }


    // returns true if there is *any* entity at that cell
    // false if there isn't
    public bool HasEntityAt(Vector3Int cell)
    {
        for (int i = 0; i < live.Count; i++)
        {
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
        GameObject instance = Instantiate(item.Prefab, terrainManager.CellToWorld(cell), item.Prefab.transform.rotation, entityParent);
        instance.name = item.Prefab.name;

        live.Add(new LiveEntity { gameObject = instance, item = item, cell = cell });

        return instance;
    }


    private Transform MakeContainer()
    {
        return new GameObject("Entities").transform;
    }
}
