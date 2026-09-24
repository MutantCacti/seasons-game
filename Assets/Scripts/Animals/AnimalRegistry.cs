using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(
    fileName = "AnimalRegistry",
    menuName = "Animals/Animal Registry"
)]
public class AnimalRegistry : ScriptableObject, Registry<Animal>
{
    [SerializeField] private List<Animal> animals = new List<Animal>();


    [System.NonSerialized] private Dictionary<string, Animal> animalById;


    public IReadOnlyList<Animal> Entries { get { return animals; } }
    public int Count { get { return animals != null ? animals.Count : 0; } }


    // MARK: lookup


    public bool TryGetById(string id, out Animal animal)
    {
        animal = null;
        if (string.IsNullOrEmpty(id)) return false;

        EnsureLookup();

        return animalById.TryGetValue(id, out animal) && animal != null;
    }


    public bool TryGetIdByEntry(Animal animal, out string id)
    {
        id = null;
        if (animal == null) return false;

        EnsureLookup();

        if (!string.IsNullOrEmpty(animal.Id) &&
            animalById.TryGetValue(animal.Id, out Animal registered) &&
            registered == animal)
        {
            id = animal.Id;
            return true;
        }

        Debug.LogError($"AnimalRegistry: '{animal.name}' is not registered or its id '{animal.Id}' is empty or duplicated, so it cannot be written to a save", this);

        return false;
    }


    // MARK: construction


    // the lookup goes stale whenever the editor changes the animals list
    private void OnValidate()
    {
        animalById = null;
    }


    public void Rebuild()
    {
        animalById = new Dictionary<string, Animal>();
        if (animals == null) return;

        for (int i = 0; i < animals.Count; i++)
        {
            Animal animal = animals[i];

            if (animal == null)
            {
                Debug.LogError($"AnimalRegistry: entry {i} is empty", this);
                continue;
            }

            if (string.IsNullOrEmpty(animal.Id))
            {
                Debug.LogError($"AnimalRegistry: '{animal.name}' has no Id, so it can never be saved or loaded", animal);
                continue;
            }

            // first entry wins on duplicate
            if (animalById.TryGetValue(animal.Id, out Animal existing))
            {
                Debug.LogError($"AnimalRegistry: duplicate Id '{animal.Id}' on '{animal.name}', keeping '{existing.name}' and ignoring this one", animal);
                continue;
            }

            animalById.Add(animal.Id, animal);
        }
    }


    private void EnsureLookup()
    {
        if (animalById != null) return;

        Rebuild();
    }
}
