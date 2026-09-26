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


    // One entity in the world, as growth and placement need to see it
    public struct LiveEntity
    {
        public GameObject gameObject;
        public EntityId entityId;
        public GameItem item;
        public Vector3Int cell;
        public bool fixture;

        // the clock reading when it became its current stage, see EntitySave
        public int stageDay;
        public float stageCycleTime;
    }


    // What a spawn carries over when it isn't a fresh placement. default is a fresh one: a new guid,
    // not a fixture, stamped with the clock as it is now
    public struct SpawnInfo
    {
        public string guid;
        public bool fixture;

        // 0 stamps with the clock now
        public int stageDay;
        public float stageCycleTime;
    }


    private readonly List<LiveEntity> live = new List<LiveEntity>();

    // entities another manager owns and saves (animals), kept out of live so they never occupy a cell
    // or count as a GameItem. only guid lookups search them.
    private readonly List<EntityId> external = new List<EntityId>();
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

        bool changed = false;

        // a save from before stamping: its growth counts from now, rather than from day 0, where
        // everything would be long overdue
        if (entry.stageDay <= 0)
        {
            CurrentClock(out repaired.stageDay, out repaired.stageCycleTime);
            changed = true;
        }

        // An empty guid has to be replaced, and so does one that an entity loaded earlier already
        // claimed. The earlier entity keeps it, so anything that refers to the guid still resolves.
        string guid = entry.guid;

        if (string.IsNullOrEmpty(guid) || IsGuidLive(guid))
        {
            guid = System.Guid.NewGuid().ToString("N");

            Debug.LogWarning($"EntityManager: saved entity '{entry.itemId}' at ({entry.x}, {entry.z}) has " +
                $"the missing or already used guid '{entry.guid}', so it was given '{guid}' instead", this);

            repaired.guid = guid;
            changed = true;
        }

        CreateInstance(item, new Vector3Int(entry.x, level, entry.z), entry.fixture, guid, repaired.stageDay, repaired.stageCycleTime);

        return changed;
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


    // Guid lookup for entities this manager does not own. For a manager that saves its own entities
    // but still wants TryGetEntity to find them, e.g. so a puzzle can point at the animal that did it.
    public void RegisterExternal(EntityId entityId)
    {
        if (entityId == null) return;
        if (external.Contains(entityId)) return;

        external.Add(entityId);
        entityId.Destroyed += OnExternalDestroyed;
    }


    public void UnregisterExternal(EntityId entityId)
    {
        if (entityId == null) return;

        entityId.Destroyed -= OnExternalDestroyed;
        external.Remove(entityId);
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

            if (Spawn(fixture.item, new Vector3Int(fixture.column.x, level, fixture.column.y), new SpawnInfo { fixture = true }) != null) seeded++;
        }

        manager.AcknowledgeNewWorld();

        if (seeded == 0) return;

        // write the new world straight away
        manager.Save();
    }


    // MARK: spawn


    public GameObject Spawn(GameItem item, Vector3Int cell)
    {
        return Spawn(item, cell, default);
    }


    // info.fixture: one of the world's own starting entities rather than something the player placed.
    // info.guid: the identity to carry on (a growth keeps its seed's), or null for a new one
    public GameObject Spawn(GameItem item, Vector3Int cell, SpawnInfo info)
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

        int stageDay = info.stageDay;
        float stageCycleTime = info.stageCycleTime;

        if (stageDay <= 0) CurrentClock(out stageDay, out stageCycleTime);

        EntityId entityId = CreateInstance(item, cell, info.fixture, info.guid, stageDay, stageCycleTime);

        if (world != null && world.entities != null)
        {
            world.entities.Add(new EntitySave
            {
                itemId = id,
                x = cell.x,
                z = cell.z,
                levelY = TerrainManager.ToLevelMillimetres(terrainManager.GetLevelHeight(cell.y)),

                // reading the guid here is what gives a freshly spawned entity its identity
                guid = entityId.Guid,

                fixture = info.fixture,

                stageDay = stageDay,
                stageCycleTime = stageCycleTime,
            });
        }

        return entityId.gameObject;
    }


    // returns true if there is *any* entity at that cell
    // false if there isn't. except is left out, for an entity that's about to be replaced
    public bool HasEntityAt(Vector3Int cell, EntityId except = null)
    {
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].cell != cell) continue;
            if (except != null && live[i].entityId == except) continue;

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


    // How many of this item the player has placed that are still in the world. The world's own starting
    // fixtures don't count
    public int CountPlaced(GameItem item)
    {
        if (item == null) return 0;

        int count = 0;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].item == item && !live[i].fixture) count++;
        }

        return count;
    }


    // Fills results with every live entity of this item. Takes the caller's list so a spawn rule
    // scanning the world does not allocate
    public void CollectEntitiesOf(GameItem item, List<EntityId> results)
    {
        results.Clear();
        if (item == null) return;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].item != item || live[i].entityId == null) continue;

            results.Add(live[i].entityId);
        }
    }


    // Fills results with the GameItems standing on a cell, so that a change to the ground under them
    // can be tested against their own placement rules. Takes the caller's list because this runs once
    // a frame while a placement drag is live.
    public void CollectEntitiesAt(Vector3Int cell, List<GameItem> results, EntityId except = null)
    {
        results.Clear();

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].cell != cell) continue;
            if (live[i].item == null) continue;
            if (except != null && live[i].entityId == except) continue;

            results.Add(live[i].item);
        }
    }


    // Every live entity, into the caller's list, so a scan can change the world while walking it
    public void CollectLive(List<LiveEntity> results)
    {
        results.Clear();

        for (int i = 0; i < live.Count; i++) results.Add(live[i]);
    }


    public bool TryGetLive(EntityId entityId, out LiveEntity entity)
    {
        entity = default;
        if (entityId == null) return false;

        int index = IndexOfLive(entityId);
        if (index < 0) return false;

        entity = live[index];
        return true;
    }


    // MARK: despawn


    // Takes an entity out of the world and the save at once, without saving: the caller's own flush
    // covers it, so a growth's removal and addition reach disk together. It leaves live straight away
    // rather than when Destroy lands at the end of the frame, so the cell is free for its replacement
    public bool Despawn(EntityId entityId)
    {
        int index = IndexOfLive(entityId);
        if (index < 0) return false;

        GameObject removed = live[index].gameObject;

        live.RemoveAt(index);
        entityId.Destroyed -= OnEntityDestroyed;

        if (entityId.HasGuid && world != null && world.entities != null)
        {
            List<EntitySave> saved = world.entities.entities;

            for (int i = saved.Count - 1; i >= 0; i--)
            {
                if (saved[i].guid != entityId.Guid) continue;

                saved.RemoveAt(i);
                break;
            }
        }

        if (removed != null) Destroy(removed);

        return true;
    }


    // Resolves a guid back to the entity that owns it. False when nothing in the world has that guid,
    // which is also what a guid belonging to an entity that has since been destroyed returns.
    //
    // A puzzle credited to a terrain cell saves the cell's key where a guid would go; that is not an
    // entity, so it is false, quietly
    public bool TryGetEntity(string guid, out EntityId entity)
    {
        entity = null;

        if (string.IsNullOrEmpty(guid)) return false;
        if (PuzzleCredit.IsCellKey(guid)) return false;

        for (int i = 0; i < live.Count; i++)
        {
            EntityId candidate = live[i].entityId;
            if (candidate == null) continue;
            if (!candidate.HasGuid) continue;
            if (candidate.Guid != guid) continue;

            entity = candidate;
            return true;
        }

        for (int i = 0; i < external.Count; i++)
        {
            EntityId candidate = external[i];
            if (candidate == null) continue;
            if (!candidate.HasGuid) continue;
            if (candidate.Guid != guid) continue;

            entity = candidate;
            return true;
        }

        return false;
    }


    // MARK: internals


    // guid null makes a new one on first read; anything else is assigned before anything can read it
    private EntityId CreateInstance(GameItem item, Vector3Int cell, bool fixture, string guid, int stageDay, float stageCycleTime)
    {
        GameObject instance = Instantiate(item.Prefab, terrainManager.CellToWorld(cell), item.Prefab.transform.rotation, entityParent);
        instance.name = item.Prefab.name;

        // a prefab script may already require one, so only add when it is missing
        EntityId entityId = instance.GetComponent<EntityId>();

        if (entityId == null) entityId = instance.AddComponent<EntityId>();

        if (!string.IsNullOrEmpty(guid)) entityId.Assign(guid);

        entityId.Destroyed += OnEntityDestroyed;

        live.Add(new LiveEntity
        {
            gameObject = instance,
            entityId = entityId,
            item = item,
            cell = cell,
            fixture = fixture,
            stageDay = stageDay,
            stageCycleTime = stageCycleTime,
        });

        return entityId;
    }


    private int IndexOfLive(EntityId entityId)
    {
        if (entityId == null) return -1;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].entityId == entityId) return i;
        }

        return -1;
    }


    // The clock as it stands. Loading runs in Awake, possibly before the clock's own, but the clock
    // restores itself from this same section, so reading it straight gives the same answer
    private void CurrentClock(out int day, out float cycleTime)
    {
        WorldStateManager clock = WorldStateManager.Instance;

        if (clock != null)
        {
            day = clock.Day;
            cycleTime = clock.CycleTime;
            return;
        }

        TimeSaveData time = world != null ? world.time : null;

        day = time != null ? Mathf.Max(1, time.day) : 1;
        cycleTime = time != null ? time.elapsedInCycle : 0f;
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


    private void OnExternalDestroyed(EntityId entityId)
    {
        if (entityId != null) entityId.Destroyed -= OnExternalDestroyed;

        // teardown order is not guaranteed, so the manager may already be gone
        if (instance != this) return;

        external.Remove(entityId);
    }


    private Transform MakeContainer()
    {
        return new GameObject("Entities").transform;
    }
}
