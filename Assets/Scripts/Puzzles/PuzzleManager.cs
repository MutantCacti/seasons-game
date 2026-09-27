using System;
using System.Collections.Generic;
using UnityEngine;



// world scripts call Complete(puzzle, source) when they decide the condition has been met
// This manager is the only thing allowed to say no


// One ingredient a batch of unlocks handed out
public readonly struct IngredientGrant
{
    public readonly GameItem item;
    public readonly int count;


    public IngredientGrant(GameItem item, int count)
    {
        this.item = item;
        this.count = count;
    }
}


// Where a batch of granted ingredients flies from: what completed the puzzle that unlocked them. An
// entity is read where it is when the fliers launch, so a moving animal is followed; a credited cell
// is a fixed point. None for the starting puzzles, which nothing completed
public readonly struct GrantOrigin
{
    public readonly EntityId entity;
    public readonly bool hasPoint;
    public readonly Vector3 point;


    private GrantOrigin(EntityId entity, bool hasPoint, Vector3 point)
    {
        this.entity = entity;
        this.hasPoint = hasPoint;
        this.point = point;
    }


    public static GrantOrigin None { get { return default; } }
    public static GrantOrigin Entity(EntityId entity) { return new GrantOrigin(entity, false, default); }
    public static GrantOrigin Point(Vector3 point) { return new GrantOrigin(null, true, point); }


    // false for none, and for an entity that has since gone
    public bool TryGetPosition(out Vector3 position)
    {
        if (entity != null)
        {
            // the entity's own marker when its prefab has one, so the fliers leave from the right point
            position = PingTarget.Resolve(entity).position;
            return true;
        }

        position = point;
        return hasPoint;
    }
}

public class PuzzleManager : MonoBehaviour
{
    private static PuzzleManager instance;
    public static PuzzleManager Instance { get { return instance; } }


    [SerializeField] private PuzzleRegistry registry;

    [SerializeField] private InventoryManager inventoryManager;

    [SerializeField] private PlacementController placementController;

    // How long the manager waits after the last consumption before deciding a puzzle is stuck
    [SerializeField] private float softLockGraceSeconds = 0.5f;


    public event Action<Puzzle> OnPuzzleComplete;

    public event Action<Puzzle> OnPuzzleUnlocked;

    public event Action<Puzzle> OnPuzzleSoftLocked;

    // Raised when a puzzle that had been flagged as stuck is let go of again, because the player turned
    // out to still be able to act on it. The flag is already gone by the time this runs
    public event Action<Puzzle> OnPuzzleSoftLockCleared;


    // Raised once per unlock batch, after it is saved, with the entity whose completion caused it
    // (null for the starting puzzles, or when that entity is gone) and every ingredient it granted.
    // Static so a listener never misses the new-world seed in Start over Awake order. That also means
    // it outlives the scene: every subscriber MUST += in OnEnable and -= in OnDisable, or a reload
    // leaves a dead listener behind. While anyone listens, grants defer their hotbar display until the
    // listener calls InventoryManager.ReleaseDisplay, so a listener must release everything it holds.
    public static event Action<GrantOrigin, IReadOnlyList<IngredientGrant>> IngredientsGranted;


    // Raised once puzzle state has been restored, which is the one safe moment for a world script to
    // ask IsActive/IsCompleted: before this, a restored puzzle reads as inactive.
    //
    // Sticky on purpose: a world script that subscribes later - a tree placed after loading finished,
    // say - is answered immediately instead of waiting for an event that has already been and gone.
    public event Action OnPuzzlesLoaded
    {
        add
        {
            puzzlesLoaded += value;

            if (isLoaded && value != null) value();
        }
        remove
        {
            puzzlesLoaded -= value;
        }
    }


    private event Action puzzlesLoaded;

    private bool isLoaded;


    private readonly HashSet<Puzzle> unlocked = new HashSet<Puzzle>();
    private readonly HashSet<Puzzle> completed = new HashSet<Puzzle>();

    // puzzle -> the guid of the entity that completed it. The runtime state is the source of truth:
    // every save of this section is rebuilt from here by WriteThrough.
    private readonly Dictionary<Puzzle, string> completedBy = new Dictionary<Puzzle, string>();

    // puzzle -> guid of the entity that completed it early ("" when there was no source). a
    // completion that arrives before its puzzle is unlocked, or mid-batch, waits here and is replayed
    // through the normal completion path once the puzzle is unlocked and checkpointed
    private readonly Dictionary<Puzzle, string> pending = new Dictionary<Puzzle, string>();

    // true while UnlockBatch is unlocking, so a completion raised from an OnPuzzleUnlocked handler
    // queues instead of landing before the batch's checkpoint is captured
    private bool batching;

    // who the current GrantUnlocks is paying out for, and what the current batch has granted.
    // both are saved and restored around nesting like batching is
    private GrantOrigin grantSource;
    private List<IngredientGrant> grants;

    // puzzles the manager has settled on being stuck, and therefore announced
    private readonly HashSet<Puzzle> softLocked = new HashSet<Puzzle>();

    // active puzzles waiting out the grace period before being judged
    private readonly HashSet<Puzzle> pendingSoftLockChecks = new HashSet<Puzzle>();

    private readonly PuzzleLedger ledger = new PuzzleLedger();

    private readonly List<Puzzle> charged = new List<Puzzle>();

    // scratch for the growth-pending scan
    private readonly List<EntityManager.LiveEntity> scratchLive = new List<EntityManager.LiveEntity>();

    // scratch for the forward walk from one ingredient: what it can still become, and what has been
    // queued already
    private readonly List<GameItem> growthPendingQueue = new List<GameItem>();
    private readonly HashSet<GameItem> growthPendingVisited = new HashSet<GameItem>();

    // a guard against a growsInto or reaction cycle, which the item registry's validation reports
    private const int MAX_GROWTH_CHAIN = 32;

    // puzzle -> every registry puzzle whose unlocks list it, so a waited-for puzzle can be walked back
    // to whatever could still unlock it. built once, the registry doesn't change at runtime
    private Dictionary<Puzzle, List<Puzzle>> unlockedBy;

