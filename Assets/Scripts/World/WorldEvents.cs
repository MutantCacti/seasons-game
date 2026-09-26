using System;
using System.Collections.Generic;
using UnityEngine;


// The one place world events are fanned out to WorldListeners, and the WorldContext they act through.
// The listeners are every spawn rule and every active scripted puzzle (unlocked, not completed); a
// puzzle completed partway through an event stops hearing it straight away. It listens to placement,
// puzzle unlocks, the world states and the clock, and AnimalManager tells it about spawns.
//
// Nothing reaches a listener until the world has finished loading: puzzles, world states, entities,
// and a new world's construction. Then every listener hears OnLoaded once. An event raised while one
// is being dispatched is queued behind it, so a chain resolves in one frame without the listeners
// being walked twice at once, and a listener that throws is logged while the rest still hear it.
//
// Not placed in a scene: the first manager that needs it makes it (Ensure), so it is always there
// alongside them and goes away with their scene.
public class WorldEvents : MonoBehaviour, WorldContext
{
    private static WorldEvents instance;
    public static WorldEvents Instance { get { return instance; } }


    private PlacementController placementController;
    private PuzzleManager puzzleManager;
    private WorldStateManager worldStates;
    private SaveDataManager saveManager;
    private TerrainManager terrainManager;

    // true once every listener has heard OnLoaded; nothing is dispatched before
    private bool ready;

    private readonly Queue<Action<WorldListener>> pendingDispatches = new Queue<Action<WorldListener>>();
    private bool dispatching;

    // the listeners for the event being dispatched, refilled for each one. One list is enough, since
    // events are dispatched one after another, never inside each other
    private readonly List<WorldListener> snapshot = new List<WorldListener>();

    // scratch for the context's scans
    private readonly List<EntityId> scratchEntities = new List<EntityId>();
    private readonly List<Vector3Int> scratchCells = new List<Vector3Int>();
    private readonly List<Vector3> scratchPositions = new List<Vector3>();


    public TerrainManager Terrain { get { return terrainManager != null ? terrainManager : TerrainManager.Instance; } }


    public bool IsReady { get { return ready; } }


    // Sticky: raised once every listener has heard OnLoaded, i.e. from when the loaded world may
    // change. A late subscriber hears it straight away
    public event Action Ready
    {
        add
        {
            readied += value;
            if (ready && value != null) value();
        }
        remove
        {
            readied -= value;
        }
    }


    private event Action readied;
    public WorldStateManager States { get { return worldStates; } }


