using System;
using System.Collections.Generic;
using UnityEngine;



// world scripts call Complete(puzzle, source) when they decide the condition has been met
// This manager is the only thing allowed to say no

public class PuzzleManager : MonoBehaviour
{
    private static PuzzleManager instance;
    public static PuzzleManager Instance { get { return instance; } }


    [SerializeField] private PuzzleRegistry registry;

    [SerializeField] private InventoryManager inventoryManager;

    // How long the manager waits after the last consumption before deciding a puzzle is stuck
    [SerializeField] private float softLockGraceSeconds = 0.5f;


    public event Action<Puzzle> OnPuzzleComplete;

    public event Action<Puzzle> OnPuzzleUnlocked;

    public event Action<Puzzle> OnPuzzleSoftLocked;


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

    // puzzles the manager has settled on being stuck, and therefore announced
    private readonly HashSet<Puzzle> softLocked = new HashSet<Puzzle>();

    // active puzzles waiting out the grace period before being judged
    private readonly HashSet<Puzzle> pendingSoftLockChecks = new HashSet<Puzzle>();

    private readonly PuzzleLedger ledger = new PuzzleLedger();

    private readonly List<Puzzle> charged = new List<Puzzle>();

    private bool completionEnabled = true;

    private bool checkpointManagerWarningLogged;

