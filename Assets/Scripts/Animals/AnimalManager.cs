using System;
using System.Collections.Generic;
using UnityEngine;


// Owns the animals in the world, their section of the save, and the spawn rules that bring them in.
// Rules are data (AnimalSpawnRule assets); this forwards world events to them and does the spawning
// on their behalf, so every animal is saved, registered and credited the same way.
public class AnimalManager : MonoBehaviour, AnimalSpawnContext
{
    private static AnimalManager instance;
    public static AnimalManager Instance { get { return instance; } }


    [SerializeField] private AnimalRegistry registry;
    [SerializeField] private TerrainManager terrainManager;
    [SerializeField] private List<AnimalSpawnRule> rules = new List<AnimalSpawnRule>();

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

        // the row this animal's transform is copied into. AnimalSaveData.Sort reorders the list but
        // never replaces the rows, so this stays the one in the save
        public AnimalSave save;
    }


    private readonly List<LiveAnimal> live = new List<LiveAnimal>();

    // cached for the same reason EntityManager caches it: only a new game or a checkpoint restore ever
    // swaps SaveData.world out, and both reload the scene
    private WorldState world;

    private PlacementController placementController;


    public TerrainManager Terrain { get { return terrainManager; } }


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
    }


    private void OnDestroy()
    {
        if (placementController != null) placementController.ItemPlaced -= OnItemPlaced;

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


    public bool HasFired(AnimalSpawnRule rule)
    {
        if (rule == null) return false;
        if (world == null || world.animals == null || world.animals.firedRules == null) return false;

        return world.animals.firedRules.Contains(rule.Id);
    }


    // MARK: rules


    private void OnItemPlaced(GameItem item, Vector3Int cell, GameObject instance)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            AnimalSpawnRule rule = rules[i];
            if (rule == null) continue;

            rule.OnItemPlaced(this, item, cell);
        }
    }


    // MARK: spawn


    // The one way a new animal enters the world. Marks the rule as fired, saves, and only then submits
    // the rule's puzzle, so the completion's guid already resolves and the save already holds the animal.
    public bool Spawn(AnimalSpawnRule rule, Vector3 position)
    {
        if (rule == null) return false;

        if (!rules.Contains(rule))
        {
            Debug.LogError($"AnimalManager: '{rule.name}' tried to spawn but is not in the rules list", rule);
            return false;
        }

        if (HasFired(rule)) return false;

        Animal animal = rule.Animal;

        if (animal == null)
        {
            Debug.LogError($"AnimalManager: '{rule.name}' has no animal, so there is nothing to spawn", rule);
            return false;
        }

        // logs and gives up if the animal is not in the registry
        if (registry == null || !registry.TryGetIdByEntry(animal, out string animalId)) return false;

        AnimalSave save = new AnimalSave { animalId = animalId, position = position };

        EntityId entityId = CreateInstance(animal, save, position, animal.Prefab != null ? animal.Prefab.transform.rotation : Quaternion.identity);
        if (entityId == null) return false;

        // reading the guid here is what gives a freshly spawned animal its identity
        save.guid = entityId.Guid;
        save.rotation = entityId.transform.rotation;

        world.animals.animals.Add(save);

        world.animals.firedRules.Add(rule.Id);

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

        return true;
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

        live.Add(new LiveAnimal { animal = animal, entityId = entityId, transform = instance.transform, save = save });

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
