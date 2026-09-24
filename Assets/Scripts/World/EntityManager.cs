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
        public EntityId entityId;
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

        // by index, because a entry whose guid had to be replaced is written back into the list
        for (int i = 0; i < saved.entities.Count; i++)
        {
            EntitySave entry = saved.entities[i];

            if (LoadEntity(entry, out EntitySave repaired)) saved.entities[i] = repaired;
        }
    }


    // Instantiates one saved entity and restores its identity. Returns true when the entry had to be
    // changed in the list, which is only ever a repaired guid.
    private bool LoadEntity(EntitySave entry, out EntitySave repaired)
    {
        repaired = entry;

        if (string.IsNullOrEmpty(entry.itemId) || !registry.TryGetById(entry.itemId, out GameItem item))
        {
            Debug.LogError($"EntityManager: the save holds entity '{entry.itemId}', but no GameItem in the registry has that id - dropping it", this);
            return false;
        }

        if (item.Prefab == null)
        {
            Debug.LogError($"EntityManager: '{item.name}' is saved as an entity but has no prefab, so there is nothing to instantiate", this);
            return false;
        }

        // levels are keyed by height rather than index, so a save survives an elevation being
        // inserted into the terrain, which would shift every index above it
        if (!terrainManager.TryGetLevelAtMillimetres(entry.levelY, out int level))
        {
            Debug.LogError($"EntityManager: the save holds '{entry.itemId}' at {entry.levelY}mm, " +
                $"which is not a level in this terrain - dropping it", this);
            return false;
        }

        EntityId entityId = CreateInstance(item, new Vector3Int(entry.x, level, entry.z));

        // An empty guid has to be replaced, and so does one that an entity loaded earlier already
        // claimed. The earlier entity keeps it, so anything that refers to the guid still resolves.
        if (string.IsNullOrEmpty(entry.guid) || IsGuidLive(entry.guid))
        {
            string replacement = System.Guid.NewGuid().ToString("N");

            Debug.LogWarning($"EntityManager: saved entity '{entry.itemId}' at ({entry.x}, {entry.z}) has " +
                $"the missing or already used guid '{entry.guid}', so it was given '{replacement}' instead", this);

            repaired.guid = replacement;
            entityId.Assign(replacement);
            return true;
        }

        entityId.Assign(entry.guid);
        return false;
    }


    private bool IsGuidLive(string guid)
    {
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].entityId == null) continue;
            if (live[i].entityId.HasGuid && live[i].entityId.Guid == guid) return true;
        }

        return false;
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
            manager.AcknowledgeNewWorld();
            return;
        }

        if (terrainManager == null)
        {
            manager.AcknowledgeNewWorld();
            return;
        }

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
        if (registry == null || !registry.TryGetIdByEntry(item, out string id)) return null;

        EntityId entityId = CreateInstance(item, cell);

        if (world != null && world.entities != null)
        {
            world.entities.Add(new EntitySave
            {
                itemId = id,
                x = cell.x,
                z = cell.z,
                levelY = TerrainManager.ToLevelMillimetres(terrainManager.GetLevelHeight(cell.y)),

                // reading the guid here is what gives a freshly spawned entity its identity
                guid = entityId.Guid
            });
        }

        return entityId.gameObject;
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


    // true when at least one live entity of this item exists in the world
    public bool HasEntityOf(GameItem item)
    {
        if (item == null) return false;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].item == item) return true;
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


    // Resolves a guid back to the entity that owns it. False when nothing in the world has that guid,
    // which is also what a guid belonging to an entity that has since been destroyed returns.
    public bool TryGetEntity(string guid, out EntityId entity)
    {
        entity = null;

        if (string.IsNullOrEmpty(guid)) return false;

        for (int i = 0; i < live.Count; i++)
        {
            EntityId candidate = live[i].entityId;
            if (candidate == null) continue;
            if (!candidate.HasGuid) continue;
            if (candidate.Guid != guid) continue;

            entity = candidate;
            return true;
        }

        return false;
    }


    // MARK: internals


    private EntityId CreateInstance(GameItem item, Vector3Int cell)
    {
        GameObject instance = Instantiate(item.Prefab, terrainManager.CellToWorld(cell), item.Prefab.transform.rotation, entityParent);
        instance.name = item.Prefab.name;

        // a prefab script may already require one, so only add when it is missing
        EntityId entityId = instance.GetComponent<EntityId>();

        if (entityId == null) entityId = instance.AddComponent<EntityId>();

        entityId.Destroyed += OnEntityDestroyed;

        live.Add(new LiveEntity { gameObject = instance, entityId = entityId, item = item, cell = cell });

        return entityId;
    }


    // Keeps the live list free of destroyed entities: it is walked every frame by placement, and a
    // destroyed GameObject left in it would keep matching a cell
    private void OnEntityDestroyed(EntityId entityId)
    {
        if (entityId != null) entityId.Destroyed -= OnEntityDestroyed;

        // teardown order is not guaranteed, so the manager may already be gone
        if (instance != this) return;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].entityId != entityId) continue;

            live.RemoveAt(i);
            return;
        }
    }


    private Transform MakeContainer()
    {
        return new GameObject("Entities").transform;
    }
}
