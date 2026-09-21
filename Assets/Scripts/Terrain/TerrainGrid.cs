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
public partial class TerrainGrid : MonoBehaviour
{
    private const float LEVEL_THRESHOLD = 0.001f;  // tolerance within which to match a level's y position
    private const float HEIGHT_STEPS = 1000.0f;    // reciprocal of LEVEL_THRESHOLD, see SnapHeight

    public struct Level
    {
        public float height;  // world y of this layer's surface
        public Tilemap tilemap;
    }


    [SerializeField] private TerrainTileRegistry registry;
    [SerializeField] private TerrainVisuals visuals;

    private List<Level> levels;
    private State state;
    private SaveData saveData;


    private void Awake()
    {
        state = BuildStateFromTilemaps();

        if (registry == null)
        {
            Debug.LogError("TerrainGrid: no TerrainTileRegistry assigned, so remaps can never be loaded or saved", this);
        }

        if (visuals == null) visuals = GetComponent<TerrainVisuals>();

        if (visuals != null)
        {
            // ensure that the map has not updated cuz we can't patch it then
            visuals.Validate(OccupancyHash(Levels));
        }

        // SaveDataManager is in the persistent scene so its Awake has already deserialised by now
        saveData = SaveDataManager.Instance != null ? SaveDataManager.Instance.SaveData : null;

        if (saveData == null)
        {
            Debug.LogWarning("TerrainGrid: no SaveDataManager in the scene, so remaps will not be saved", this);
            saveData = new SaveData();
        }

        WarnIfMapChanged(saveData.terrain);
        ApplyRemaps(saveData.terrain);

        saveData.terrain.Stamp(state.originX, state.originZ, state.width, state.depth, state.levelHeights);
    }


