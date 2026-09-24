using System;
using System.Collections.Generic;


// How much of one ingredient an active puzzle is still waiting on
[Serializable]
public class IngredientLedgerSave
{
    public string itemId;
    public int remaining;
}


[Serializable]
public class PuzzleLedgerSave
{
    public string puzzleId;
    public List<IngredientLedgerSave> ingredients = new List<IngredientLedgerSave>();
}


// Which entity completed a puzzle, so a later system can find the thing that did it
[Serializable]
public class PuzzleCompletionSave
{
    public string puzzleId;
    public string entityGuid;   // an EntityId guid, resolved through EntityManager
}


// what has been unlocked, what has been completed, and the softlock ledgers
[Serializable]
public class PuzzleSaveData
{
    // the full list of possible puzzles is authored
    // in the PuzzleRegistry asset

    // every puzzle that has ever been unlocked
    public List<string> unlocked = new List<string>();

    // the subset of unlocked that has since been completed
    public List<string> completed = new List<string>();

    // one row per active puzzle; a completed puzzle needs none
    public List<PuzzleLedgerSave> ledgers = new List<PuzzleLedgerSave>();

    // one row per completed puzzle that named the entity that completed it
    public List<PuzzleCompletionSave> completedBy = new List<PuzzleCompletionSave>();

    // completions that arrived before their puzzle was unlocked. they flush through the normal
    // completion path once it is, so a spawn that beats its puzzle still counts
    public List<PuzzleCompletionSave> pending = new List<PuzzleCompletionSave>();


    public bool IsEmpty { get { return unlocked == null || unlocked.Count == 0; } }


    // Deterministic output (identical worlds write identical files)
    public void Sort()
    {
        if (unlocked != null) unlocked.Sort(string.CompareOrdinal);
        if (completed != null) completed.Sort(string.CompareOrdinal);

        SortLedgers();

        if (completedBy != null) completedBy.Sort(CompareCompletion);
        if (pending != null) pending.Sort(CompareCompletion);
    }


    private void SortLedgers()
    {
        if (ledgers == null) return;

        ledgers.Sort((a, b) => string.CompareOrdinal(SafeId(a), SafeId(b)));

        for (int i = 0; i < ledgers.Count; i++)
        {
            List<IngredientLedgerSave> entries = ledgers[i] != null ? ledgers[i].ingredients : null;
            if (entries == null) continue;

            entries.Sort((a, b) => string.CompareOrdinal(SafeItemId(a), SafeItemId(b)));
        }
    }


    // by puzzle, then by guid, so the order never depends on the dictionary it was rebuilt from
    public static int CompareCompletion(PuzzleCompletionSave a, PuzzleCompletionSave b)
    {
        int byPuzzle = string.CompareOrdinal(SafePuzzleId(a), SafePuzzleId(b));
        if (byPuzzle != 0) return byPuzzle;

        return string.CompareOrdinal(SafeEntityGuid(a), SafeEntityGuid(b));
    }


    private static string SafeId(PuzzleLedgerSave entry)
    {
        return entry != null && entry.puzzleId != null ? entry.puzzleId : "";
    }


    private static string SafeItemId(IngredientLedgerSave entry)
    {
        return entry != null && entry.itemId != null ? entry.itemId : "";
    }


    private static string SafePuzzleId(PuzzleCompletionSave entry)
    {
        return entry != null && entry.puzzleId != null ? entry.puzzleId : "";
    }


    private static string SafeEntityGuid(PuzzleCompletionSave entry)
    {
        return entry != null && entry.entityGuid != null ? entry.entityGuid : "";
    }
}
