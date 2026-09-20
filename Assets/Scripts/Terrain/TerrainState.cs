using UnityEngine;
using UnityEngine.Tilemaps;

// What is actually on the map, one tile per cell.
//
// Dense per level over the union of every tilemap's bounds, which is the same region the
// bake rasterises, so a state cell and a raster cell are the same cell. That matters later:
// a tile swap has to find the quads that were baked for its cell.
//
// The map's shape never changes at runtime, only which tile sits in a cell, so this is
// sized once and then only ever written through SetTile.
public class TerrainState
{
    // cell space bounds, matching world axes (the raster calls z "y" because it thinks in tilemaps)
    public readonly int originX;
    public readonly int originZ;
    public readonly int width;
    public readonly int depth;

    public readonly float[] levelHeights;  // world y per level, ascending

    private readonly TileBase[][] tileByCell;  // [level][z * width + x]


    public TerrainState(int originX, int originZ, int width, int depth, float[] levelHeights)
    {
        this.originX = originX;
        this.originZ = originZ;
        this.width = width;
        this.depth = depth;
        this.levelHeights = levelHeights;

        tileByCell = new TileBase[levelHeights.Length][];

        for (int level = 0; level < levelHeights.Length; level++)
        {
            tileByCell[level] = new TileBase[width * depth];
        }
    }


    public int LevelCount { get { return levelHeights.Length; } }


    // cell is (x, level, z) - see TerrainGrid
    public bool Contains(Vector3Int cell)
    {
        if (cell.y < 0 || cell.y >= levelHeights.Length) return false;

        int localX = cell.x - originX;
        int localZ = cell.z - originZ;

        return localX >= 0 && localX < width && localZ >= 0 && localZ < depth;
    }


    // null for an empty cell, or for one outside the map
    public TileBase GetTile(Vector3Int cell)
    {
        if (!Contains(cell)) return null;

        return tileByCell[cell.y][(cell.z - originZ) * width + (cell.x - originX)];
    }


    public void SetTile(Vector3Int cell, TileBase tile)
    {
        if (!Contains(cell)) return;

        tileByCell[cell.y][(cell.z - originZ) * width + (cell.x - originX)] = tile;
    }
}
