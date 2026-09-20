using UnityEngine;
using UnityEngine.Tilemaps;

// Just a reference for how you would go about using the terrain system
// e.g. for placement how you would get cell positions from world position
//
// Important notes:
// the Grid sits at the origin with a 90 degree X rotation so grid x is world x, grid y is world z
// and a level's height is world y, read off the tilemap's transform by TerrainLevels.
// Tiles are exactly 1 unit by 1 unit in x and z, but levels may be any distance apart in y,
// so a level is a float.
// These things really should not change or the entire Terrain scripts need rewriting.


// Terrain space: (x, level, z), matching world axes
// Not the same as a tilemap cell because tilemaps are 2D.
// level is a world y rather than a coordinate within the tilemap, so it is a float
public struct TerrainCell
{
    public int x;
    public float level;  // world y of the layer's surface
    public int z;
}


public class TileGridHelper
{
    private const float NUDGE = 0.01f;
    private const float LEVEL_EPSILON = 0.001f;  // tolerance within which to match a level's y position


    // Terrain space: (x, level, z), matching world axes
    // Not the same as a tilemap cell because tilemaps are 2D
    public TerrainCell GetCellPosition(Vector3 worldPosition)
    {
        TerrainCell terrainCell;
        terrainCell.x = Mathf.FloorToInt(worldPosition.x);
        terrainCell.z = Mathf.FloorToInt(worldPosition.z);
        // levels are not 1 unit apart any more, so this can't be rounded to one.
        // it is a world y that FindTilemapForLevel resolves to a tilemap
        terrainCell.level = worldPosition.y;

        return terrainCell;
    }


    // When interfacing with an individual tilemap, level becomes a key
    // on which tilemap to use rather than a coordinate within the tilemap
    // which is of course 2D. The tilemap requires (x, y, 0) where y is actually
    // world z as mentioned above.
    public Vector3Int ToTilemapCell(TerrainCell terrainCell)
    {
        return new Vector3Int(terrainCell.x, terrainCell.z, 0);
    }


    // Utility to offset into the centre of a cell
    public Vector3 GetCellCentre(TerrainCell terrainCell)
    {
        return new Vector3(terrainCell.x + 0.5f, terrainCell.level, terrainCell.z + 0.5f);
    }


    // Since the tilemaps are still used for creating the map and debugging, they shouldn't
    // drift out of sync with the baked meshes --- this method validates that a world position
    // returns the same cell via our assumptions and the tilemap's own transform
    public bool MatchesTilemap(Tilemap tilemap, Vector3 worldPosition, TerrainCell terrainCell)
    {
        return tilemap.WorldToCell(worldPosition) == ToTilemapCell(terrainCell);
    }


    // Check if a tilemap cell is occupied
    public bool IsOccupied(Tilemap tilemap, TerrainCell terrainCell)
    {
        return tilemap.HasTile(ToTilemapCell(terrainCell));
    }


    // Tilemap lookup from level
    // Assumes all the tilemaps are children of gridRoot (currently GO "Grid" in Scene "Game")
    // level is a world y, so it is matched with a tolerance rather than exactly
    // NB: rediscovers every call, and returns only the first tilemap at a height even
    // though a level may hold several. TerrainGrid replaces this with a cached table.
    public Tilemap FindTilemapForLevel(Transform gridRoot, float level)
    {
        foreach (TerrainLevels.Level terrainLevel in TerrainLevels.Discover(gridRoot))
        {
            if (Mathf.Abs(terrainLevel.height - level) <= LEVEL_EPSILON) return terrainLevel.tilemaps[0];
        }

        return null;
    }


    // And here is a cheeky debug drawing of a cell so you can visually
    // check that the mapping functions are not completely fucked
    public void DrawCell(TerrainCell terrainCell, Color colour, float duration = 0.0f)
    {
        float y = terrainCell.level + NUDGE;
        float x = terrainCell.x;
        float z = terrainCell.z;

        Vector3 a = new Vector3(x, y, z);
        Vector3 b = new Vector3(x + 1, y, z);
        Vector3 c = new Vector3(x + 1, y, z + 1);
        Vector3 d = new Vector3(x, y, z + 1);

        Debug.DrawLine(a, b, colour, duration);
        Debug.DrawLine(b, c, colour, duration);
        Debug.DrawLine(c, d, colour, duration);
        Debug.DrawLine(d, a, colour, duration);
    }
}