    public void SetVisuals(TerrainVisuals target)
    {
        visuals = target;
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
                height = SnapHeight(tilemap.transform.position.y),
                tilemap = tilemap,
            });
        }

        discovered.Sort((a, b) => a.height.CompareTo(b.height));



        // two tilemaps within LEVEL_THRESHOLD are considered duplicates
        // and flagged --- you probably don't want this to change, 
        // but if you do, haha you have to fix the code first
        for (int i = discovered.Count - 1; i > 0; i--)
        {
            if (discovered[i].height - discovered[i - 1].height > LEVEL_THRESHOLD) continue;

            Debug.LogWarning($"'{discovered[i].tilemap.name}' is at the same height as " +
                $"'{discovered[i - 1].tilemap.name}' - ignoring it", discovered[i].tilemap);

            discovered.RemoveAt(i);
        }

        return discovered;
    }


    // handy dandy: turn 1.0000001f into 1.0f
    private static float SnapHeight(float worldY)
    {
        return Mathf.Round(worldY * HEIGHT_STEPS) / HEIGHT_STEPS;
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


    // Reads every tilemap into a flat grid
    // Tilemaps are dev only, so there is no BuildTilemapsFromState.
    private State BuildStateFromTilemaps()
    {
        int cellMinX = int.MaxValue, cellMinZ = int.MaxValue;
        int cellMaxX = int.MinValue, cellMaxZ = int.MinValue;

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

        State built = new State(
            cellMinX, cellMinZ,
            cellMaxX - cellMinX, cellMaxZ - cellMinZ,
            levelHeights
        );

        for (int level = 0; level < Levels.Count; level++)
        {
            Tilemap tilemap = Levels[level].tilemap;

            foreach (Vector3Int tilemapCell in tilemap.cellBounds.allPositionsWithin)
            {
                TileBase tile = tilemap.GetTile(tilemapCell);
                if (tile == null) continue;

                // Only TerrainTile's can be saved !!
                if (!(tile is TerrainTile terrainTile))
                {
                    Debug.LogError($"TerrainGrid: '{tile.name}' in '{tilemap.name}' is not a TerrainTile, so it can never be saved", tile);
                    continue;
                }

                // tilemap (x, y, 0) is terrain (x, level, z)
                built.SetTile(new Vector3Int(tilemapCell.x, level, tilemapCell.y), terrainTile);
            }
        }

        return built;
    }


    // MARK: queries


    // Reads the runtime state, not the tilemaps. The tilemaps stop being the truth the moment
    // anything is remapped, which is what this whole path exists to do.
    public bool IsOccupied(Vector3Int cell)
    {
        return state.GetTile(cell) != null;
    }


    public TerrainTile GetTile(Vector3Int cell)
    {
        return state.GetTile(cell);
    }


    // MARK: remapping


    // The one and only way terrain changes. Everything that needs to know about a remap hangs off
    // here: the state, the live save, and the rendered mesh.
    public bool Remap(Vector3Int cell, TerrainTile tile)
    {
        if (tile == null) return false;
        if (registry == null) return false;

        // occupancy is fixed at build time, so remapping a hole means something upstream is wrong
        // rather than that the player is doing something new
        if (state.GetTile(cell) == null)
        {
            Debug.LogError($"TerrainGrid: cell {cell} is not occupied, so it cannot be remapped", this);
            return false;
        }

        if (!registry.TryGetIdByTile(tile, out string tileId)) return false;

        state.SetTile(cell, tile);
        saveData.terrain.SetRemap(cell.x, cell.z, ToLevelMillimetres(state.levelHeights[cell.y]), tileId);
        SetCellVisual(cell, tile);

        return true;
    }


    // The one place a tile becomes visible. Both Remap and ApplyRemaps go through here, so a
    // remap restored from a save looks the same as one the player just made.
    private void SetCellVisual(Vector3Int cell, TerrainTile tile)
    {
        if (visuals == null || tile == null) return;

        // Resolved against the tilemap's own occupancy rather than this class's state, so the
        // sprite comes out exactly as the bake resolved it: same neighbour mask, same slot.
        //
        // VariantFor hashes the *tilemap* cell, which is why this is ToTilemapCell and not the
        // terrain cell - passing the terrain cell would pick a different random variant than the
        // bake did and the tile would change appearance on load.
        Vector3Int tilemapCell = ToTilemapCell(cell);
        Tilemap tilemap = GetTilemap(cell.y);

        Sprite sprite = tilemap != null
            ? tile.SpriteFor(tilemapCell, tilemap)
            : tile.VariantFor(tilemapCell);

        visuals.Patch(cell, sprite);
    }


    // MARK: level key


    // Levels are keyed by world y rather than index because a level is positional: inserting a new
    // elevation shifts every index above it, which would silently move saves to the wrong storey.
    // Millimetres keep it an int, so no float ever reaches the file or a comparison by equality.
    public static int ToLevelMillimetres(float height)
    {
        return Mathf.RoundToInt(height * HEIGHT_STEPS);
    }


    public bool TryGetLevelAtMillimetres(int millimetres, out int level)
    {
        return TryGetLevelAtHeight(millimetres / HEIGHT_STEPS, out level);
    }


    // MARK: load


    private void WarnIfMapChanged(TerrainSaveData terrain)
    {
        if (terrain == null) return;

        // an unstamped section is a fresh save, not a changed map
        if (terrain.width <= 0) return;

        if (terrain.originX != state.originX || terrain.originZ != state.originZ ||
            terrain.width != state.width || terrain.depth != state.depth)
        {
            Debug.LogWarning($"TerrainGrid: the authored map is now {state.width}x{state.depth} at " +
                $"({state.originX}, {state.originZ}) but this save was written against " +
                $"{terrain.width}x{terrain.depth} at ({terrain.originX}, {terrain.originZ}) - " +
                $"remaps that fall outside it will be skipped", this);
        }

        int savedLevels = terrain.levelHeights != null ? terrain.levelHeights.Length : 0;
        if (savedLevels != state.levelHeights.Length)
        {
            Debug.LogWarning($"TerrainGrid: this save was written against {savedLevels} levels and " +
                $"the map now has {state.levelHeights.Length}", this);
        }
    }


    // Every entry is validated on its own, so a changed map degrades into skipped cells with a log
    // rather than tiles landing in the wrong places
    private void ApplyRemaps(TerrainSaveData terrain)
    {
        if (terrain == null || terrain.remaps == null) return;
        if (registry == null) return;   // already reported in Awake

        int applied = 0;
        int skipped = 0;

        foreach (TerrainCellSave remap in terrain.remaps)
        {
            if (!TryGetLevelAtMillimetres(remap.levelY, out int level))
            {
                Debug.LogWarning($"TerrainGrid: save names a level at {remap.levelY}mm which no longer " +
                    $"exists - skipping cell ({remap.x}, {remap.z})", this);
                skipped++;
                continue;
            }

            Vector3Int cell = new Vector3Int(remap.x, level, remap.z);

            if (state.GetTile(cell) == null)
            {
                Debug.LogWarning($"TerrainGrid: save remaps cell {cell}, which the authored map does " +
                    $"not occupy - skipping", this);
                skipped++;
                continue;
            }

            if (!registry.TryGetTileById(remap.tileId, out TerrainTile tile))
            {
                Debug.LogWarning($"TerrainGrid: save names tile id '{remap.tileId}' which is not in " +
                    $"the registry - skipping cell {cell}", this);
                skipped++;
                continue;
            }

            state.SetTile(cell, tile);
            SetCellVisual(cell, tile);
            applied++;
        }

        if (skipped > 0)
        {
            Debug.LogWarning($"TerrainGrid: applied {applied} remaps, skipped {skipped}", this);
        }
    }


    // MARK: occupancy hash


    // get a hash based on what cells are occupied
    // this is sort of the only good way to check if the map has been updated in the editor
    public static uint OccupancyHash(IReadOnlyList<Level> levels)
    {
        if (levels == null || levels.Count == 0) return 0u;

        int minX = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxZ = int.MinValue;

        foreach (Level level in levels)
        {
            BoundsInt bounds = level.tilemap.cellBounds;

            minX = Mathf.Min(minX, bounds.xMin);
            minZ = Mathf.Min(minZ, bounds.yMin);
            maxX = Mathf.Max(maxX, bounds.xMax);
            maxZ = Mathf.Max(maxZ, bounds.yMax);
        }

        uint hash = TerrainHash.FNV_OFFSET_BASIS;
        hash = TerrainHash.Fold(hash, levels.Count);
        hash = TerrainHash.Fold(hash, minX);
        hash = TerrainHash.Fold(hash, minZ);
        hash = TerrainHash.Fold(hash, maxX);
        hash = TerrainHash.Fold(hash, maxZ);

        for (int level = 0; level < levels.Count; level++)
        {
            hash = TerrainHash.Fold(hash, ToLevelMillimetres(levels[level].height));

            Tilemap tilemap = levels[level].tilemap;

            for (int z = minZ; z < maxZ; z++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    hash = TerrainHash.Fold(hash, tilemap.HasTile(new Vector3Int(x, z, 0)) ? 1 : 0);
                }
            }
        }

        return hash;
    }


    // MARK: debug


    // DEBUG: remap one random occupied cell holding fromId over to toId
    public bool DebugRemapRandomTile(string fromId, string toId, out Vector3Int changedCell)
    {
        changedCell = default;

        if (state == null || registry == null) return false;

        if (!registry.TryGetTileById(toId, out TerrainTile toTile))
        {
            Debug.LogError($"TerrainGrid: no registered tile with id '{toId}'", this);
            return false;
        }

        List<Vector3Int> candidates = new List<Vector3Int>();

        for (int level = 0; level < state.LevelCount; level++)
        {
            for (int z = state.originZ; z < state.originZ + state.depth; z++)
            {
                for (int x = state.originX; x < state.originX + state.width; x++)
                {
                    Vector3Int cell = new Vector3Int(x, level, z);

                    TerrainTile tile = state.GetTile(cell);
                    if (tile == null) continue;

                    if (!registry.TryGetIdByTile(tile, out string id) || id != fromId) continue;

                    candidates.Add(cell);
                }
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"TerrainGrid: no occupied cells holding tile id '{fromId}'", this);
            return false;
        }

        changedCell = candidates[Random.Range(0, candidates.Count)];
        return Remap(changedCell, toTile);
    }


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