    // scratch for the reachability walk
    private readonly HashSet<Puzzle> reachVisited = new HashSet<Puzzle>();
    private readonly List<Puzzle> reachStack = new List<Puzzle>();

    private bool completionEnabled = true;

    private bool checkpointManagerWarningLogged;

    private float softLockCheckAt;
    private bool softLockCheckScheduled;

    private bool warnedAboutMissingNotifications;

    private bool warnedAboutMissingWorldStates;


    public bool CompletionEnabled { get { return completionEnabled; } }
    public PuzzleRegistry Registry { get { return registry; } }
    public int ActiveCount { get { return unlocked.Count - completed.Count; } }
    public bool IsLoaded { get { return isLoaded; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one PuzzleManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (registry == null)
        {
            Debug.LogError("PuzzleManager: no PuzzleRegistry assigned, so no puzzle can ever be resolved", this);
        }
    }


    private void Start()
    {
        if (inventoryManager == null) inventoryManager = InventoryManager.Instance;

        if (inventoryManager == null)
        {
            Debug.LogError("PuzzleManager: no InventoryManager, so unlocking a puzzle cannot grant its ingredients", this);
        }
        else
        {
            inventoryManager.ItemConsumed += OnItemConsumed;

            // A puzzle held off because the player is holding a reagent has to be judged again the
            // moment they spend it, or the moment another puzzle hands them one
            inventoryManager.SlotsChanged += OnSlotsChanged;
        }

        if (placementController == null) placementController = PlacementController.Instance;

        if (placementController == null)
        {
            Debug.LogError("PuzzleManager: no PlacementController, so a soft-lock judgement cannot wait for a " +
                "tile transition to finish and may fail a puzzle that is still growing", this);
        }
        else
        {
            placementController.ItemShown += OnPlacementShown;
        }

        // a spawn spends nothing, but it can end a puzzle's settling, so it is judged again
        if (AnimalManager.Instance != null) AnimalManager.Instance.AnimalSpawned += OnAnimalSpawned;

        LoadFromSave();
    }


    private void OnDestroy()
    {
        if (inventoryManager != null)
        {
            inventoryManager.ItemConsumed -= OnItemConsumed;
            inventoryManager.SlotsChanged -= OnSlotsChanged;
        }

        if (placementController != null) placementController.ItemShown -= OnPlacementShown;

        if (AnimalManager.Instance != null) AnimalManager.Instance.AnimalSpawned -= OnAnimalSpawned;

        if (instance == this) instance = null;
    }


    // Only does anything once a soft-lock judgement has been queued
    private void Update()
    {
        if (!softLockCheckScheduled) return;
        if (Time.time < softLockCheckAt) return;

        softLockCheckScheduled = false;

        EvaluateSoftLocks();
    }


    // MARK: queries


    public bool IsUnlocked(Puzzle puzzle)
    {
        return puzzle != null && unlocked.Contains(puzzle);
    }


    public bool IsCompleted(Puzzle puzzle)
    {
        return puzzle != null && completed.Contains(puzzle);
    }


    // Unlocked but not yet completed
    public bool IsActive(Puzzle puzzle)
    {
        return IsUnlocked(puzzle) && !IsCompleted(puzzle);
    }


    // Completed early and waiting for the puzzle to unlock (or for the current unlock batch to finish)
    public bool IsPending(Puzzle puzzle)
    {
        return puzzle != null && pending.ContainsKey(puzzle);
    }


    // True once the manager has settled on the puzzle being stuck. Deliberately not the same as "the
    // ledger is exhausted": that goes true the instant the last ingredient is spent, which is usually
    // before the placement that spent it has finished being handled.
    public bool IsSoftLocked(Puzzle puzzle)
    {
        return puzzle != null && softLocked.Contains(puzzle);
    }


    // For debug and UI, returns how many ingredients a puzzle's soft lock
    // is still waiting for
    public int RemainingIngredient(Puzzle puzzle, GameItem item)
    {
        return ledger.Remaining(puzzle, item);
    }


    // Which entity completed this puzzle. False when the completion was never attributed to one, when
    // the entity that did it is no longer in the world, or when it was credited to a cell instead
    public bool TryGetCompletedBy(Puzzle puzzle, out EntityId entity)
    {
        entity = null;

        if (puzzle == null) return false;

        if (!completedBy.TryGetValue(puzzle, out string guid)) return false;

        if (PuzzleCredit.IsCellKey(guid)) return false;

        if (EntityManager.Instance == null) return false;

        return EntityManager.Instance.TryGetEntity(guid, out entity);
    }


    // What this puzzle was credited to, as a credit another completion can reuse: the entity when it is
    // still in the world (growth keeps the guid, so a sapling that became a tree still counts), else the
    // cell. False when there is neither
    public bool TryGetCredit(Puzzle puzzle, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (puzzle == null || !completedBy.TryGetValue(puzzle, out string key)) return false;

        if (TryGetCompletedBy(puzzle, out EntityId entity) && entity != null)
        {
            credit = PuzzleCredit.Entity(entity);
            return true;
        }

        TerrainManager terrain = TerrainManager.Instance;
        if (terrain == null) return false;
        if (!PuzzleCredit.TryParseCellKey(key, out int x, out int levelMm, out int z)) return false;
        if (!terrain.TryGetLevelAtMillimetres(levelMm, out int level)) return false;

        credit = PuzzleCredit.Cell(new Vector3Int(x, level, z));
        return true;
    }


    // every ingredient this active puzzle was given has been spent
    public bool IsExhausted(Puzzle puzzle)
    {
        return ledger.IsExhausted(puzzle);
    }


    // the fliers for what a completion unlocks come from whatever completed it, entity or cell
    private GrantOrigin CreditOrigin(Puzzle puzzle)
    {
        if (!TryGetCreditTarget(puzzle, out EntityId entity, out Vector3 point)) return GrantOrigin.None;

        return entity != null ? GrantOrigin.Entity(entity) : GrantOrigin.Point(point);
    }


    // Where to go to see what completed this puzzle: the entity, or failing that the credited cell's
    // top. False when there is neither
    public bool TryGetCreditTarget(Puzzle puzzle, out EntityId entity, out Vector3 point)
    {
        point = default;

        if (TryGetCompletedBy(puzzle, out entity) && entity != null)
        {
            point = entity.transform.position;
            return true;
        }

        if (puzzle == null || !completedBy.TryGetValue(puzzle, out string key)) return false;

        return PuzzleCredit.TryResolveCellKey(key, TerrainManager.Instance, out point);
    }


    // MARK: gate


    // basically lets you disable puzzles briefly
    // will need to be used for checkpoint resets and game loading
    // because terrain remapping remaps tiles one by one
    // and that could probably totally fuck with some puzzles
    public void SetCompletionEnabled(bool enabled)
    {
        completionEnabled = enabled;
    }


    // MARK: completion


    // the same, with a PuzzleCredit: what a scripted puzzle completes with. A cell goes on as its key
    // wherever a guid would, so the queue, completedBy and the ping all carry it unchanged
    public bool Complete(Puzzle puzzle, PuzzleCredit credit)
    {
        if (!credit.hasCell) return Complete(puzzle, credit.entity);

        if (!completionEnabled) return false;
        if (puzzle == null) return false;

        string key = PuzzleCredit.ToCellKey(credit.cell, TerrainManager.Instance);

        if (key == null) Debug.LogError($"PuzzleManager: '{puzzle.name}' was credited to cell {credit.cell}, which " +
            $"is not on this terrain, so nothing is recorded as having completed it", this);

        return CompleteInternal(puzzle, key, null);
    }


    // single entry point, every world script should use this to submit puzzle completion
    // returns true only when the puzzle was completed right now. false when puzzles are disabled,
    // the puzzle is null or already completed, or the completion was queued: a puzzle that is not
    // unlocked yet (or is mid unlock batch) remembers it and completes as soon as it is unlocked
    //
    // source names the entity that met the condition. It may be null, in which case nothing is
    // recorded and the completion counts for nothing but the save's completed list.
    public bool Complete(Puzzle puzzle, EntityId source)
    {
        if (!completionEnabled) return false;

        if (puzzle == null) return false;

        string guid = null;

        // a completion with no source is legal, it simply records nothing about who did it
        if (source != null)
        {
            if (source.HasGuid) guid = source.Guid;
            else Debug.LogError($"PuzzleManager: '{puzzle.name}' was completed by an entity with no guid, " +
                $"so the save cannot record which entity did it", source);
        }

        return CompleteInternal(puzzle, guid, source);
    }


    // guid is what completedBy records, source is only the ping's target. they are split so a pending
    // completion can be flushed with its saved guid while its entity is not resolvable yet - on load
    // the flush runs in PuzzleManager.Start, which can be before AnimalManager.Start has registered
    // the animal that did it. the ping retries resolving the guid by itself.
    private bool CompleteInternal(Puzzle puzzle, string guid, EntityId source)
    {
        if (completed.Contains(puzzle)) return false;

        if (batching || !unlocked.Contains(puzzle))
        {
            Enqueue(puzzle, guid);
            return false;
        }

        // all this must happen before the event is invoked
        completed.Add(puzzle);
        ledger.Forget(puzzle);
        pending.Remove(puzzle);

        // no longer stuck, and no longer worth judging
        softLocked.Remove(puzzle);
        pendingSoftLockChecks.Remove(puzzle);

        if (!string.IsNullOrEmpty(guid)) completedBy[puzzle] = guid;

        // The next puzzles now unlock when the player opens the completion ping, not here. Without a
        // NotificationManager (a test scene, say) they unlock straight away so progress never stalls.
        NotificationManager notifications = NotificationManager.Instance;

        if (notifications == null)
        {
            if (!warnedAboutMissingNotifications)
            {
                warnedAboutMissingNotifications = true;
                Debug.LogWarning("PuzzleManager: no NotificationManager in the scene, so completed puzzles " +
                    "unlock the next ones immediately instead of through a ping", this);
            }

            GrantUnlocks(puzzle);
        }

        WriteThrough();

        // here rather than from OnPuzzleComplete, which runs after the save: a quit in between would
        // keep the completion and lose the condition for good. StartCondition saves the whole save,
        // completion included, so both reach disk together
        StartConditions(puzzle);

        // after the write-through, so the notification never reaches disk ahead of the completion
        if (notifications != null)
        {
            notifications.Withdraw(NotificationType.FailPuzzle, puzzle);

            // a flushed completion may only have the saved guid, and the ping resolves that itself
            if (source != null) notifications.Submit(NotificationType.CompletePuzzle, source, puzzle);
            else notifications.Submit(NotificationType.CompletePuzzle, guid, puzzle);
        }

        Save();

        OnPuzzleComplete?.Invoke(puzzle);

        return true;
    }


    // Starts the world conditions a completion brings on. Only the live completion gets here: a load
    // restores completed puzzles without this path, and the conditions are already in the saved states
    private void StartConditions(Puzzle puzzle)
    {
        IReadOnlyList<WorldCondition> conditions = puzzle.StartsConditions;
        if (conditions == null || conditions.Count == 0) return;

        WorldStateManager worldStates = WorldStateManager.Instance;

        if (worldStates == null)
        {
            if (!warnedAboutMissingWorldStates)
            {
                warnedAboutMissingWorldStates = true;
                Debug.LogError($"PuzzleManager: no WorldStateManager, so '{puzzle.name}' completed without " +
                    $"starting its world conditions", this);
            }

            return;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            worldStates.StartCondition(conditions[i]);
        }
    }


    // Unlocks everything the given puzzle unlocks. Does nothing unless that puzzle is actually
    // completed, so get it wrong the other way round and world scripts will simply be told no.
    public void GrantUnlocks(Puzzle completedPuzzle)
    {
        if (completedPuzzle == null) return;
        if (!completed.Contains(completedPuzzle)) return;

        IReadOnlyList<Puzzle> unlocks = completedPuzzle.Unlocks;
        if (unlocks == null || unlocks.Count == 0) return;

        // Collected into a list first so the batch has a single obvious input and this loop cannot be
        // affected by the unlocking that follows it.
        List<Puzzle> candidates = new List<Puzzle>();

        for (int i = 0; i < unlocks.Count; i++)
        {
            candidates.Add(unlocks[i]);
        }

        GrantOrigin outerSource = grantSource;
        grantSource = CreditOrigin(completedPuzzle);

        try
        {
            UnlockBatch(candidates);
        }
        finally
        {
            grantSource = outerSource;
        }
    }


    // The one place puzzles become unlocked. Write-through and save happen once for the whole batch,
    // and only when something in it was actually new.
    private void UnlockBatch(IReadOnlyList<Puzzle> puzzles)
    {
        if (puzzles == null) return;

        List<Puzzle> newlyUnlocked = new List<Puzzle>();

        GrantOrigin source = grantSource;
        List<IngredientGrant> outerGrants = grants;
        List<IngredientGrant> batchGrants = new List<IngredientGrant>();
        grants = batchGrants;

        // an OnPuzzleUnlocked handler that completes a puzzle now queues it, so the completion lands
        // after this batch's checkpoint rather than inside it
        bool wasBatching = batching;
        batching = true;

        try
        {
            for (int i = 0; i < puzzles.Count; i++)
            {
                // Unlock only returns true for a puzzle that was not unlocked before, so this list is
                // exactly the puzzles this batch added
                if (Unlock(puzzles[i])) newlyUnlocked.Add(puzzles[i]);
            }
        }
        finally
        {
            batching = wasBatching;
            grants = outerGrants;
        }

        if (newlyUnlocked.Count == 0)
        {
            FlushPending();
            return;
        }

        WriteThrough();

        if (CheckpointManager.Instance != null)
        {
            CheckpointManager.Instance.Capture(newlyUnlocked);
        }
        else if (!checkpointManagerWarningLogged)
        {
            checkpointManagerWarningLogged = true;
            Debug.LogWarning("PuzzleManager: no CheckpointManager in the scene, so no checkpoint was captured");
        }

        Save();

        // after the save, so whatever the listener shows is already safe on disk
        if (batchGrants.Count > 0) IngredientsGranted?.Invoke(source, batchGrants);

        // after the capture on purpose: the checkpoint still holds these as pending, so resetting to
        // it replays the completion instead of losing it
        FlushPending();

        // a puzzle held while it waited for one of these is judged normally now. the grant's
        // SlotsChanged usually asks too, but not for a puzzle with no ingredients
        ScheduleSoftLockCheck();
    }


    // MARK: pending


    // Remembers a completion that cannot happen yet. The first one wins, so the entity that got there
    // first keeps the credit. Inside a batch nothing is written: UnlockBatch writes once for the whole
    // batch, and a save from here would put a half-unlocked batch on disk.
    private void Enqueue(Puzzle puzzle, string guid)
    {
        if (pending.ContainsKey(puzzle)) return;

        pending.Add(puzzle, guid ?? "");

        Debug.Log($"PuzzleManager: '{puzzle.name}' is not active yet, so its completion was queued " +
            $"until it unlocks", puzzle);

        if (batching) return;

        WriteThrough();
        Save();
    }


    // Replays every queued completion whose puzzle is now unlocked, through the normal completion
    // path so it gets its ping, completedBy and events. Iterates a snapshot because each completion can
    // unlock (and flush) more puzzles further down this call stack.
    private void FlushPending()
    {
        if (!completionEnabled) return;
        if (batching) return;
        if (pending.Count == 0) return;

        List<KeyValuePair<Puzzle, string>> ready = new List<KeyValuePair<Puzzle, string>>();

        foreach (KeyValuePair<Puzzle, string> entry in pending)
        {
            if (entry.Key != null && unlocked.Contains(entry.Key)) ready.Add(entry);
        }

        for (int i = 0; i < ready.Count; i++)
        {
            Puzzle puzzle = ready[i].Key;
            string guid = ready[i].Value;

            // a nested flush may already have done this one
            if (!pending.ContainsKey(puzzle)) continue;

            EntityId source = null;

            // a cell credit has no entity to find; its key still goes to completedBy and the ping
            if (!string.IsNullOrEmpty(guid) && !PuzzleCredit.IsCellKey(guid) && EntityManager.Instance != null)
            {
                EntityManager.Instance.TryGetEntity(guid, out source);
            }

            // no source is fine: the saved guid still goes to completedBy and the ping
            CompleteInternal(puzzle, string.IsNullOrEmpty(guid) ? null : guid, source);
        }
    }


    // MARK: unlock


    // Grants a puzzle's ingredients and makes it active
    private bool Unlock(Puzzle puzzle)
    {
        if (puzzle == null) return false;

        if (unlocked.Contains(puzzle)) return false;

        unlocked.Add(puzzle);
        ledger.Track(puzzle);

        Grant(puzzle);

        OnPuzzleUnlocked?.Invoke(puzzle);

        return true;
    }


    private void Grant(Puzzle puzzle)
    {
        if (inventoryManager == null) return;

        IReadOnlyList<Ingredient> ingredients = puzzle.Ingredients;
        if (ingredients == null) return;

        for (int i = 0; i < ingredients.Count; i++)
        {
            Ingredient ingredient = ingredients[i];

            if (ingredient.item == null)
            {
                Debug.LogError($"PuzzleManager: '{puzzle.name}' lists an empty ingredient, so there is nothing to grant", puzzle);
                continue;
            }

            if (ingredient.count <= 0)
            {
                Debug.LogError($"PuzzleManager: '{puzzle.name}' lists {ingredient.count}x " +
                    $"'{ingredient.item.name}', so there is nothing to grant", puzzle);
                continue;
            }

            // the display only waits when something will fly the icons in and release it
            bool deferDisplay = IngredientsGranted != null && grants != null;

            // A full inventory refuses here and the ingredient is lost
            if (!inventoryManager.Add(ingredient.item, ingredient.count, deferDisplay))
            {
                Debug.LogError($"PuzzleManager: could not grant {ingredient.count}x '{ingredient.item.name}' for '{puzzle.name}' (inventoryManager.Add returned false)", this);
                continue;
            }

            if (grants != null) grants.Add(new IngredientGrant(ingredient.item, ingredient.count));
        }
    }


    // MARK: consumption


    private void OnItemConsumed(GameItem item, int quantity)
    {
        ledger.Charge(item, quantity, charged);

        if (charged.Count == 0) return;

        WriteThrough();

        // Judged a grace period from now rather than immediately: the placement that just spent the
        // final ingredient can complete the puzzle further down this very call stack
        ScheduleSoftLockCheck();
    }


    // The ground has settled after a placement, so a puzzle held back while its tile grew is judged
    // now, against the world the player can finally see
    private void OnPlacementShown(GameItem item, Vector3Int cell)
    {
        ScheduleSoftLockCheck();
    }


    // An animal arrived, which may be the last one a settling puzzle was waiting for, or may fill the
    // mob cap so it can never settle
    private void OnAnimalSpawned(Animal animal)
    {
        ScheduleSoftLockCheck();
    }


    // The inventory moved: something was granted or spent, which is what decides whether the player is
    // still holding a reagent a live entity would react with
    private void OnSlotsChanged()
    {
        ScheduleSoftLockCheck();
    }


    // Queues every active puzzle to be judged once the world has settled. Re-queueing one that is
    // already flagged is harmless, because the flag is only raised on the transition. Public so growth
    // can have a puzzle that was waiting on it judged again once the grown things exist
    public void ScheduleSoftLockCheck()
    {
        pendingSoftLockChecks.Clear();

        foreach (Puzzle puzzle in unlocked)
        {
            if (completed.Contains(puzzle)) continue;

            // about to complete, so never stuck
            if (pending.ContainsKey(puzzle)) continue;

            pendingSoftLockChecks.Add(puzzle);
        }

        if (pendingSoftLockChecks.Count == 0) return;

        softLockCheckAt = Time.time + softLockGraceSeconds;
        softLockCheckScheduled = true;
    }


    private void EvaluateSoftLocks()
    {
        // a flag going up or down can decide whether another puzzle's wait is still reachable, so that
        // one is judged again. deferred to after the loop: scheduling clears the set being walked
        bool rejudge = false;

        foreach (Puzzle puzzle in pendingSoftLockChecks)
        {
            // a puzzle completed during the grace period was never stuck
            if (!IsActive(puzzle)) continue;
            if (IsPending(puzzle)) continue;

            // Not stuck, so any saved fail notification is left over from a save that disagrees with
            // the ledger. A fail ping cannot be dismissed, so without this it would stay forever.
            if (!ledger.IsExhausted(puzzle))
            {
                if (NotificationManager.Instance != null) NotificationManager.Instance.Withdraw(NotificationType.FailPuzzle, puzzle);
                continue;
            }

            // Spent, but some of it is still growing into what the puzzle may want, so it isn't judged
            // yet. GrowthManager asks again once anything grows. An existing flag or ping is left alone
            if (IsGrowthPending(puzzle)) continue;

            // Spent, but the world is still bringing what completes it (bees moving in). Skipped, not
            // cleared: settling only ever goes true to false, so a flag raised without it stays right
            if (puzzle is ScriptedPuzzle scripted && scripted.IsSettling(WorldEvents.Instance)) continue;

            // Spent, but the ground under it is still changing, so the thing that completes it may not
            // have appeared yet. ItemShown asks again once the world has settled. An existing flag or
            // ping is left alone
            if (IsPresenting()) continue;

            // Spent, but a puzzle it waits for can still unlock and bring what it needs. Like the reagent
            // check below this can become true after the puzzle was flagged (a parent's flag clears, or
            // a save from before waits existed), so it un-flags and withdraws too
            if (IsWaitingOnUnlock(puzzle))
            {
                if (ClearSoftLock(puzzle)) rejudge = true;
                continue;
            }

            // Spent, but the player is still holding something that would react with what's left on the
            // ground, so the puzzle is not provably stuck: they can still act. Unlike growth and presenting
            // above, this condition can become true AFTER the puzzle was flagged - a later puzzle grants
            // the reagent - so it has to un-flag and withdraw, not just skip. SlotsChanged asks again
            // whenever the inventory moves
            if (IsReagentInHand(puzzle))
            {
                if (ClearSoftLock(puzzle)) rejudge = true;
                continue;
            }

            if (!softLocked.Add(puzzle)) continue;

            rejudge = true;

            Debug.LogWarning($"PuzzleManager: '{puzzle.name}' has spent every ingredient it was given " +
                $"and is still incomplete, so it is flagged as soft locked", puzzle);

            OnPuzzleSoftLocked?.Invoke(puzzle);

            // The ping points at the book, which is an entity: its EntityId is on the prefab root, the
            // same object as CompendiumUIManager. Missing, the ping parks above the inventory instead.
            if (NotificationManager.Instance != null)
            {
                EntityId compendium = CompendiumUIManager.Instance != null
                    ? CompendiumUIManager.Instance.GetComponentInParent<EntityId>()
                    : null;

                NotificationManager.Instance.Submit(NotificationType.FailPuzzle, compendium, puzzle);
            }
        }

        pendingSoftLockChecks.Clear();

        if (rejudge) ScheduleSoftLockCheck();
    }


    // Lets go of a puzzle the player can still act on. True when it had been flagged
    private bool ClearSoftLock(Puzzle puzzle)
    {
        bool wasFlagged = softLocked.Remove(puzzle);

        if (NotificationManager.Instance != null) NotificationManager.Instance.Withdraw(NotificationType.FailPuzzle, puzzle);

        // the Puzzles tab is the only thing that reads IsSoftLocked, and it only rebinds on a
        // change: without this, the row would keep its fail state after the data let go of it
        if (wasFlagged) OnPuzzleSoftLockCleared?.Invoke(puzzle);

        return wasFlagged;
    }


    // MARK: waits


    // True while any puzzle this one waits for is still locked but can still be unlocked, so the
    // ingredients it needs are on their way
    private bool IsWaitingOnUnlock(Puzzle puzzle)
    {
        IReadOnlyList<Puzzle> waitsFor = puzzle.WaitsFor;
        if (waitsFor == null || waitsFor.Count == 0) return false;

        for (int i = 0; i < waitsFor.Count; i++)
        {
            Puzzle wanted = waitsFor[i];
            if (wanted == null || wanted == puzzle) continue;
            if (unlocked.Contains(wanted)) continue;

            if (CanStillUnlock(wanted, puzzle)) return true;
        }

        return false;
    }


    // Walks back from a locked puzzle through whatever unlocks it. A parent keeps it reachable when it
    // is completed (its unlocks land when the ping is opened) or active and not stuck; a locked parent
    // only does if it is reachable itself. A visited node counts as unreachable, which is right because
    // any reachable one ends the walk. Scratch is reused, so this allocates nothing
    private bool CanStillUnlock(Puzzle wanted, Puzzle waiter)
    {
        EnsureUnlockedBy();

        reachVisited.Clear();
        reachStack.Clear();

        // the waiter can't count as a way through: it's exhausted, so it will never complete
        reachVisited.Add(waiter);
        reachVisited.Add(wanted);
        reachStack.Add(wanted);

        bool reachable = false;

        while (reachStack.Count > 0 && !reachable)
        {
            Puzzle step = reachStack[reachStack.Count - 1];
            reachStack.RemoveAt(reachStack.Count - 1);

            if (!unlockedBy.TryGetValue(step, out List<Puzzle> parents)) continue;

            for (int i = 0; i < parents.Count; i++)
            {
                Puzzle parent = parents[i];
                if (!reachVisited.Add(parent)) continue;

                if (completed.Contains(parent) || (unlocked.Contains(parent) && !softLocked.Contains(parent)))
                {
                    reachable = true;
                    break;
                }

                if (!unlocked.Contains(parent)) reachStack.Add(parent);
            }
        }

        reachVisited.Clear();
        reachStack.Clear();

        return reachable;
    }


    private void EnsureUnlockedBy()
    {
        if (unlockedBy != null) return;

        unlockedBy = new Dictionary<Puzzle, List<Puzzle>>();

        if (registry == null) return;

        IReadOnlyList<Puzzle> entries = registry.Entries;
        if (entries == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            Puzzle parent = entries[i];
            if (parent == null) continue;

            IReadOnlyList<Puzzle> unlocks = parent.Unlocks;
            if (unlocks == null) continue;

            for (int j = 0; j < unlocks.Count; j++)
            {
                Puzzle child = unlocks[j];
                if (child == null) continue;

                if (!unlockedBy.TryGetValue(child, out List<Puzzle> list))
                {
                    list = new List<Puzzle>();
                    unlockedBy.Add(child, list);
                }

                if (!list.Contains(parent)) list.Add(parent);
            }
        }
    }


    // True while the player can still act on this puzzle with something they are holding: a live entity
    // they can still reach from one of the puzzle's ingredients has a reaction whose catalyst is in the
    // inventory. The live entity is what makes it true: holding a reagent with nothing left to react
    // with (every seed already fertilised, say) leaves the puzzle stuck, as it must
    private bool IsReagentInHand(Puzzle puzzle)
    {
        IReadOnlyList<Ingredient> ingredients = puzzle.Ingredients;
        if (ingredients == null || ingredients.Count == 0) return false;

        EntityManager entities = EntityManager.Instance;
        InventoryManager inventory = inventoryManager;

        if (entities == null || inventory == null) return false;

        for (int i = 0; i < ingredients.Count; i++)
        {
            if (TryFindHeldCatalyst(ingredients[i].item, entities, inventory)) return true;
        }

        return false;
    }


    // Walks forward from one ingredient, the same growsInto + every-reaction-becomes walk the growth
    // check uses, and answers whether anything it reaches is both standing in the world and reacts to
    // something the player holds
    private bool TryFindHeldCatalyst(GameItem start, EntityManager entities, InventoryManager inventory)
    {
        growthPendingQueue.Clear();
        growthPendingVisited.Clear();

        if (start == null) return false;

        growthPendingQueue.Add(start);
        growthPendingVisited.Add(start);

        for (int i = 0; i < growthPendingQueue.Count && i < MAX_GROWTH_CHAIN; i++)
        {
            GameItem step = growthPendingQueue[i];

            if (HeldCatalystFor(step, entities, inventory)) return true;

            Enqueue(step.GrowsInto);
            EnqueueReactions(step);
        }

        return false;
    }


    // Whether something the player is holding reacts with a live one of this item. The entity check
    // comes first because it is the cheap one, and because it is what stops a held reagent counting
    // when the only things it could react with have already reacted
    private static bool HeldCatalystFor(GameItem item, EntityManager entities, InventoryManager inventory)
    {
        if (item == null) return false;
        if (!entities.HasEntityOf(item)) return false;

        IReadOnlyList<ItemReaction> reactions = item.Reactions;
        if (reactions == null) return false;

        for (int i = 0; i < reactions.Count; i++)
        {
            ItemReaction reaction = reactions[i];
            if (reaction == null) continue;
            if (reaction.Becomes == null) continue;
            if (reaction.Catalyst == null) continue;
            if (inventory.GetCount(reaction.Catalyst) <= 0) continue;

            return true;
        }

        return false;
    }


    // True while a placement's tile transition is still growing the ground a puzzle stands on
    private bool IsPresenting()
    {
        return placementController != null && placementController.IsPresenting;
    }


    // True while any live entity is one of the puzzle's ingredients, or anything one grows into or
    // reacts into, that will still grow: a seed, or the sapling it became on its way to a tree
    private bool IsGrowthPending(Puzzle puzzle)
    {
        IReadOnlyList<Ingredient> ingredients = puzzle.Ingredients;
        if (ingredients == null || ingredients.Count == 0) return false;

        EntityManager entities = EntityManager.Instance;
        if (entities == null) return false;

        entities.CollectLive(scratchLive);

        bool pending = false;

        for (int i = 0; i < scratchLive.Count && !pending; i++)
        {
            GameItem item = scratchLive[i].item;

            // Only something that will grow on its own clock holds the judgement off. A seed waiting on
            // the player to fertilise it doesn't: if the reagent is spent, that is a soft lock like any
            // other, and saying so is the whole point of this check
            if (item == null || item.GrowsInto == null) continue;

            pending = GrowsFromIngredient(item, ingredients);
        }

        scratchLive.Clear();

        return pending;
    }


    // whether item is an ingredient or somewhere down what an ingredient grows into or reacts into
    private bool GrowsFromIngredient(GameItem item, IReadOnlyList<Ingredient> ingredients)
    {
        for (int i = 0; i < ingredients.Count; i++)
        {
            if (ReachesFrom(ingredients[i].item, item)) return true;
        }

        return false;
    }


    // Breadth-first forward from an ingredient: what it grows into after growthDays, and what a reaction
    // turns it into right away. Reactions can branch, so this walks every one of them rather than a
    // single chain. growthPendingQueue and growthPendingVisited are reused rather than allocated; both
    // are scratch, so a check is never re-entered (this runs from a coroutine, not from a walk)
    private bool ReachesFrom(GameItem start, GameItem wanted)
    {
        growthPendingQueue.Clear();
        growthPendingVisited.Clear();

        if (start == null) return false;

        growthPendingQueue.Add(start);
        growthPendingVisited.Add(start);

        // the same guard the chain walk always had: a reaction loop or a growsInto loop is caught by the
        // registry's validation, and this only has to not spin. The queue can't grow past one entry per
        // item, so the index walk always ends anyway
        for (int i = 0; i < growthPendingQueue.Count && i < MAX_GROWTH_CHAIN; i++)
        {
            GameItem step = growthPendingQueue[i];

            if (step == wanted) return true;

            Enqueue(step.GrowsInto);
            EnqueueReactions(step);
        }

        return false;
    }


    private void EnqueueReactions(GameItem step)
    {
        IReadOnlyList<ItemReaction> reactions = step.Reactions;
        if (reactions == null) return;

        for (int i = 0; i < reactions.Count; i++)
        {
            ItemReaction reaction = reactions[i];
            if (reaction == null) continue;

            Enqueue(reaction.Becomes);
        }
    }


    private void Enqueue(GameItem item)
    {
        if (item == null) return;
        if (!growthPendingVisited.Add(item)) return;

        growthPendingQueue.Add(item);
    }


    // MARK: load


    private void LoadFromSave()
    {
        if (registry == null)
        {
            // already reported in Awake, but it is still an end to loading
            MarkLoaded();
            return;
        }

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null)
        {
            Debug.LogError("PuzzleManager: no SaveDataManager in the scene, so puzzle progress cannot be loaded or saved", this);
            MarkLoaded();
            return;
        }

        if (manager.SaveData.world == null)
        {
            Debug.LogError("PuzzleManager: the save has no world, so puzzle progress cannot be loaded or saved", this);
            MarkLoaded();
            return;
        }

        PuzzleSaveData saved = manager.SaveData.world.puzzles;

        if (saved == null)
        {
            saved = new PuzzleSaveData();
            manager.SaveData.world.puzzles = saved;
        }

        completionEnabled = false;

        completedBy.Clear();
        pending.Clear();

        if (saved.IsEmpty) SeedStartingPuzzles();
        else Restore(saved);

        completionEnabled = true;

        // a completion queued for a puzzle that was already unlocked when the game closed (quit
        // between the unlock's save and the flush) is replayed now
        FlushPending();

        // Judge what was restored once the world has settled, so a puzzle that was already stuck when
        // the game was closed is flagged without needing another consumption to trigger it
        ScheduleSoftLockCheck();

        MarkLoaded();
    }


