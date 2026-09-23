using System.Collections.Generic;
using UnityEngine;


// for every active puzzle, how many of each ingredient it is still
// waiting for the player to spend.
//
// TODO: Revisit Charge()
// My instinct is to decrement the ledger of the puzzle requiring the
// most of that item. This does require one bit of bookkeeping ---when
// a new puzzle is initialised that requires more of that ingredients,
// the consumption needs to be transferred onto its ledger, in order
// to avoid having to remember which ledger is currently being decremented
// (and avoid cases where new puzzles cause it to take significantly
// longer to flag a failure).
// For now, every puzzle just gets decremented


public class PuzzleLedger
{
    // yes this is a double dictionary
    // for each puzzle (key), there is a dictionary (value) that shows 
    // its ingredients and how many haven't been placed uet
    private readonly Dictionary<Puzzle, Dictionary<GameItem, int>> remaining =
        new Dictionary<Puzzle, Dictionary<GameItem, int>>();


    // MARK: lifecycle


    public void Track(Puzzle puzzle)
    {
        if (puzzle == null) return;

        remaining[puzzle] = Build(puzzle);
    }


    public void Forget(Puzzle puzzle)
    {
        if (puzzle == null) return;

        remaining.Remove(puzzle);
    }


    // Restores a ledger read back from a save
    public void Restore(Puzzle puzzle, IReadOnlyList<IngredientLedgerSave> saved)
    {
        if (puzzle == null) return;

        Dictionary<GameItem, int> perItem = Build(puzzle);

        if (saved != null)
        {
            for (int i = 0; i < saved.Count; i++)
            {
                IngredientLedgerSave entry = saved[i];
                if (entry == null) continue;

                GameItem item = FindIngredient(puzzle, entry.itemId);

                if (item == null)
                {
                    Debug.LogWarning($"PuzzleLedger: '{puzzle.name}' no longer lists '{entry.itemId}', " +
                        $"so that ledger entry was dropped", puzzle);
                    continue;
                }

                perItem[item] = entry.remaining > 0 ? entry.remaining : 0;
            }
        }

        remaining[puzzle] = perItem;
    }


    // MARK: queries


    public bool IsTracked(Puzzle puzzle)
    {
        return puzzle != null && remaining.ContainsKey(puzzle);
    }


    public int Remaining(Puzzle puzzle, GameItem item)
    {
        if (puzzle == null || item == null) return 0;
        if (!remaining.TryGetValue(puzzle, out Dictionary<GameItem, int> perItem)) return 0;
        if (perItem == null) return 0;

        return perItem.TryGetValue(item, out int count) ? count : 0;
    }


    // All ingredients have all of their ledgers at 0
    public bool IsExhausted(Puzzle puzzle)
    {
        if (puzzle == null) return false;
        if (!remaining.TryGetValue(puzzle, out Dictionary<GameItem, int> perItem)) return false;
        if (perItem == null || perItem.Count == 0) return false;

        foreach (KeyValuePair<GameItem, int> pair in perItem)
        {
            if (pair.Value > 0) return false;
        }

        return true;
    }


    // MARK: charging


    public void Charge(GameItem item, int quantity, List<Puzzle> charged)
    {
        if (charged == null) return;

        charged.Clear();

        if (item == null || quantity <= 0) return;

        foreach (KeyValuePair<Puzzle, Dictionary<GameItem, int>> entry in remaining)
        {
            Dictionary<GameItem, int> perItem = entry.Value;
            if (perItem == null) continue;
            if (!perItem.TryGetValue(item, out int current)) continue;
            if (current <= 0) continue;

            // clamping at zero means a ledger never goes negative, so "exhausted" cannot be undone by
            // spending more of an item than the puzzle asked for
            perItem[item] = current > quantity ? current - quantity : 0;

            charged.Add(entry.Key);
        }
    }


    // MARK: internals


    private static Dictionary<GameItem, int> Build(Puzzle puzzle)
    {
        Dictionary<GameItem, int> perItem = new Dictionary<GameItem, int>();

        IReadOnlyList<Ingredient> ingredients = puzzle.Ingredients;
        if (ingredients == null) return perItem;

        for (int i = 0; i < ingredients.Count; i++)
        {
            GameItem item = ingredients[i].item;
            if (item == null) continue;

            perItem.TryGetValue(item, out int existing);
            perItem[item] = existing + ingredients[i].count;
        }

        return perItem;
    }


    private static GameItem FindIngredient(Puzzle puzzle, string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;

        IReadOnlyList<Ingredient> ingredients = puzzle.Ingredients;
        if (ingredients == null) return null;

        for (int i = 0; i < ingredients.Count; i++)
        {
            GameItem item = ingredients[i].item;
            if (item == null) continue;
            if (item.Id == itemId) return item;
        }

        return null;
    }
}
