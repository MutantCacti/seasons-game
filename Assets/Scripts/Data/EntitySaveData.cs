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

            return string.CompareOrdinal(a.itemId, b.itemId);
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
}
