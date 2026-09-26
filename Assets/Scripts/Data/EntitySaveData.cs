using System.Collections.Generic;


// Full state of all entities in the scene
[System.Serializable]
public class EntitySaveData
{
    public List<EntitySave> entities = new List<EntitySave>();


    public void Clear()
    {
        entities.Clear();
    }


    public void Add(EntitySave entry)
    {
        entities.Add(entry);
    }


    // Stable file order. positional ties broken by id
    public void Sort()
    {
        entities.Sort((a, b) =>
        {
            int byLevel = a.levelY.CompareTo(b.levelY);
            if (byLevel != 0) return byLevel;

            int byZ = a.z.CompareTo(b.z);
            if (byZ != 0) return byZ;

            int byX = a.x.CompareTo(b.x);
            if (byX != 0) return byX;

            int byItem = string.CompareOrdinal(a.itemId, b.itemId);
            if (byItem != 0) return byItem;

            // two entities can only tie this far if they are the same item in the same cell, which
            // placement itself prevents - the guid just keeps the file order from depending on
            // whatever order they happened to be spawned in
            return string.CompareOrdinal(a.guid, b.guid);
        });
    }
}


[System.Serializable]
public struct EntitySave
{
    public string itemId;   // resolves through GameItemRegistry to the prefab that gets instantiated
    public int x;           // absolute terrain cell x
    public int z;           // absolute terrain cell z
    public int levelY;      // level world y in millimetres
    public string guid;     // this instance's EntityId, so a puzzle can name the entity that completed it

    // one the world started with (a WorldFixtures entry) rather than one the player placed, so it never
    // counts towards a puzzle that wants things placed
    public bool fixture;
}