    private float softLockCheckAt;
    private bool softLockCheckScheduled;


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
        }

        LoadFromSave();
    }


    private void OnDestroy()
    {
        if (inventoryManager != null) inventoryManager.ItemConsumed -= OnItemConsumed;

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


    // Which entity completed this puzzle. False when the completion was never attributed to one, or
    // when the entity that did it is no longer in the world.
    public bool TryGetCompletedBy(Puzzle puzzle, out EntityId entity)
    {
        entity = null;

        if (puzzle == null) return false;

        if (!completedBy.TryGetValue(puzzle, out string guid)) return false;

        if (EntityManager.Instance == null) return false;

        return EntityManager.Instance.TryGetEntity(guid, out entity);
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


    // single entry point, every world script should use this to submit puzzle completion
    // returns false when the manager refused, meaning puzzles are disabled, this puzzle
    // is null or not unlocked or has already been completed
    //
    // source names the entity that met the condition. It may be null, in which case nothing is
    // recorded and the completion counts for nothing but the save's completed list.
    public bool Complete(Puzzle puzzle, EntityId source)
    {
        if (!completionEnabled) return false;

        if (puzzle == null) return false;

        if (!unlocked.Contains(puzzle))
        {
            Debug.LogWarning($"PuzzleManager: '{puzzle.name}' is not active, so its completion was ignored", puzzle);
            return false;
        }

        if (completed.Contains(puzzle)) return false;

        // all this must happen before the event is invoked
        completed.Add(puzzle);
        ledger.Forget(puzzle);

        // no longer stuck, and no longer worth judging
        softLocked.Remove(puzzle);
        pendingSoftLockChecks.Remove(puzzle);

        // a completion with no source is legal, it simply records nothing about who did it
        if (source != null)
        {
            if (source.HasGuid) completedBy[puzzle] = source.Guid;
            else Debug.LogError($"PuzzleManager: '{puzzle.name}' was completed by an entity with no guid, " +
                $"so the save cannot record which entity did it", source);
        }

        GrantUnlocks(puzzle);

        WriteThrough();
        Save();

        OnPuzzleComplete?.Invoke(puzzle);

        return true;
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

        UnlockBatch(candidates);
    }


    // The one place puzzles become unlocked. Write-through and save happen once for the whole batch,
    // and only when something in it was actually new.
    private void UnlockBatch(IReadOnlyList<Puzzle> puzzles)
    {
        if (puzzles == null) return;

        List<Puzzle> newlyUnlocked = new List<Puzzle>();

        for (int i = 0; i < puzzles.Count; i++)
        {
            // Unlock only returns true for a puzzle that was not unlocked before, so this list is
            // exactly the puzzles this batch added
            if (Unlock(puzzles[i])) newlyUnlocked.Add(puzzles[i]);
        }

        if (newlyUnlocked.Count == 0) return;

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

            // A full inventory refuses here and the ingredient is lost
            if (!inventoryManager.Add(ingredient.item, ingredient.count))
            {
                Debug.LogError($"PuzzleManager: could not grant {ingredient.count}x '{ingredient.item.name}' for '{puzzle.name}' (inventoryManager.Add returned false)", this);
            }
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


    // Queues every active puzzle to be judged once the world has settled. Re-queueing one that is
    // already flagged is harmless, because the flag is only raised on the transition.
    private void ScheduleSoftLockCheck()
    {
        pendingSoftLockChecks.Clear();

        foreach (Puzzle puzzle in unlocked)
        {
            if (completed.Contains(puzzle)) continue;

            pendingSoftLockChecks.Add(puzzle);
        }

        if (pendingSoftLockChecks.Count == 0) return;

        softLockCheckAt = Time.time + softLockGraceSeconds;
        softLockCheckScheduled = true;
    }


    private void EvaluateSoftLocks()
    {
        foreach (Puzzle puzzle in pendingSoftLockChecks)
        {
            // a puzzle completed during the grace period was never stuck
            if (!IsActive(puzzle)) continue;
            if (!ledger.IsExhausted(puzzle)) continue;
            if (!softLocked.Add(puzzle)) continue;

            Debug.LogWarning($"PuzzleManager: '{puzzle.name}' has spent every ingredient it was given " +
                $"and is still incomplete, so it is flagged as soft locked", puzzle);

            OnPuzzleSoftLocked?.Invoke(puzzle);
        }

        pendingSoftLockChecks.Clear();
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

        if (saved.IsEmpty) SeedStartingPuzzles();
        else Restore(saved);

        completionEnabled = true;

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
            if (!registry.TryGetPuzzleById(saved.unlocked[i], out Puzzle puzzle))
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
            if (!registry.TryGetPuzzleById(saved.completed[i], out Puzzle puzzle))
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

            if (!registry.TryGetPuzzleById(savedLedger.puzzleId, out Puzzle puzzle)) continue;

            if (!unlocked.Contains(puzzle) || completed.Contains(puzzle)) continue;

            ledger.Restore(puzzle, savedLedger.ingredients);
        }

        RestoreCompletedBy(saved);
    }


    // Read after the completed list, because a row is meaningless unless its puzzle is in it
    private void RestoreCompletedBy(PuzzleSaveData saved)
    {
        if (saved.completedBy == null) return;

        for (int i = 0; i < saved.completedBy.Count; i++)
        {
            PuzzleCompletionSave entry = saved.completedBy[i];
            if (entry == null) continue;

            if (!registry.TryGetPuzzleById(entry.puzzleId, out Puzzle puzzle))
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

        foreach (Puzzle puzzle in unlocked)
        {
            if (!registry.TryGetIdByPuzzle(puzzle, out string id)) continue;

            saved.unlocked.Add(id);
        }

        foreach (Puzzle puzzle in completed)
        {
            if (!registry.TryGetIdByPuzzle(puzzle, out string id)) continue;

            saved.completed.Add(id);
        }

        // rebuilt from the runtime dictionary, so the file cannot disagree with what the manager knows
        List<PuzzleCompletionSave> attributions = new List<PuzzleCompletionSave>();

        foreach (KeyValuePair<Puzzle, string> attribution in completedBy)
        {
            if (attribution.Key == null) continue;
            if (string.IsNullOrEmpty(attribution.Value)) continue;

            if (!registry.TryGetIdByPuzzle(attribution.Key, out string attributedId))
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

        foreach (Puzzle puzzle in unlocked)
        {
            // a completed puzzle is never tested again, so it keeps no ledger
            if (completed.Contains(puzzle)) continue;
            if (!registry.TryGetIdByPuzzle(puzzle, out string puzzleId)) continue;

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
