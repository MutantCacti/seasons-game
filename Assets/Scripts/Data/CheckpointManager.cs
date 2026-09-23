using System.Collections.Generic;
using UnityEngine;


// Owns the checkpoints section of the save. A checkpoint is a snapshot of the whole world taken the
// moment a puzzle becomes active, so a player who soft-locks that puzzle can return to it.
public class CheckpointManager : MonoBehaviour
{
    private static CheckpointManager instance;
    public static CheckpointManager Instance { get { return instance; } }


    // Puzzles unlocked while a new world is still being built are held here and captured once the
    // world finishes populating, so the snapshot includes the fixtures that only a new world spawns
    private readonly List<Puzzle> pendingCaptures = new List<Puzzle>();


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one CheckpointManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }


    private void Start()
    {
        // subscribed in Start rather than Awake: SaveDataManager is in the same persistent scene, and
        // its Instance is only guaranteed once every Awake in the scene has run
        if (SaveDataManager.Instance != null)
        {
            SaveDataManager.Instance.NewWorldConstructed += OnNewWorldConstructed;
        }
        else
        {
            Debug.LogError("CheckpointManager: no SaveDataManager in the scene, so checkpoints cannot be captured or restored", this);
        }
    }


    private void OnDestroy()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.NewWorldConstructed -= OnNewWorldConstructed;

        if (instance == this) instance = null;
    }


    // MARK: capture


    // One checkpoint per puzzle in the batch. Returns true when the checkpoints were written into the
    // live save in this call - the caller then saves - and false when they were deferred to a new
    // world's construction ending, in which case the flush path saves instead. Never saves itself.
    public bool Capture(IReadOnlyList<Puzzle> puzzles)
    {
        if (puzzles == null || puzzles.Count == 0) return false;

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null || manager.SaveData.world == null)
        {
            Debug.LogError("CheckpointManager: no SaveDataManager, so no checkpoint can be captured", this);
            return false;
        }

        if (manager.IsConstructingNewWorld)
        {
            // the world fixtures have not spawned yet, so hold the puzzles back and capture them once
            // construction finishes, when the world clone will include those fixtures
            for (int i = 0; i < puzzles.Count; i++)
            {
                if (puzzles[i] != null) pendingCaptures.Add(puzzles[i]);
            }

            return false;
        }

        return CaptureNow(manager, puzzles);
    }


    // Clones the live world for each puzzle and stores or overwrites its checkpoint. The world is
    // cloned at this moment on purpose: a checkpoint holds what the world looked like when the puzzle
    // became active, not what it looks like now.
    private bool CaptureNow(SaveDataManager manager, IReadOnlyList<Puzzle> puzzles)
    {
        SaveData save = manager.SaveData;

        if (save.checkpoints == null) save.checkpoints = new List<Checkpoint>();

        bool anyCaptured = false;

        for (int i = 0; i < puzzles.Count; i++)
        {
            Puzzle puzzle = puzzles[i];

            if (puzzle == null) continue;

            if (string.IsNullOrEmpty(puzzle.Id))
            {
                Debug.LogError($"CheckpointManager: '{puzzle.name}' has no Id, so it cannot have a checkpoint", puzzle);
                continue;
            }

            Checkpoint checkpoint = new Checkpoint
            {
                puzzleId = puzzle.Id,
                world = save.world.Clone(),
            };

            int existing = IndexOfCheckpoint(save.checkpoints, puzzle.Id);

            // overwrite in place so the checkpoint keeps its position in the list
            if (existing >= 0) save.checkpoints[existing] = checkpoint;
            else save.checkpoints.Add(checkpoint);

            anyCaptured = true;
        }

        return anyCaptured;
    }


    private void FlushPending()
    {
        if (pendingCaptures.Count == 0) return;

        List<Puzzle> captures = new List<Puzzle>(pendingCaptures);
        pendingCaptures.Clear();

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null || manager.SaveData.world == null)
        {
            Debug.LogError("CheckpointManager: the world finished constructing but there is no SaveDataManager, so the held-back checkpoints were dropped", this);
            return;
        }

        CaptureNow(manager, captures);

        // the inline capture path leaves saving to its caller, but the flush is the only write here
        manager.Save();
    }


    private void OnNewWorldConstructed()
    {
        FlushPending();
    }


    // MARK: queries


    public bool HasCheckpoint(Puzzle puzzle)
    {
        if (puzzle == null) return false;

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null) return false;

        return IndexOfCheckpoint(manager.SaveData.checkpoints, puzzle.Id) >= 0;
    }


    // MARK: reset


    public bool ResetTo(Puzzle puzzle)
    {
        return puzzle != null && ResetById(puzzle.Id);
    }


    // The one place the live world is swapped for a checkpoint. Reset resolves by stored puzzle id, so
    // the debug triggers can reset by index without needing a PuzzleRegistry reference.
    private bool ResetById(string puzzleId)
    {
        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null || manager.SaveData.world == null)
        {
            Debug.LogError("CheckpointManager: no SaveDataManager, so there is nothing to reset", this);
            return false;
        }

        int index = IndexOfCheckpoint(manager.SaveData.checkpoints, puzzleId);

        if (index < 0) return false;

        Checkpoint checkpoint = manager.SaveData.checkpoints[index];

        if (checkpoint == null || checkpoint.world == null)
        {
            Debug.LogError($"CheckpointManager: the checkpoint for '{puzzleId}' is empty, so there is nothing to reset to", this);
            return false;
        }

        if (LoadingScreenManager.Instance == null)
        {
            Debug.LogError("CheckpointManager: no LoadingScreenManager, so the Game scene cannot be reloaded after a reset", this);
            return false;
        }

        if (LoadingScreenManager.Instance.IsLoading)
        {
            Debug.LogWarning("CheckpointManager: a load is already in progress, so the reset was ignored");
            return false;
        }

        // a clone, so the checkpoint stays pristine and the player can reset to it again
        manager.SaveData.world = checkpoint.world.Clone();
        manager.Save();

        LoadingScreenManager.Instance.LoadScene("Game");

        return true;
    }


    // MARK: lookup


    private int IndexOfCheckpoint(List<Checkpoint> checkpoints, string puzzleId)
    {
        if (checkpoints == null || string.IsNullOrEmpty(puzzleId)) return -1;

        for (int i = 0; i < checkpoints.Count; i++)
        {
            if (checkpoints[i] != null && checkpoints[i].puzzleId == puzzleId) return i;
        }

        return -1;
    }


    private Checkpoint CheckpointAt(int index)
    {
        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null || manager.SaveData.checkpoints == null) return null;
        if (index < 0 || index >= manager.SaveData.checkpoints.Count) return null;

        return manager.SaveData.checkpoints[index];
    }


    private int LastCheckpointIndex()
    {
        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null || manager.SaveData.checkpoints == null) return -1;

        return manager.SaveData.checkpoints.Count - 1;
    }


    // MARK: debug


    [ContextMenu("Reset To Latest Checkpoint")]
    private void ResetToLatestCheckpoint()
    {
        ResetToCheckpointAt(LastCheckpointIndex());
    }


    [ContextMenu("Reset To First Checkpoint")]
    private void ResetToFirstCheckpoint()
    {
        ResetToCheckpointAt(0);
    }


    [ContextMenu("Log Checkpoints")]
    private void LogCheckpoints()
    {
        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null || manager.SaveData.checkpoints == null)
        {
            Debug.Log("CheckpointManager: no SaveDataManager, so there are no checkpoints to log");
            return;
        }

        List<Checkpoint> checkpoints = manager.SaveData.checkpoints;

        if (checkpoints.Count == 0)
        {
            Debug.Log("CheckpointManager: 0 checkpoints");
            return;
        }

        for (int i = 0; i < checkpoints.Count; i++)
        {
            Checkpoint checkpoint = checkpoints[i];

            if (checkpoint == null)
            {
                Debug.Log($"CheckpointManager: [{i}] empty");
                continue;
            }

            Debug.Log($"CheckpointManager: [{i}] {checkpoint.puzzleId} - {EntityCount(checkpoint)} entities, {InventorySummary(checkpoint)}");
        }
    }


    private void ResetToCheckpointAt(int index)
    {
        Checkpoint checkpoint = CheckpointAt(index);

        if (checkpoint == null || string.IsNullOrEmpty(checkpoint.puzzleId))
        {
            Debug.LogWarning($"CheckpointManager: no checkpoint at index {index}, so there is nothing to reset to");
            return;
        }

        ResetById(checkpoint.puzzleId);
    }


    // MARK: log helpers


    private int EntityCount(Checkpoint checkpoint)
    {
        if (checkpoint.world == null || checkpoint.world.entities == null || checkpoint.world.entities.entities == null) return 0;

        return checkpoint.world.entities.entities.Count;
    }


    private string InventorySummary(Checkpoint checkpoint)
    {
        if (checkpoint.world == null || checkpoint.world.inventory == null) return "no inventory";

        SerializableDictionary<string, int> inventory = checkpoint.world.inventory;

        if (inventory.Count == 0) return "empty inventory";

        int total = 0;

        foreach (KeyValuePair<string, int> pair in inventory)
        {
            total += pair.Value;
        }

        return $"{inventory.Count} item types, {total} items";
    }
}