    // Every exit from LoadFromSave goes through here, so a subscriber can never be left waiting for a
    // load that gave up
    private void MarkLoaded()
    {
        isLoaded = true;

        // captured so a handler that subscribes or unsubscribes from inside the callback cannot
        // change the list of handlers this invocation is calling
        Action handlers = puzzlesLoaded;

        handlers?.Invoke();
    }


    private void SeedStartingPuzzles()
    {
        IReadOnlyList<Puzzle> starting = registry.StartingPuzzles;

        if (starting == null || starting.Count == 0)
        {
            Debug.LogError($"PuzzleManager: '{registry.name}' has no starting puzzles, so a new world " +
                $"begins with nothing to do", registry);
            return;
        }

        UnlockBatch(starting);
    }


    private void Restore(PuzzleSaveData saved)
    {
        for (int i = 0; i < saved.unlocked.Count; i++)
        {
            if (!registry.TryGetById(saved.unlocked[i], out Puzzle puzzle))
            {
                Debug.LogError($"PuzzleManager: the save holds unlocked puzzle '{saved.unlocked[i]}', but " +
                    $"no puzzle in '{registry.name}' has that id - dropping it", this);
                continue;
            }

            unlocked.Add(puzzle);
            ledger.Track(puzzle);
        }

        for (int i = 0; i < saved.completed.Count; i++)
        {
            if (!registry.TryGetById(saved.completed[i], out Puzzle puzzle))
            {
                Debug.LogError($"PuzzleManager: the save holds completed puzzle '{saved.completed[i]}', but " +
                    $"no puzzle in '{registry.name}' has that id - dropping it", this);
                continue;
            }

            if (!unlocked.Contains(puzzle))
            {
                Debug.LogError($"PuzzleManager: the save holds '{puzzle.name}' as completed without ever " +
                    $"having unlocked it - dropping it", this);
                continue;
            }

            completed.Add(puzzle);
            ledger.Forget(puzzle);
        }

        for (int i = 0; i < saved.ledgers.Count; i++)
        {
            PuzzleLedgerSave savedLedger = saved.ledgers[i];
            if (savedLedger == null) continue;

            if (!registry.TryGetById(savedLedger.puzzleId, out Puzzle puzzle)) continue;

            if (!unlocked.Contains(puzzle) || completed.Contains(puzzle)) continue;

            ledger.Restore(puzzle, savedLedger.ingredients);
        }

        RestoreCompletedBy(saved);
        RestorePending(saved);
    }


