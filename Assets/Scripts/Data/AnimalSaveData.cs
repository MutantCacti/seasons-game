using System.Collections.Generic;
using UnityEngine;


// One animal standing in the world
[System.Serializable]
public class AnimalSave
{
    public string animalId;     // resolves through AnimalRegistry to the prefab that gets instantiated
    public string guid;         // this instance's EntityId, so a puzzle can name the animal that completed it
    public Vector3 position;    // world space rather than a cell, because animals wander

    // all zero in a save from before rotation was written, which reads as "use the prefab's"
    public Quaternion rotation;

    // what the player named this one; empty is the species name
    public string customName;

    // starred in its status panel, which makes it glow
    public bool highlighted;
}


// Every animal in the world. It lives in the world, so a checkpoint reset takes the animals away, and
// their rules can spawn them again because each animal's mob cap has room once more.
[System.Serializable]
public class AnimalSaveData
{
    public List<AnimalSave> animals = new List<AnimalSave>();


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
    }
}
