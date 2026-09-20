using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Important notes:
// the Grid sits at the origin with a 90 degree X rotation so grid x is world x, grid y is world z
// and a level's height is world y. Tiles are exactly 1 unit by 1 unit in x and z, but levels
// may be any distance apart in y. These things really should not change
// or the entire Terrain scripts need rewriting.
//
// A terrain cell is a Vector3Int of (x, level, z), matching world axis order, where level is an
// index into the level table rather than a height.
public class TerrainGrid : MonoBehaviour
{
    private const float LEVEL_THRESHOLD = 0.001f;  // tolerance within which to match a level's y position

    public struct Level
    {
        public float height;  // world y of this layer's surface
        public Tilemap tilemap;
    }


    private List<Level> levels;
    private TerrainState state;


    private void Awake()
    {
        state = BuildStateFromTilemaps();
    }


    // for running in the editor without Awake()
    private List<Level> Levels
    {
        get
        {
            if (levels == null) levels = DiscoverLevels(transform);
            return levels;
        }
    }


    // MARK: level discovery


    // index 0 is always the lowest layer
    // !! this method is the canonical source of levels !!
    public static List<Level> DiscoverLevels(Transform root)
    {
        List<Level> discovered = new List<Level>();

        foreach (Tilemap tilemap in root.GetComponentsInChildren<Tilemap>())
        {
            // an empty tilemap is skipped
            BoundsInt bounds = tilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0) continue;

            discovered.Add(new Level
            {
                height = tilemap.transform.position.y,
                tilemap = tilemap,
            });
        }

        discovered.Sort((a, b) => a.height.CompareTo(b.height));

        // two tilemaps within LEVEL_THRESHOLD are considered duplicates
        // and flagged --- you probably don't want this, and if you do,
        // haha you have to fix the code first
        for (int i = discovered.Count - 1; i > 0; i--)
        {
            if (discovered[i].height - discovered[i - 1].height > LEVEL_THRESHOLD) continue;

            Debug.LogWarning($"'{discovered[i].tilemap.name}' is at the same height as " +
                $"'{discovered[i - 1].tilemap.name}' - ignoring it", discovered[i].tilemap);

            discovered.RemoveAt(i);
        }

        return discovered;
    }


    // MARK: level table


    public int LevelCount { get { return Levels.Count; } }


    public float GetLevelHeight(int level)
    {
        return Levels[level].height;
    }


    public Tilemap GetTilemap(int level)
    {
        return Levels[level].tilemap;
    }


    public bool TryGetLevelAtHeight(float worldY, out int level)
    {
        for (int i = 0; i < Levels.Count; i++)
        {
            if (Mathf.Abs(Levels[i].height - worldY) <= LEVEL_THRESHOLD)
            {
                level = i;
                return true;
            }
        }

        level = -1;
        return false;
    }


    // get the highest level at or below worldY
    // if there is none, returns -1
    public int GetLevelBelow(float worldY)
    {
        int found = -1;

        for (int i = 0; i < Levels.Count; i++)
        {
            // LEVEL_THRESHOLD accounts for floating point error
            // when height == worldY
            if (Levels[i].height <= worldY + LEVEL_THRESHOLD) found = i;
            else break;
        }

        return found;
    }


    // MARK: coordinates


    // Terrain space: (x, level, z), matching world axes
    // Not the same as a tilemap cell because tilemaps are 2D
    public Vector3Int WorldToCell(Vector3 worldPosition)
    {
        return new Vector3Int(
            Mathf.FloorToInt(worldPosition.x),
            GetLevelBelow(worldPosition.y),
            Mathf.FloorToInt(worldPosition.z));
    }


    // When interfacing with an individual tilemap, level becomes a key on which tilemap to use
    // rather than a coordinate within the tilemap, which is of course 2D. The tilemap requires
    // (x, y, 0) where y is actually world z as mentioned above.
    public static Vector3Int ToTilemapCell(Vector3Int cell)
    {
        return new Vector3Int(cell.x, cell.z, 0);
    }


    // Offsets into the XZ centre of the cell
    public Vector3 CellToWorld(Vector3Int cell)
    {
        return new Vector3(cell.x + 0.5f, GetLevelHeight(cell.y), cell.z + 0.5f);
    }


    // MARK: state


    public TerrainState State { get { return state; } }


    // Reads every tilemap into a flat grid
    // Tilemaps are dev only, so there is no BuildTilemapsFromState.
    public TerrainState BuildStateFromTilemaps()
    {
        int cellMinX = int.MaxValue, cellMinZ = int.MaxValue;
        int cellMaxX = int.MinValue, cellMaxZ = int.MinValue;

        // same union the raster spans, so the two agree on what a cell is
        foreach (Level level in Levels)
        {
            BoundsInt bounds = level.tilemap.cellBounds;

            cellMinX = Mathf.Min(cellMinX, bounds.xMin);
            cellMinZ = Mathf.Min(cellMinZ, bounds.yMin);
            cellMaxX = Mathf.Max(cellMaxX, bounds.xMax);
            cellMaxZ = Mathf.Max(cellMaxZ, bounds.yMax);
        }

        float[] levelHeights = new float[Levels.Count];
        for (int level = 0; level < Levels.Count; level++)
        {
            levelHeights[level] = Levels[level].height;
        }

        TerrainState built = new TerrainState(cellMinX, cellMinZ,
            cellMaxX - cellMinX, cellMaxZ - cellMinZ, levelHeights);

        for (int level = 0; level < Levels.Count; level++)
        {
            Tilemap tilemap = Levels[level].tilemap;

            foreach (Vector3Int tilemapCell in tilemap.cellBounds.allPositionsWithin)
            {
                TileBase tile = tilemap.GetTile(tilemapCell);
                if (tile == null) continue;

                // tilemap (x, y, 0) is terrain (x, level, z)
                built.SetTile(new Vector3Int(tilemapCell.x, level, tilemapCell.y), tile);
            }
        }

        return built;
    }


    // MARK: queries


    // Check with the tilemaps if a cell has a tile in it
    public bool IsOccupied(Vector3Int cell)
    {
        if (cell.y < 0 || cell.y >= Levels.Count) return false;

        return Levels[cell.y].tilemap.HasTile(ToTilemapCell(cell));
    }


    // MARK: debug


    // this method validates that a world position returns the same cell 
    // via our assumptions and the tilemap's own transform
    public bool MatchesTilemap(Vector3 worldPosition, Vector3Int cell)
    {
        if (cell.y < 0 || cell.y >= Levels.Count) return false;

        return GetTilemap(cell.y).WorldToCell(worldPosition) == ToTilemapCell(cell);
    }


    // And here is a cheeky debug drawing of a cell so you can visually
    // check that the mapping functions are not completely fucked
    public void DrawCell(Vector3Int cell, Color colour, float duration = 0.0f)
    {
        float y = GetLevelHeight(cell.y) + LEVEL_THRESHOLD;
        float x = cell.x;
        float z = cell.z;

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
