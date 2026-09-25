using System;
using System.Collections.Generic;
using UnityEngine;


// Owns the animals in the world, their section of the save, and the spawn rules that bring them in.
// Rules are AnimalSpawnRule assets; this forwards world events to them and does the spawning on their
// behalf, so every animal is gated, saved, registered and credited the same way.
//
// Nothing reaches a rule until the world has finished loading (puzzles, world states and entities),
// and then every rule hears OnLoaded once. Events a rule raises while being told of another (a spawn
// inside OnAnimalSpawned, say) are queued and dispatched after, so a chain resolves in one frame
// without the rules list being walked twice at once.
public class AnimalManager : MonoBehaviour, AnimalSpawnContext
{
    private static AnimalManager instance;
    public static AnimalManager Instance { get { return instance; } }


    [SerializeField] private AnimalRegistry registry;

    // the rules live in a registry asset rather than on this component, so an editor can gather and
    // validate them the way every other id-keyed list in the project is gathered and validated
    [SerializeField] private AnimalSpawnRuleRegistry ruleRegistry;

    [SerializeField] private TerrainManager terrainManager;

    // if this is null, AnimalManager creates a new empty at the scene root
    [SerializeField] private Transform animalParent;


    // a new animal came into the world (not raised for animals loaded from the save)
    public event Action<Animal> AnimalSpawned;

    // the set of live animals changed, loads included. the compendium tab recomputes from this
    public event Action AnimalsChanged;


    private struct LiveAnimal
    {
        public Animal animal;
        public EntityId entityId;
        public Transform transform;
        public AnimalController controller;

        // the row this animal's transform is copied into. AnimalSaveData.Sort reorders the list but
        // never replaces the rows, so this stays the one in the save
        public AnimalSave save;
    }


    private readonly List<LiveAnimal> live = new List<LiveAnimal>();

    // cached for the same reason EntityManager caches it: only a new game or a checkpoint restore ever
    // swaps SaveData.world out, and both reload the scene
    private WorldState world;

    private PlacementController placementController;
    private PuzzleManager puzzleManager;
    private WorldStateManager worldStates;
    private SaveDataManager saveManager;

    // true once every rule has heard OnLoaded; nothing is dispatched before
    private bool ready;

    private readonly Queue<Action<AnimalSpawnRule>> pendingDispatches = new Queue<Action<AnimalSpawnRule>>();
    private bool dispatching;

    // scratch for the context's scans
    private readonly List<EntityId> scratchEntities = new List<EntityId>();
    private readonly List<Vector3Int> scratchCells = new List<Vector3Int>();
    private readonly List<Vector3> scratchPositions = new List<Vector3>();