    // Read after the completed list, because a puzzle that is already completed has nothing to wait for
    private void RestorePending(PuzzleSaveData saved)
    {
        if (saved.pending == null) return;

        for (int i = 0; i < saved.pending.Count; i++)
        {
            PuzzleCompletionSave entry = saved.pending[i];
            if (entry == null) continue;

            if (!registry.TryGetById(entry.puzzleId, out Puzzle puzzle))
            {
                Debug.LogError($"PuzzleManager: the save holds a queued completion of '{entry.puzzleId}', but " +
                    $"no puzzle in '{registry.name}' has that id - dropping it", this);
                continue;
            }

            if (completed.Contains(puzzle)) continue;
            if (pending.ContainsKey(puzzle)) continue;

            pending.Add(puzzle, entry.entityGuid ?? "");
        }
    }


    // Read after the completed list, because a row is meaningless unless its puzzle is in it
    private void RestoreCompletedBy(PuzzleSaveData saved)
    {
        if (saved.completedBy == null) return;

        for (int i = 0; i < saved.completedBy.Count; i++)
        {
            PuzzleCompletionSave entry = saved.completedBy[i];
            if (entry == null) continue;

            if (!registry.TryGetById(entry.puzzleId, out Puzzle puzzle))
            {
                Debug.LogError($"PuzzleManager: the save says '{entry.puzzleId}' was completed by entity " +
                    $"'{entry.entityGuid}', but no puzzle in '{registry.name}' has that id - dropping it", this);
                continue;
            }

            if (!completed.Contains(puzzle))
            {
                Debug.LogError($"PuzzleManager: the save says '{puzzle.name}' was completed by entity " +
                    $"'{entry.entityGuid}' but does not list it as completed - dropping it", this);
                continue;
            }

            if (string.IsNullOrEmpty(entry.entityGuid))
            {
                Debug.LogError($"PuzzleManager: the save attributes '{puzzle.name}' to an empty guid - " +
                    $"dropping it", this);
                continue;
            }

            // a cell credit is its key; one that doesn't parse can never be resolved
            if (PuzzleCredit.IsCellKey(entry.entityGuid) && !PuzzleCredit.TryParseCellKey(entry.entityGuid, out int _, out int _, out int _))
            {
                Debug.LogError($"PuzzleManager: the save attributes '{puzzle.name}' to the malformed cell " +
                    $"'{entry.entityGuid}' - dropping it", this);
                continue;
            }

            completedBy[puzzle] = entry.entityGuid;
        }
    }


