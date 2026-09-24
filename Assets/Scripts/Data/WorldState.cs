using UnityEngine;


// Everything that is not global to the whole save. A checkpoint holds one of these, so anything added
// here is automatically captured and restored by every checkpoint - keep it to world state only.
[System.Serializable]
public class WorldState
{
    public TerrainSaveData terrain;
    public EntitySaveData entities;
    public SerializableDictionary<string, int> inventory;
    public PuzzleSaveData puzzles;
    public NotificationSaveData notifications;
    public AnimalSaveData animals;
    public TimeSaveData time;


    // default constructor
    public WorldState()
    {
        terrain = new TerrainSaveData();
        entities = new EntitySaveData();
        inventory = new SerializableDictionary<string, int>();
        puzzles = new PuzzleSaveData();
        notifications = new NotificationSaveData();
        animals = new AnimalSaveData();
        time = new TimeSaveData();
    }


    // A section that is missing from the file arrives as null, and every reader assumes it exists
    public void EnsureSections()
    {
        if (terrain == null) terrain = new TerrainSaveData();
        if (entities == null) entities = new EntitySaveData();
        if (inventory == null) inventory = new SerializableDictionary<string, int>();
        if (puzzles == null) puzzles = new PuzzleSaveData();
        if (notifications == null) notifications = new NotificationSaveData();
        if (animals == null) animals = new AnimalSaveData();
        if (time == null) time = new TimeSaveData();
    }


    // Deep copy for a checkpoint. A JsonUtility round trip is fine: every section is plain serializable
    // data, and it takes the SerializableDictionary with it through its serialized key/value lists.
    public WorldState Clone()
    {
        WorldState copy = JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(this));

        if (copy == null)
        {
            Debug.LogError("WorldState: the world could not be cloned, so a checkpoint of it " +
                "would have been empty - returning a blank world instead");
            return new WorldState();
        }

        copy.EnsureSections();
        return copy;
    }


    public void Sort()
    {
        if (terrain != null) terrain.Sort();
        if (entities != null) entities.Sort();
        if (puzzles != null) puzzles.Sort();
        if (notifications != null) notifications.Sort();
        if (animals != null) animals.Sort();

        // inventory is not sorted: JsonUtility writes it through the dictionary's own serialized
        // key/value lists, so its file order is whatever the dictionary enumerated
    }
}