    // made on first need, at the scene root of whoever asked
    public static WorldEvents Ensure()
    {
        if (instance != null) return instance;

        GameObject holder = new GameObject("World Events");
        return holder.AddComponent<WorldEvents>();
    }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one WorldEvents in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }


    // Start rather than Awake: the managers it listens to are only guaranteed once every Awake has run
    private void Start()
    {
        terrainManager = TerrainManager.Instance;

        placementController = PlacementController.Instance;

        if (placementController != null) placementController.ItemPlaced += OnItemPlaced;
        else Debug.LogError("WorldEvents: no PlacementController, so no listener hears a placement", this);

        Subscribe();
    }


    // The sticky ones (puzzles loaded, world states loaded, season and conditions) answer at once if
    // they have already happened, which is why replayed states are dropped while not ready.
    //
    // Every reference is resolved before anything is subscribed, because a sticky event calls
    // TryBecomeReady on the spot and it must not see a manager as missing that is only unassigned
    private void Subscribe()
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
            Debug.LogError("WorldEvents: no WorldStateManager, so no listener hears a season, a condition or the time of day", this);
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
            Debug.LogError("WorldEvents: no PuzzleManager, so listeners are never gated on their puzzles", this);
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


    // MARK: readiness


    // Ready once puzzles have loaded, world states have loaded and a new world has finished being
    // built. Each of those calls this, and the last one through tells every listener OnLoaded
    private void TryBecomeReady()
    {
        if (ready) return;

        if (puzzleManager != null && !puzzleManager.IsLoaded) return;
        if (saveManager != null && saveManager.IsConstructingNewWorld) return;

        // a listener's OnLoaded scan must see the placed entities
        if (EntityManager.Instance != null) EntityManager.Instance.EnsureLoaded();

        ready = true;

        Dispatch(listener => listener.OnLoaded(this));

        readied?.Invoke();
    }


    // MARK: dispatch


    // Walks every listener with the hook. A hook that dispatches again is queued behind this one
    private void Dispatch(Action<WorldListener> hook)
    {
        if (!ready) return;

        pendingDispatches.Enqueue(hook);

        if (dispatching) return;
        dispatching = true;

        try
        {
            while (pendingDispatches.Count > 0)
            {
                Action<WorldListener> next = pendingDispatches.Dequeue();

                CollectListeners();

                for (int i = 0; i < snapshot.Count; i++)
                {
                    WorldListener listener = snapshot[i];
                    if (listener == null) continue;

                    // completed by an earlier listener in this same event
                    if (listener is Puzzle puzzle && !IsListening(puzzle)) continue;

                    try
                    {
                        next(listener);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, listener);
                    }
                }
            }
        }
        finally
        {
            dispatching = false;
            snapshot.Clear();
        }
    }


    // who hears the event about to be dispatched
    private void CollectListeners()
    {
        snapshot.Clear();

        AnimalManager animals = AnimalManager.Instance;
        IReadOnlyList<AnimalSpawnRule> rules = animals != null ? animals.SpawnRules : null;

        if (rules != null)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] != null) snapshot.Add(rules[i]);
            }
        }

        PuzzleRegistry registry = puzzleManager != null ? puzzleManager.Registry : null;
        IReadOnlyList<Puzzle> puzzles = registry != null ? registry.Entries : null;

        if (puzzles == null) return;

        for (int i = 0; i < puzzles.Count; i++)
        {
            if (IsListening(puzzles[i])) snapshot.Add(puzzles[i]);
        }
    }


    // only a scripted puzzle has anything to hear, and only while it is active
    private bool IsListening(Puzzle puzzle)
    {
        return puzzle is ScriptedPuzzle && puzzleManager != null && puzzleManager.IsActive(puzzle);
    }


    // MARK: events


    private void OnItemPlaced(GameItem item, Vector3Int cell, GameObject placed)
    {
        Dispatch(listener => listener.OnItemPlaced(this, item, cell));
    }


    // only to the listeners it belongs to: the puzzle itself, and the spawn rules that complete it
    private void OnPuzzleUnlocked(Puzzle puzzle)
    {
        Dispatch(listener =>
        {
            bool belongs = listener == puzzle || (listener is AnimalSpawnRule rule && rule.Completes == puzzle);
            if (belongs) listener.OnPuzzleUnlocked(this);
        });
    }


    // AnimalManager calls this once a spawn has been saved and credited
    public void RaiseAnimalSpawned(Animal animal, EntityId entity)
    {
        Dispatch(listener => listener.OnAnimalSpawned(this, animal, entity));
    }


    private void OnSeasonChanged(Season season)
    {
        Dispatch(listener => listener.OnSeasonChanged(this, season));
    }


    private void OnConditionChanged(WorldCondition condition, ConditionPhase phase)
    {
        Dispatch(listener => listener.OnConditionChanged(this, condition, phase));
    }


    private void OnSunrise() { Dispatch(listener => listener.OnSunrise(this)); }
    private void OnNoon() { Dispatch(listener => listener.OnNoon(this)); }
    private void OnSunset() { Dispatch(listener => listener.OnSunset(this)); }
    private void OnMidnight() { Dispatch(listener => listener.OnMidnight(this)); }


    // MARK: context: spawning


    public int Count(Animal animal)
    {
        return AnimalManager.Instance != null ? AnimalManager.Instance.Count(animal) : 0;
    }


    public bool CanSpawn(AnimalSpawnRule rule)
    {
        return AnimalManager.Instance != null && AnimalManager.Instance.CanSpawn(rule);
    }


    public bool Spawn(AnimalSpawnRule rule, Vector3 position)
    {
        return AnimalManager.Instance != null && AnimalManager.Instance.Spawn(rule, position);
    }


    // MARK: context: puzzles


    public int CountPlaced(GameItem item)
    {
        if (item == null) return 0;

        TerrainManager terrain = Terrain;

        if (item.Tile != null && terrain != null)
        {
            terrain.CollectPlayerCellsOf(item.Tile, scratchCells);
            return scratchCells.Count;
        }

        return EntityManager.Instance != null ? EntityManager.Instance.CountPlaced(item) : 0;
    }


    public void CollectPlacedCells(GameItem item, List<Vector3Int> results)
    {
        results.Clear();

        TerrainManager terrain = Terrain;
        if (item == null || item.Tile == null || terrain == null) return;

        terrain.CollectPlayerCellsOf(item.Tile, results);
    }


    public bool Complete(Puzzle puzzle, PuzzleCredit credit)
    {
        return puzzleManager != null && puzzleManager.Complete(puzzle, credit);
    }


    // MARK: context: queries


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

        TerrainManager terrain = Terrain;

        if (item.Tile != null && terrain != null)
        {
            terrain.CollectCellsOf(item.Tile, scratchCells);

            for (int i = 0; i < scratchCells.Count; i++) results.Add(terrain.CellToWorld(scratchCells[i]));
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

        TerrainManager terrain = Terrain;
        if (terrain == null) return false;

        Camera view = offScreen ? Camera.main : null;

        if (offScreen && view == null)
        {
            Debug.LogError("WorldEvents: no main camera, so an off-screen spawn point cannot be picked", this);
            return false;
        }

        terrain.GetWorldXZBounds(out Vector2 min, out Vector2 max);

        scratchPositions.Clear();

        for (int z = Mathf.FloorToInt(min.y); z < Mathf.CeilToInt(max.y); z++)
        {
            for (int x = Mathf.FloorToInt(min.x); x < Mathf.CeilToInt(max.x); x++)
            {
                int level = terrain.GetTopLevel(x, z);
                if (level < 0) continue;

                Vector3Int cell = new Vector3Int(x, level, z);
                if (accept != null && !accept(terrain.GetTile(cell))) continue;

                Vector3 point = terrain.CellToWorld(cell);

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
}
