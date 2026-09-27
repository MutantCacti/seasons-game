using System;
using System.Collections.Generic;
using UnityEngine;


[Serializable]
public struct Ingredient
{
    public GameItem item;
    [Min(1)] public int count;
}


// A plain puzzle: completed by something else (a spawn rule, a script) through PuzzleManager.Complete.
// A puzzle with its own completion code is a ScriptedPuzzle subclass instead. It is a WorldListener so
// that a scripted one can hear the world; a plain one's hooks are all empty.
[CreateAssetMenu(
    fileName = "Puzzle",
    menuName = "Puzzles/Puzzle"
)]
public class Puzzle : WorldListener
{
    [SerializeField] private string id;
    [SerializeField] [TextArea] private string description;
    [SerializeField] private Sprite icon;

    // Unlocking this puzzle puts these in the inventory
    [SerializeField] private List<Ingredient> ingredients = new List<Ingredient>();

    // Completing this puzzle unlocks these
    [SerializeField] private List<Puzzle> unlocks = new List<Puzzle>();

    [Tooltip("the checkpoint a fail resets to; set it to an earlier puzzle when this one's failure can be " +
        "caused by that one. Empty resets to this puzzle's own")]
    [SerializeField] private Puzzle resetsTo;

    [Tooltip("puzzles whose unlock brings ingredients this one needs; it isn't judged stuck while any of " +
        "them can still be unlocked")]
    [SerializeField] private List<Puzzle> waitsFor = new List<Puzzle>();

    [Tooltip("world conditions this puzzle starts the moment it's completed")]
    [SerializeField] private List<WorldCondition> startsConditions = new List<WorldCondition>();


    public string Id => id;
    public string Name => name;   // Object.name, the name in the inspector
    public string Description => description;
    public Sprite Icon => icon;
    public IReadOnlyList<Ingredient> Ingredients => ingredients;
    public IReadOnlyList<Puzzle> Unlocks => unlocks;
    public IReadOnlyList<Puzzle> WaitsFor => waitsFor;
    public IReadOnlyList<WorldCondition> StartsConditions => startsConditions;

    // whose checkpoint a fail of this one goes back to
    public Puzzle ResetTarget => resetsTo != null ? resetsTo : this;


    // How many of item this puzzle needs
    // Zero means it does not need it at all
    public int RequiredCount(GameItem item)
    {
        if (item == null || ingredients == null) return 0;

        int total = 0;

        for (int i = 0; i < ingredients.Count; i++)
        {
            if (ingredients[i].item == item) total += ingredients[i].count;
        }

        return total;
    }
}
