using UnityEngine;


// What a spawn rule is allowed to touch. AnimalManager implements it, so a rule never has to reach
// for singletons and cannot spawn outside the manager's bookkeeping.
public interface AnimalSpawnContext
{
    TerrainManager Terrain { get; }
    bool HasFired(AnimalSpawnRule rule);
    bool Spawn(AnimalSpawnRule rule, Vector3 position);
}


// One way an animal comes into the world. Each mechanic is a subclass with its own asset, and
// AnimalManager forwards world events to every rule through the virtual hooks below. A rule fires at
// most once per world.
public abstract class AnimalSpawnRule : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] private Animal animal;

    // optional: the puzzle this spawn completes. a locked puzzle queues the completion until it unlocks
    [SerializeField] private Puzzle completes;


    // falls back to the asset name so a rule with no id still fires once rather than every time
    public string Id { get { return string.IsNullOrEmpty(id) ? name : id; } }
    public Animal Animal { get { return animal; } }
    public Puzzle Completes { get { return completes; } }


    // MARK: hooks


    // after a placement has been committed, consumed and saved
    public virtual void OnItemPlaced(AnimalSpawnContext context, GameItem item, Vector3Int cell)
    {
    }
}