    public TerrainManager Terrain { get { return terrainManager; } }
    public WorldStateManager States { get { return worldStates; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one AnimalManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (registry == null)
        {
            Debug.LogError("AnimalManager: no AnimalRegistry, so no animal can be spawned, saved or loaded", this);
        }

        if (ruleRegistry == null)
        {
            Debug.LogError("AnimalManager: no AnimalSpawnRuleRegistry, so no spawn rule can ever fire", this);
        }

        if (animalParent == null) animalParent = new GameObject("Animals").transform;

        ResolveWorld();

        LoadFromSave();
    }


    private void ResolveWorld()
    {
        SaveDataManager manager = SaveDataManager.Instance;
        world = manager != null && manager.SaveData != null ? manager.SaveData.world : null;

        if (world == null)
        {
            Debug.LogWarning("AnimalManager: no SaveDataManager in the scene, so spawned animals " +
                "will not be saved", this);
            world = new WorldState();
        }

        world.EnsureSections();
    }


    // registration waits for Start: EntityManager's Instance is only guaranteed once every Awake has
    // run. a completion ping restored before this retries its guid lookup on its own, so the gap is
    // harmless.
    private void Start()
    {
        if (terrainManager == null) terrainManager = TerrainManager.Instance;

        if (terrainManager == null)
        {
            Debug.LogError("AnimalManager: no TerrainManager, so tile-based spawn rules can never fire", this);
        }

        if (EntityManager.Instance != null)
        {
            for (int i = 0; i < live.Count; i++)
            {
                EntityManager.Instance.RegisterExternal(live[i].entityId);
            }
        }
        else
        {
            Debug.LogError("AnimalManager: no EntityManager, so a puzzle completed by an animal cannot find it", this);
        }

        placementController = PlacementController.Instance;

        if (placementController != null) placementController.ItemPlaced += OnItemPlaced;
        else Debug.LogError("AnimalManager: no PlacementController, so placement-based spawn rules can never fire", this);

        AnimalsChanged?.Invoke();

        SubscribeToWorld();
    }


    // the sticky ones (puzzles loaded, world states loaded, season and conditions) answer at once if
    // they have already happened, which is why the replayed states are dropped while not ready
    //
    // every reference is resolved before anything is subscribed, because a sticky event calls
    // TryBecomeReady on the spot and it must not see a manager as missing that is only unassigned
    private void SubscribeToWorld()
    {
        saveManager = SaveDataManager.Instance;
        worldStates = WorldStateManager.Instance;
        puzzleManager = PuzzleManager.Instance;

        if (saveManager != null) saveManager.NewWorldConstructed += TryBecomeReady;

        if (worldStates != null)
        {
            worldStates.Loaded += TryBecomeReady;
            worldStates.SeasonChanged += OnSeasonChanged;
            worldStates.ConditionChanged += OnConditionChanged;
            worldStates.Sunrise += OnSunrise;
            worldStates.Noon += OnNoon;
            worldStates.Sunset += OnSunset;
            worldStates.Midnight += OnMidnight;
        }
        else
        {
            Debug.LogError("AnimalManager: no WorldStateManager, so no rule hears a season, a condition or the time of day", this);
        }

        // without one, nothing else would ever call this: the world states are simply not waited for
        if (worldStates == null) TryBecomeReady();

        if (puzzleManager != null)
        {
            puzzleManager.OnPuzzleUnlocked += OnPuzzleUnlocked;
            puzzleManager.OnPuzzlesLoaded += TryBecomeReady;
        }
        else
        {
            Debug.LogError("AnimalManager: no PuzzleManager, so rules are never gated on their puzzles", this);
            TryBecomeReady();
        }
    }


    private void OnDestroy()
    {
        if (placementController != null) placementController.ItemPlaced -= OnItemPlaced;

        if (saveManager != null) saveManager.NewWorldConstructed -= TryBecomeReady;

        if (worldStates != null)
        {
            worldStates.Loaded -= TryBecomeReady;
            worldStates.SeasonChanged -= OnSeasonChanged;
            worldStates.ConditionChanged -= OnConditionChanged;
            worldStates.Sunrise -= OnSunrise;
            worldStates.Noon -= OnNoon;
            worldStates.Sunset -= OnSunset;
            worldStates.Midnight -= OnMidnight;
        }

        if (puzzleManager != null)
        {
            puzzleManager.OnPuzzleUnlocked -= OnPuzzleUnlocked;
            puzzleManager.OnPuzzlesLoaded -= TryBecomeReady;
        }

        if (instance == this) instance = null;
    }


    // Copies every live animal's transform into its save row, in memory only. Every save and every
    // checkpoint capture (which clones the world) then sees where the animals are now, not where
    // they spawned. LateUpdate so a mover's Update this frame is included.
    private void LateUpdate()
    {
        for (int i = 0; i < live.Count; i++)
        {
            LiveAnimal animal = live[i];
            if (animal.transform == null || animal.save == null) continue;

            animal.save.position = animal.transform.position;
            animal.save.rotation = animal.transform.rotation;
        }
    }


    // MARK: queries


    // every live animal, for things that look them all over (picking one under the pointer)
    public int LiveCount { get { return live.Count; } }


    public AnimalController LiveAt(int index)
    {
        return index >= 0 && index < live.Count ? live[index].controller : null;
    }


    // true when at least one live animal of this kind exists in the world
    public bool HasAnimalOf(Animal animal)
    {
        if (animal == null) return false;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].animal == animal) return true;
        }

        return false;
    }


    public int Count(Animal animal)
    {
        if (animal == null) return 0;

        int count = 0;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].animal == animal) count++;
        }

        return count;
    }


    public bool CanSpawn(AnimalSpawnRule rule)
    {
        if (rule == null || rule.Animal == null) return false;
        if (!IsRegistered(rule)) return false;
        if (!IsUnlocked(rule)) return false;

        return Count(rule.Animal) < rule.Animal.MobCap;
    }


    // a completed puzzle stays unlocked, so this holds from the unlock on. with no PuzzleManager (a test
    // scene) nothing is gated, so progress never stalls
    private bool IsUnlocked(AnimalSpawnRule rule)
    {
        if (rule.Completes == null) return true;
        if (puzzleManager == null) return true;

        return puzzleManager.IsUnlocked(rule.Completes);
    }


    // MARK: rules


    // registry.Entries is an IReadOnlyList, which has no Contains, so this is the plain loop
    private bool IsRegistered(AnimalSpawnRule rule)
    {
        if (rule == null || ruleRegistry == null) return false;

        IReadOnlyList<AnimalSpawnRule> rules = ruleRegistry.Entries;

        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i] == rule) return true;
        }

        return false;
    }


    // Ready once puzzles have loaded, world states have loaded and a new world has finished being
    // built. Each of those calls this, and the last one through tells every rule OnLoaded
    private void TryBecomeReady()
    {
        if (ready) return;

        if (puzzleManager != null && !puzzleManager.IsLoaded) return;
        if (saveManager != null && saveManager.IsConstructingNewWorld) return;

        // a rule's OnLoaded scan must see the placed entities
        if (EntityManager.Instance != null) EntityManager.Instance.EnsureLoaded();

        ready = true;

        Dispatch(rule => rule.OnLoaded(this));
    }


    // Walks every rule with the hook. A hook that dispatches again (a spawn inside a spawn) is queued
    // behind this one. A rule that throws is logged and the rest still hear the event
    private void Dispatch(Action<AnimalSpawnRule> hook)
    {
        if (!ready || ruleRegistry == null) return;

        pendingDispatches.Enqueue(hook);

        if (dispatching) return;
        dispatching = true;

        try
        {
            while (pendingDispatches.Count > 0)
            {
                Action<AnimalSpawnRule> next = pendingDispatches.Dequeue();
                IReadOnlyList<AnimalSpawnRule> rules = ruleRegistry.Entries;

                for (int i = 0; i < rules.Count; i++)
                {
                    AnimalSpawnRule rule = rules[i];
                    if (rule == null) continue;

                    try
                    {
                        next(rule);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, rule);
                    }
                }
            }
        }
        finally
        {
            dispatching = false;
        }
    }


    private void OnItemPlaced(GameItem item, Vector3Int cell, GameObject instance)
    {
        Dispatch(rule => rule.OnItemPlaced(this, item, cell));
    }


    // only to the rules that complete this puzzle: it is their gate that just opened
    private void OnPuzzleUnlocked(Puzzle puzzle)
    {
        Dispatch(rule =>
        {
            if (rule.Completes == puzzle) rule.OnPuzzleUnlocked(this);
        });
    }


    private void OnSeasonChanged(Season season)
    {
        Dispatch(rule => rule.OnSeasonChanged(this, season));
    }


    private void OnConditionChanged(WorldCondition condition, ConditionPhase phase)
    {
        Dispatch(rule => rule.OnConditionChanged(this, condition, phase));
    }


    private void OnSunrise() { Dispatch(rule => rule.OnSunrise(this)); }
    private void OnNoon() { Dispatch(rule => rule.OnNoon(this)); }
    private void OnSunset() { Dispatch(rule => rule.OnSunset(this)); }
    private void OnMidnight() { Dispatch(rule => rule.OnMidnight(this)); }


    // MARK: spawn


    // The one way a new animal enters the world. Gates it (registered, puzzle unlocked, under the mob
    // cap), saves, and only then submits the rule's puzzle, so the completion's guid already resolves
    // and the save already holds the animal. Completing an already completed puzzle does nothing, so
    // every spawn after the first just adds an animal.
    public bool Spawn(AnimalSpawnRule rule, Vector3 position)
    {
        if (rule == null) return false;

        if (!IsRegistered(rule))
        {
            Debug.LogError($"AnimalManager: '{rule.name}' tried to spawn but is not in the rules list", rule);
            return false;
        }

        Animal animal = rule.Animal;

        if (animal == null)
        {
            Debug.LogError($"AnimalManager: '{rule.name}' has no animal, so there is nothing to spawn", rule);
            return false;
        }

        if (!IsUnlocked(rule)) return false;
        if (Count(animal) >= animal.MobCap) return false;

        // logs and gives up if the animal is not in the registry
        if (registry == null || !registry.TryGetIdByEntry(animal, out string animalId)) return false;

        AnimalSave save = new AnimalSave { animalId = animalId, position = position };

        EntityId entityId = CreateInstance(animal, save, position, animal.Prefab != null ? animal.Prefab.transform.rotation : Quaternion.identity);
        if (entityId == null) return false;

        // reading the guid here is what gives a freshly spawned animal its identity
        save.guid = entityId.Guid;
        save.rotation = entityId.transform.rotation;

        world.animals.animals.Add(save);

        if (EntityManager.Instance != null) EntityManager.Instance.RegisterExternal(entityId);

        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();

        AnimalSpawned?.Invoke(animal);
        AnimalsChanged?.Invoke();

        if (rule.Completes != null)
        {
            if (PuzzleManager.Instance != null) PuzzleManager.Instance.Complete(rule.Completes, entityId);
            else Debug.LogError($"AnimalManager: no PuzzleManager, so '{animal.name}' spawning did not " +
                $"complete '{rule.Completes.name}'", this);
        }

        Dispatch(other => other.OnAnimalSpawned(this, animal, entityId));

        return true;
    }


    // MARK: context queries


    public void CollectPositionsOf(GameItem item, List<Vector3> results)
    {
        results.Clear();
        if (item == null) return;

        if (EntityManager.Instance != null)
        {
            EntityManager.Instance.CollectEntitiesOf(item, scratchEntities);

            for (int i = 0; i < scratchEntities.Count; i++)
            {
                if (scratchEntities[i] != null) results.Add(scratchEntities[i].transform.position);
            }
        }

        if (item.Tile != null && terrainManager != null)
        {
            terrainManager.CollectCellsOf(item.Tile, scratchCells);

            for (int i = 0; i < scratchCells.Count; i++) results.Add(terrainManager.CellToWorld(scratchCells[i]));
        }
    }


    // plain world space distance: a thing on a cliff above counts as near if it is close enough
    public bool FindWithin(Vector3 point, float radius, GameItem item, out Vector3 found)
    {
        found = default;

        CollectPositionsOf(item, scratchPositions);

        float best = float.PositiveInfinity;

        for (int i = 0; i < scratchPositions.Count; i++)
        {
            float distance = Vector3.Distance(point, scratchPositions[i]);
            if (distance > radius || distance >= best) continue;

            best = distance;
            found = scratchPositions[i];
        }

        return !float.IsPositiveInfinity(best);
    }


    public bool TryRandomSurface(Predicate<TerrainTile> accept, bool offScreen, out Vector3 position)
    {
        position = default;
        if (terrainManager == null) return false;

        Camera view = offScreen ? Camera.main : null;

        if (offScreen && view == null)
        {
            Debug.LogError("AnimalManager: no main camera, so an off-screen spawn point cannot be picked", this);
            return false;
        }

        terrainManager.GetWorldXZBounds(out Vector2 min, out Vector2 max);

        scratchPositions.Clear();

        for (int z = Mathf.FloorToInt(min.y); z < Mathf.CeilToInt(max.y); z++)
        {
            for (int x = Mathf.FloorToInt(min.x); x < Mathf.CeilToInt(max.x); x++)
            {
                int level = terrainManager.GetTopLevel(x, z);
                if (level < 0) continue;

                Vector3Int cell = new Vector3Int(x, level, z);
                if (accept != null && !accept(terrainManager.GetTile(cell))) continue;

                Vector3 point = terrainManager.CellToWorld(cell);

                if (view != null && IsOnScreen(view, point)) continue;

                scratchPositions.Add(point);
            }
        }

        if (scratchPositions.Count == 0) return false;

        position = scratchPositions[UnityEngine.Random.Range(0, scratchPositions.Count)];
        return true;
    }


    private static bool IsOnScreen(Camera view, Vector3 point)
    {
        Vector3 viewport = view.WorldToViewportPoint(point);

        return viewport.z > 0.0f && viewport.x >= 0.0f && viewport.x <= 1.0f && viewport.y >= 0.0f && viewport.y <= 1.0f;
    }


    // MARK: load


    private void LoadFromSave()
    {
        if (registry == null) return;

        List<AnimalSave> saved = world.animals.animals;
        if (saved == null || saved.Count == 0) return;

        // spawn in a stable order so the hierarchy does not reshuffle between sessions
        world.animals.Sort();

        // forwards, so the first of two clashing guids is the one that keeps it
        List<AnimalSave> kept = new List<AnimalSave>(saved.Count);

        for (int i = 0; i < saved.Count; i++)
        {
            AnimalSave entry = saved[i];

            if (entry != null && LoadAnimal(entry)) kept.Add(entry);
        }

        if (kept.Count == saved.Count) return;

        saved.Clear();
        saved.AddRange(kept);
    }


    // false drops the entry from the save
    private bool LoadAnimal(AnimalSave entry)
    {
        if (string.IsNullOrEmpty(entry.animalId) || !registry.TryGetById(entry.animalId, out Animal animal))
        {
            Debug.LogError($"AnimalManager: the save holds animal '{entry.animalId}', but no Animal in the " +
                $"registry has that id - dropping it", this);
            return false;
        }

        if (animal.Prefab == null)
        {
            Debug.LogError($"AnimalManager: '{animal.name}' has no prefab, so the saved one cannot be " +
                $"loaded - dropping it", animal);
            return false;
        }

        // a save from before rotation was written holds an all-zero quaternion, which is not a rotation
        Quaternion rotation = entry.rotation;
        bool unset = rotation.x == 0.0f && rotation.y == 0.0f && rotation.z == 0.0f && rotation.w == 0.0f;

        if (unset) rotation = animal.Prefab.transform.rotation;

        EntityId entityId = CreateInstance(animal, entry, entry.position, rotation);
        if (entityId == null) return false;

        // same repair as EntityManager.LoadEntity: an animal loaded earlier keeps a clashing guid, so
        // anything that refers to it still resolves
        if (string.IsNullOrEmpty(entry.guid) || IsGuidLive(entry.guid, entityId))
        {
            string replacement = System.Guid.NewGuid().ToString("N");

            Debug.LogWarning($"AnimalManager: saved animal '{entry.animalId}' has the missing or already " +
                $"used guid '{entry.guid}', so it was given '{replacement}' instead", this);

            entry.guid = replacement;
        }

        entityId.Assign(entry.guid);
        return true;
    }


    private bool IsGuidLive(string guid, EntityId except)
    {
        for (int i = 0; i < live.Count; i++)
        {
            EntityId candidate = live[i].entityId;
            if (candidate == null || candidate == except) continue;
            if (candidate.HasGuid && candidate.Guid == guid) return true;
        }

        return false;
    }


    // MARK: internals


    // Instantiates an animal and binds its controller to the save row it writes through to
    private EntityId CreateInstance(Animal animal, AnimalSave save, Vector3 position, Quaternion rotation)
    {
        if (animal.Prefab == null)
        {
            Debug.LogError($"AnimalManager: '{animal.name}' has no prefab, so there is nothing to instantiate", animal);
            return null;
        }

        GameObject instance = Instantiate(animal.Prefab, position, rotation, animalParent);
        instance.name = animal.Prefab.name;

        AnimalController controller = instance.GetComponent<AnimalController>();

        if (controller == null)
        {
            Debug.LogWarning($"AnimalManager: '{animal.Prefab.name}' has no AnimalController on its root, so " +
                $"the base one was added - add it to the prefab", animal.Prefab);

            // RequireComponent brings an EntityId along with it
            controller = instance.AddComponent<AnimalController>();
        }

        EntityId entityId = instance.GetComponent<EntityId>();

        // unreachable while AnimalController requires an EntityId; kept so losing that attribute is loud
        if (entityId == null)
        {
            Debug.LogError($"AnimalManager: '{animal.Prefab.name}' came out with no EntityId, so one was added " +
                $"- a puzzle it completes could not otherwise find it", animal.Prefab);
            entityId = instance.AddComponent<EntityId>();
        }

        controller.Bind(animal, entityId, save);

        entityId.Destroyed += OnAnimalDestroyed;

        live.Add(new LiveAnimal { animal = animal, entityId = entityId, transform = instance.transform, controller = controller, save = save });

        return entityId;
    }


    // only the live list: nothing destroys an animal on purpose yet, so the save is left alone
    private void OnAnimalDestroyed(EntityId entityId)
    {
        if (entityId != null) entityId.Destroyed -= OnAnimalDestroyed;

        // teardown order is not guaranteed, so the manager may already be gone
        if (instance != this) return;

        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].entityId != entityId) continue;

            live.RemoveAt(i);
            AnimalsChanged?.Invoke();
            return;
        }
    }
}
