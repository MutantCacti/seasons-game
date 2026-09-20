using UnityEngine;
using UnityEngine.Tilemaps;

// Just a reference for how you would go about using the terrain system
// e.g. for placement how you would get cell positions from world position
//
// Important notes:
// the Grid sits at the origin with a 90 degree X rotation so grid x is world x, grid y is world z
// and a TerrainLayer's level is world y. Tiles are exactly 1 unit by 1 unit
// These things really should not change or the entire Terrain scripts need rewriting.

public class TileGridHelper
{
    private const float NUDGE = 0.01f;


    // Terrain space: (x, level, z), matching world axes
    // Not the same as a tilemap cell because tilemaps are 2D
    public Vector3Int GetCellPosition(Vector3 worldPosition)
    {
        int cellX = Mathf.FloorToInt(worldPosition.x);
        int cellY = Mathf.FloorToInt(worldPosition.z);  // tilemap Y is world Z because of that 90 degree rotation
        int level = Mathf.RoundToInt(worldPosition.y);

        return new Vector3Int(cellX, level, cellY);
    }

    // When interfacing with an individual tilemap, level becomes a key
    // on which tilemap to use rather than a coordinate within the tilemap
    // which is of course 2D. The tilemap requires (x, y, 0) where y is actually
    // world z as mentioned above.
    public Vector3Int ToTilemapCell(Vector3Int terrainCell)
    {
        return new Vector3Int(terrainCell.x, terrainCell.z, 0);
    }


    // Utility to offset into the centre of a cell
    public Vector3 GetCellCentre(Vector3Int terrainCell)
    {
        return new Vector3(terrainCell.x + 0.5f, terrainCell.y, terrainCell.z + 0.5f);
    }


    // Since the tilemaps are still used for creating the map and debugging, they shouldn't
    // drift out of sync with the baked meshes --- this method validates that a world position
    // returns the same cell via our assumptions and the tilemap's own transform
    public bool MatchesTilemap(Tilemap tilemap, Vector3 worldPosition, Vector3Int terrainCell)
    {
        return tilemap.WorldToCell(worldPosition) == ToTilemapCell(terrainCell);
    }


    // Check if a tilemap cell is occupied
    public bool IsOccupied(Tilemap tilemap, Vector3Int terrainCell)
    {
        return tilemap.HasTile(ToTilemapCell(terrainCell));
    }


    // Tilemap lookup from level
    // Assumes all the tilemaps are children of gridRoot (currently GO "Grid" in Scene "Game")
    public Tilemap FindTilemapForLevel(Transform gridRoot, int level)
    {
        foreach (TerrainLayer terrainLayer in gridRoot.GetComponentsInChildren<TerrainLayer>(true))
        {
            if (terrainLayer.layer == level) return terrainLayer.GetComponent<Tilemap>();
        }

        return null;
    }


    // And here is a cheeky debug drawing of a cell so you can visually
    // check that the mapping functions are not completely fucked
    public void DrawCell(Vector3Int terrainCell, Color colour, float duration = 0.0f)
    {
        float y = terrainCell.y + 0.01f;
        Vector3 a = new Vector3(terrainCell.x, y, terrainCell.z);
        Vector3 b = new Vector3(terrainCell.x + 1, y, terrainCell.z);
        Vector3 c = new Vector3(terrainCell.x + 1, y, terrainCell.z + 1);
        Vector3 d = new Vector3(terrainCell.x, y, terrainCell.z + 1);

        Debug.DrawLine(a, b, colour, duration);
        Debug.DrawLine(b, c, colour, duration);
        Debug.DrawLine(c, d, colour, duration);
        Debug.DrawLine(d, a, colour, duration);
    }
}
