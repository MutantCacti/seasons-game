using System.Collections.Generic;
using UnityEngine;


// One animal standing in the world
[System.Serializable]
public class AnimalSave
{
    public string animalId;     // resolves through AnimalRegistry to the prefab that gets instantiated
    public string guid;         // this instance's EntityId, so a puzzle can name the animal that completed it
    public Vector3 position;    // world space rather than a cell, because animals may wander later
}


// Every animal in the world, and which spawn rules have already had their turn. Both live in the
// world, so a checkpoint reset takes the animals away and lets their rules fire again.
[System.Serializable]
public class AnimalSaveData
{
    public List<AnimalSave> animals = new List<AnimalSave>();

    // AnimalSpawnRule ids; a rule fires at most once per world
    public List<string> firedRules = new List<string>();


    // Deterministic output (identical worlds write identical files)
    public void Sort()
    {
        if (animals != null)
        {
            animals.Sort((a, b) =>
            {
                int byAnimal = string.CompareOrdinal(a != null ? a.animalId : "", b != null ? b.animalId : "");
                if (byAnimal != 0) return byAnimal;

                return string.CompareOrdinal(a != null ? a.guid : "", b != null ? b.guid : "");
            });
        }

        if (firedRules != null) firedRules.Sort(string.CompareOrdinal);
    }
}
