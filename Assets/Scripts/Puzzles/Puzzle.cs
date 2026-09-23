using System;
using System.Collections.Generic;
using UnityEngine;


[Serializable]
public struct Ingredient
{
    public GameItem item;
    [Min(1)] public int count;
}


[CreateAssetMenu(
    fileName = "Puzzle",
    menuName = "Puzzles/Puzzle"
)]
public class Puzzle : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] [TextArea] private string description;

    // Unlocking this puzzle puts these in the inventory
    [SerializeField] private List<Ingredient> ingredients = new List<Ingredient>();

    // Completing this puzzle unlocks these
    [SerializeField] private List<Puzzle> unlocks = new List<Puzzle>();


    public string Id => id;
    public string Name => name;   // Object.name, the name in the inspector
    public string Description => description;
    public IReadOnlyList<Ingredient> Ingredients => ingredients;
    public IReadOnlyList<Puzzle> Unlocks => unlocks;


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