    // MARK: save


    // Writes the runtime state into the live save
    private void WriteThrough()
    {
        SaveDataManager manager = SaveDataManager.Instance;
        if (manager == null || manager.SaveData == null || manager.SaveData.world == null) return;
        if (registry == null) return;

        PuzzleSaveData saved = manager.SaveData.world.puzzles;

        if (saved == null)
        {
            saved = new PuzzleSaveData();
            manager.SaveData.world.puzzles = saved;
        }

        saved.unlocked.Clear();
        saved.completed.Clear();
        saved.ledgers.Clear();
        saved.completedBy.Clear();

        // a save from before pending existed arrives with it null
        if (saved.pending == null) saved.pending = new List<PuzzleCompletionSave>();
        saved.pending.Clear();

        foreach (Puzzle puzzle in unlocked)
        {
            if (!registry.TryGetIdByEntry(puzzle, out string id)) continue;

            saved.unlocked.Add(id);
        }

        foreach (Puzzle puzzle in completed)
        {
            if (!registry.TryGetIdByEntry(puzzle, out string id)) continue;

            saved.completed.Add(id);
        }

        // rebuilt from the runtime dictionary, so the file cannot disagree with what the manager knows
        List<PuzzleCompletionSave> attributions = new List<PuzzleCompletionSave>();

        foreach (KeyValuePair<Puzzle, string> attribution in completedBy)
        {
            if (attribution.Key == null) continue;
            if (string.IsNullOrEmpty(attribution.Value)) continue;

            if (!registry.TryGetIdByEntry(attribution.Key, out string attributedId))
            {
                Debug.LogError($"PuzzleManager: '{attribution.Key.name}' was completed by entity " +
                    $"'{attribution.Value}', but it has no id in '{registry.name}', so the attribution " +
                    $"cannot be saved", this);
                continue;
            }

            attributions.Add(new PuzzleCompletionSave
            {
                puzzleId = attributedId,
                entityGuid = attribution.Value,
            });
        }

        // the dictionary enumerates in its own order, so sorting is what makes this section's file
        // order deterministic
        attributions.Sort(CompareAttributions);

        for (int i = 0; i < attributions.Count; i++)
        {
            saved.completedBy.Add(attributions[i]);
        }

        foreach (KeyValuePair<Puzzle, string> queued in pending)
        {
            if (queued.Key == null) continue;
            if (!registry.TryGetIdByEntry(queued.Key, out string queuedId)) continue;

            saved.pending.Add(new PuzzleCompletionSave
            {
                puzzleId = queuedId,
                entityGuid = queued.Value,
            });
        }

        saved.pending.Sort(PuzzleSaveData.CompareCompletion);

        foreach (Puzzle puzzle in unlocked)
        {
            // a completed puzzle is never tested again, so it keeps no ledger
            if (completed.Contains(puzzle)) continue;
            if (!registry.TryGetIdByEntry(puzzle, out string puzzleId)) continue;

            PuzzleLedgerSave entry = new PuzzleLedgerSave();
            entry.puzzleId = puzzleId;

            IReadOnlyList<Ingredient> ingredients = puzzle.Ingredients;

            if (ingredients != null)
            {
                // one row per distinct item
                List<GameItem> seen = new List<GameItem>();

                for (int i = 0; i < ingredients.Count; i++)
                {
                    GameItem item = ingredients[i].item;
                    if (item == null) continue;
                    if (seen.Contains(item)) continue;

                    seen.Add(item);

                    if (string.IsNullOrEmpty(item.Id))
                    {
                        Debug.LogError($"PuzzleManager: '{item.name}' is an ingredient of '{puzzle.name}' " +
                            $"but has no Id, so its ledger cannot be saved", item);
                        continue;
                    }

                    entry.ingredients.Add(new IngredientLedgerSave
                    {
                        itemId = item.Id,
                        remaining = ledger.Remaining(puzzle, item),
                    });
                }
            }

            saved.ledgers.Add(entry);
        }
    }


    private void Save()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
    }


    private static int CompareAttributions(PuzzleCompletionSave a, PuzzleCompletionSave b)
    {
        int byPuzzle = string.CompareOrdinal(a != null ? a.puzzleId : "", b != null ? b.puzzleId : "");
        if (byPuzzle != 0) return byPuzzle;

        return string.CompareOrdinal(a != null ? a.entityGuid : "", b != null ? b.entityGuid : "");
    }
}
