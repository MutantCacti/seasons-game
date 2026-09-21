using System.Collections.Generic;


// occupancy is fixed at build time - the player never adds or removes a tile
// the player only ever swaps which TerrainTile sits in an occupied cell
// TerrainSaveData only stores differences from the tilemaps
[System.Serializable]
public class TerrainSaveData
{
    // To check what tilemaps this save was made with
    // allows warning that the map has changed when loading an older save
    public int originX;
    public int originZ;
    public int width;
    public int depth;
    public float[] levelHeights;

    public List<TerrainCellSave> remaps = new List<TerrainCellSave>();


    public void Stamp(int originX, int originZ, int width, int depth, float[] levelHeights)
    {
        this.originX = originX;
        this.originZ = originZ;
        this.width = width;
        this.depth = depth;
        this.levelHeights = levelHeights != null ? (float[])levelHeights.Clone() : new float[0];
    }


    public void Clear()
    {
        remaps.Clear();
    }


    // O(N)
    public void SetRemap(int x, int z, int levelY, string tileId)
    {
        for (int i = 0; i < remaps.Count; i++)
        {
            if (remaps[i].x != x || remaps[i].z != z || remaps[i].levelY != levelY) continue;

            TerrainCellSave entry = remaps[i];
            entry.tileId = tileId;
            remaps[i] = entry;   // TerrainCellSave is a struct, so it has to be written back
            return;
        }

        remaps.Add(new TerrainCellSave { x = x, z = z, levelY = levelY, tileId = tileId });
    }


    public void Sort()
    {
        remaps.Sort((a, b) =>
        {
            int byLevel = a.levelY.CompareTo(b.levelY);
            if (byLevel != 0) return byLevel;

            int byZ = a.z.CompareTo(b.z);
            return byZ != 0 ? byZ : a.x.CompareTo(b.x);
        });
    }
}


[System.Serializable]
public struct TerrainCellSave
{
    public int x;         // absolute terrain cell x
    public int z;         // absolute terrain cell z
    public int levelY;    // level world y in millimetres
    public string tileId;
}
