using System;
using System.Collections.Generic;
using UnityEngine;



// world scripts call Complete(puzzle) when they decide the condition has been met
// This manager is the only thing allowed to say no

// Runs after the default-order scripts (TerrainGrid, EntityManager, InventoryManager, CameraController)
// so that the world and the inventory are fully built before the starting puzzles are seeded
[DefaultExecutionOrder(100)]
public class PuzzleManager : MonoBehaviour
{
    private static PuzzleManager instance;
    public static PuzzleManager Instance { get { return instance; } }


    [SerializeField] private PuzzleRegistry registry;

    [SerializeField] private InventoryManager inventoryManager;


    public event Action<Puzzle> OnPuzzleComplete;

    public event Action<Puzzle> OnPuzzleUnlocked;

    public event Action<Puzzle> OnPuzzleSoftLocked;


    private readonly HashSet<Puzzle> unlocked = new HashSet<Puzzle>();
    private readonly HashSet<Puzzle> completed = new HashSet<Puzzle>();

    // puzzles already announced as soft locked in case you missed the event
    private readonly HashSet<Puzzle> announcedSoftLocks = new HashSet<Puzzle>();

    private readonly PuzzleLedger ledger = new PuzzleLedger();

    private readonly List<Puzzle> charged = new List<Puzzle>();

    private bool completionEnabled = true;


    public bool CompletionEnabled { get { return completionEnabled; } }
    public PuzzleRegistry Registry { get { return registry; } }
    public int ActiveCount { get { return unlocked.Count - completed.Count; } }


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


    // True when every ingredient the puzzle required has been consumed
    // and nobody has submitted its completion
    public bool IsSoftLocked(Puzzle puzzle)
    {
        if (!IsActive(puzzle)) return false;

        return ledger.IsExhausted(puzzle);
    }


    // For debug and UI, returns how many ingredients a puzzle's soft lock
    // is still waiting for
    public int RemainingIngredient(Puzzle puzzle, GameItem item)
    {
        return ledger.Remaining(puzzle, item);
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
    public bool Complete(Puzzle puzzle)
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
        announcedSoftLocks.Remove(puzzle);

        // all this must happen before the event is invoked
        if (puzzle.Unlocks != null)
        {
            for (int i = 0; i < puzzle.Unlocks.Count; i++)
            {
                Unlock(puzzle.Unlocks[i]);
            }
        }

        WriteThrough();
        Save();

        OnPuzzleComplete?.Invoke(puzzle);

        return true;
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

        for (int i = 0; i < charged.Count; i++)
        {
            Puzzle puzzle = charged[i];

            // a puzzle that completes later still completes
            if (!IsSoftLocked(puzzle)) continue;
            if (!announcedSoftLocks.Add(puzzle)) continue;

            Debug.LogWarning($"PuzzleManager: '{puzzle.name}' has spent every ingredient it was given " +
                $"and is still incomplete, so it is flagged as soft locked", puzzle);

            OnPuzzleSoftLocked?.Invoke(puzzle);
        }

        if (charged.Count == 0) return;

        WriteThrough();
    }


    // MARK: load


    private void LoadFromSave()
    {
        if (registry == null) return;

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null)
        {
            Debug.LogError("PuzzleManager: no SaveDataManager in the scene, so puzzle progress cannot be loaded or saved", this);
            return;
        }

        PuzzleSaveData saved = manager.SaveData.puzzles;

        if (saved == null)
        {
            saved = new PuzzleSaveData();
            manager.SaveData.puzzles = saved;
        }

        completionEnabled = false;

        if (saved.IsEmpty) SeedStartingPuzzles();
        else Restore(saved);

        completionEnabled = true;
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

        int seeded = 0;

        for (int i = 0; i < starting.Count; i++)
        {
            if (Unlock(starting[i])) seeded++;
        }

        if (seeded == 0) return;

        WriteThrough();
        Save();
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
    }


    // MARK: save


    // Writes the runtime state into the live save
    private void WriteThrough()
    {
        SaveDataManager manager = SaveDataManager.Instance;
        if (manager == null || manager.SaveData == null) return;
        if (registry == null) return;

        PuzzleSaveData saved = manager.SaveData.puzzles;

        if (saved == null)
        {
            saved = new PuzzleSaveData();
            manager.SaveData.puzzles = saved;
        }

        saved.unlocked.Clear();
        saved.completed.Clear();
        saved.ledgers.Clear();

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
}
