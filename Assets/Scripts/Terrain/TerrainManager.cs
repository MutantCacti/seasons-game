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
public partial class TerrainManager : MonoBehaviour
{
    private static TerrainManager instance;
    public static TerrainManager Instance { get { return instance; } }


    private const float LEVEL_THRESHOLD = 0.001f;  // tolerance within which to match a level's y position
    private const float HEIGHT_STEPS = 1000.0f;    // reciprocal of LEVEL_THRESHOLD, see SnapHeight


    public struct Level
    {
        public float height;  // world y of this layer's surface
        public Tilemap tilemap;
    }


    [SerializeField] private TerrainTileRegistry registry;
    [SerializeField] private TerrainVisuals visuals;
    [SerializeField] private Transform tilemapRoot;


    private List<Level> levels;
    private State state;
    private WorldState world;


    private void Awake()
    {
        if (instance != this && instance != null)
        {
            Debug.LogWarning("TerrainManager: Found more than one TerrainManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        state = BuildStateFromTilemaps();

        if (registry == null)
        {
            Debug.LogError("TerrainManager: no TerrainTileRegistry assigned, so remaps can never be loaded or saved", this);
        }

        if (visuals == null)
        {
            Debug.LogError("TerrainManager: no TerrainVisuals assigned, so remaps can't be done", this);
        }

        if (visuals != null)
        {
            // ensure that the map has not updated cuz we can't patch it then
            visuals.Validate(VisualHash(Levels));
        }

        if (tilemapRoot == null)
        {
            Debug.LogError("TerrainManager: no tilemapRoot assigned, so tilemaps can't be found", this);
        }

        // SaveDataManager is in the persistent scene so its Awake has already deserialised by now.
        // The whole world is cached, not just its terrain section: SaveData.world is what the save
        // managers hold, and it is only ever swapped out by a new game or a checkpoint restore.
        SaveDataManager saveManager = SaveDataManager.Instance;
        world = saveManager != null && saveManager.SaveData != null ? saveManager.SaveData.world : null;

        if (world == null)
        {
            Debug.LogWarning("TerrainManager: no SaveDataManager in the scene, so remaps will not be saved", this);
            world = new WorldState();
        }

        world.EnsureSections();

        WarnIfMapChanged(world.terrain);
        ApplyRemaps(world.terrain);

        world.terrain.Stamp(state.originX, state.originZ, state.width, state.depth, state.levelHeights);
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
            if (levels == null) levels = DiscoverLevels(tilemapRoot);
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
                    Debug.LogError($"TerrainManager: '{tile.name}' in '{tilemap.name}' is not a TerrainTile, so it can never be saved", tile);
                    continue;
                }

                // tilemap (x, y, 0) is terrain (x, level, z)
                built.SetTile(new Vector3Int(tilemapCell.x, level, tilemapCell.y), terrainTile);
            }
        }

        return built;
    }


    // MARK: bounds


    // The map's XZ footprint in world space, straight from the cell bounds. Cells are exactly
    // one unit square and the grid maps cell x/z onto world x/z with no transform, so the cell
    // bounds are the world bounds. The map never changes shape at runtime, so callers may cache.
    public void GetWorldXZBounds(out Vector2 min, out Vector2 max)
    {
        min = new Vector2(state.originX, state.originZ);
        max = new Vector2(state.originX + state.width, state.originZ + state.depth);
    }


    // MARK: queries


    public bool IsOccupied(Vector3Int cell)
    {
        return state.GetTile(cell) != null;
    }


    public TerrainTile GetTile(Vector3Int cell)
    {
        return state.GetTile(cell);
    }


    public int CountTile(TerrainTile tile)
    {
        return state != null ? state.CountTile(tile) : 0;
    }


    // Fills results with the cells the player has changed that hold this tile right now: the terrain
    // save's remaps, so the world's own authored tiles never count. It is what is there NOW, not what
    // was ever placed: a cell changed to the tile and later changed again (dirt watered into grass)
    // stops counting. One pass over the remaps, which only grow as the player places tiles
    public void CollectPlayerCellsOf(TerrainTile tile, List<Vector3Int> results)
    {
        results.Clear();
        if (tile == null || registry == null || world == null || world.terrain == null || world.terrain.remaps == null) return;
        if (!registry.TryGetIdByEntry(tile, out string tileId)) return;

        List<TerrainCellSave> remaps = world.terrain.remaps;

        for (int i = 0; i < remaps.Count; i++)
        {
            TerrainCellSave remap = remaps[i];
            if (remap.tileId != tileId) continue;
            if (!TryGetLevelAtMillimetres(remap.levelY, out int level)) continue;

            Vector3Int cell = new Vector3Int(remap.x, level, remap.z);
            if (state.GetTile(cell) == tile) results.Add(cell);
        }
    }


    // Fills results with every cell holding this tile, on every level. A whole-map scan, so it is for
    // events (a load, a puzzle unlocking), not for every frame
    public void CollectCellsOf(TerrainTile tile, List<Vector3Int> results)
    {
        results.Clear();
        if (tile == null || state == null || state.CountTile(tile) == 0) return;

        for (int level = 0; level < state.LevelCount; level++)
        {
            for (int z = state.originZ; z < state.originZ + state.depth; z++)
            {
                for (int x = state.originX; x < state.originX + state.width; x++)
                {
                    Vector3Int cell = new Vector3Int(x, level, z);
                    if (state.GetTile(cell) == tile) results.Add(cell);
                }
            }
        }
    }


    // MARK: placement targeting


    // How far past a surface to step before flooring to a cell
    private const float SURFACE_PROBE = 0.01f;


    // The topmost occupied level in a column, or -1 when the column is empty. Occupancy is fixed at
    // build time, so unlike a tile's identity this never changes at runtime.
    public int GetTopLevel(int x, int z)
    {
        for (int level = Levels.Count - 1; level >= 0; level--)
        {
            if (state.GetTile(new Vector3Int(x, level, z)) != null) return level;
        }

        return -1;
    }


    // Resolves a point on the terrain's surface to the cell a player means by pointing at it.
    public bool TryGetTopCellAt(Vector3 surfacePoint, Vector3 rayDirection, out Vector3Int cell)
    {
        cell = default;

        Vector3 point = surfacePoint;
        int level = TopLevelAt(point);

        if (level < 0 || Mathf.Abs(point.y - GetLevelHeight(level)) > LEVEL_THRESHOLD)
        {
            point = surfacePoint + rayDirection * SURFACE_PROBE;
            level = TopLevelAt(point);
        }

        if (level < 0) return false;

        Vector3Int resolved = new Vector3Int(Mathf.FloorToInt(point.x), level, Mathf.FloorToInt(point.z));

        // the tilemap has the final say on the x/z mapping, because the state is keyed by tilemap cell
        if (!MatchesTilemap(point, resolved)) return false;

        cell = resolved;

        return true;
    }


    private int TopLevelAt(Vector3 point)
    {
        return GetTopLevel(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.z));
    }


    // MARK: remapping


    // The one and only way terrain changes
    public bool Remap(Vector3Int cell, TerrainTile tile)
    {
        if (tile == null) return false;
        if (registry == null) return false;

        // occupancy is fixed at build time, so remapping a hole means something upstream is wrong
        // rather than that the player is doing something new
        if (state.GetTile(cell) == null)
        {
            Debug.LogError($"TerrainManager: cell {cell} is not occupied, so it cannot be remapped", this);
            return false;
        }

        if (!registry.TryGetIdByEntry(tile, out string tileId)) return false;

        state.SetTile(cell, tile);
        world.terrain.SetRemap(cell.x, cell.z, ToLevelMillimetres(state.levelHeights[cell.y]), tileId);
        SetCellVisual(cell, tile);

        return true;
    }


    // The one place a tile becomes visible
    private void SetCellVisual(Vector3Int cell, TerrainTile tile)
    {
        if (visuals == null || tile == null) return;

        visuals.Patch(cell, ResolveCellSprite(cell, tile));
    }


    // The sprite this tile shows at this cell, exactly as SetCellVisual would patch it in. A tile
    // transition uses it to picture the tile a cell held before a remap
    public Sprite ResolveCellSprite(Vector3Int cell, TerrainTile tile)
    {
        if (tile == null) return null;

        // Resolved against the tilemap's own occupancy rather than this class's state, so the
        // sprite comes out exactly as the bake resolved it: same neighbour mask, same slot.
        //
        // VariantFor hashes the *tilemap* cell, which is why this is ToTilemapCell and not the
        // terrain cell - passing the terrain cell would pick a different random variant than the
        // bake did and the tile would change appearance on load.
        Vector3Int tilemapCell = ToTilemapCell(cell);
        Tilemap tilemap = cell.y >= 0 && cell.y < Levels.Count ? GetTilemap(cell.y) : null;

        return tilemap != null
            ? tile.SpriteFor(tilemapCell, tilemap)
            : tile.VariantFor(tilemapCell);
    }


    // Fills results with the texel rects (x0, y0, x1, y1, inside the cell's 32px slice) of the baked
    // top quads that draw this cell, i.e. the cell's real visible shape: a trimmed edge or a corner
    // partly covered by the level above is smaller than the full square. Empty when nothing is baked
    public bool TryGetTopTexelRects(Vector3Int cell, List<Vector4> results, out int pixelsPerTile)
    {
        results.Clear();
        pixelsPerTile = 0;

        TerrainQuadMap map = visuals != null ? visuals.TopQuadMap : null;
        if (map == null || map.quadTexelOffsets == null) return false;
        if (!map.TryGetQuads(cell, out int first, out int count)) return false;

        pixelsPerTile = map.pixelsPerTile;

        for (int quad = 0; quad < count; quad++)
        {
            results.Add(map.quadTexelOffsets[first + quad]);
        }

        return results.Count > 0 && pixelsPerTile > 0;
    }


    // MARK: preview


    // Patches a cell's visuals without touching the state or the save, for a placement drag
    public bool PreviewCell(Vector3Int cell, TerrainTile tile)
    {
        if (tile == null) return false;

        // same guard as Remap: previewing a hole would promise something that cannot happen
        if (state.GetTile(cell) == null) return false;

        SetCellVisual(cell, tile);

        return true;
    }


    // Restores whatever the cell actually holds
    public void ClearPreview(Vector3Int cell)
    {
        TerrainTile tile = state.GetTile(cell);
        if (tile == null) return;

        SetCellVisual(cell, tile);
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
            Debug.LogWarning($"TerrainManager: the authored map is now {state.width}x{state.depth} at " +
                $"({state.originX}, {state.originZ}) but this save was written against " +
                $"{terrain.width}x{terrain.depth} at ({terrain.originX}, {terrain.originZ}) - " +
                $"remaps that fall outside it will be skipped", this);
        }

        int savedLevels = terrain.levelHeights != null ? terrain.levelHeights.Length : 0;
        if (savedLevels != state.levelHeights.Length)
        {
            Debug.LogWarning($"TerrainManager: this save was written against {savedLevels} levels and " +
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
                Debug.LogWarning($"TerrainManager: save names a level at {remap.levelY}mm which no longer " +
                    $"exists - skipping cell ({remap.x}, {remap.z})", this);
                skipped++;
                continue;
            }

            Vector3Int cell = new Vector3Int(remap.x, level, remap.z);

            if (state.GetTile(cell) == null)
            {
                Debug.LogWarning($"TerrainManager: save remaps cell {cell}, which the authored map does " +
                    $"not occupy - skipping", this);
                skipped++;
                continue;
            }

            if (!registry.TryGetById(remap.tileId, out TerrainTile tile))
            {
                Debug.LogWarning($"TerrainManager: save names tile id '{remap.tileId}' which is not in " +
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
            Debug.LogWarning($"TerrainManager: applied {applied} remaps, skipped {skipped}", this);
        }
    }


    // MARK: visual hash


    // Hash of everything the bake resolves per cell: which cells hold a tile, and which sprite each one
    // renders. Occupancy on its own does not catch a stale bake - reassigning a tile's edge slots, or
    // re-importing the atlas, leaves every occupied cell exactly where it was while changing every
    // sprite on screen. The sprite's name and rect stand in for the sprite itself because, unlike an
    // instance id, they survive a domain reload, and a re-slice moves both.
    public static uint VisualHash(IReadOnlyList<Level> levels)
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
                    Vector3Int cell = new Vector3Int(x, z, 0);
                    bool occupied = tilemap.HasTile(cell);

                    hash = TerrainHash.Fold(hash, occupied ? 1 : 0);

                    if (occupied) hash = FoldSprite(hash, tilemap.GetSprite(cell));
                }
            }
        }

        return hash;
    }


    // What one cell resolves to, as far as the bake and a load can agree on it
    private static uint FoldSprite(uint hash, Sprite sprite)
    {
        if (sprite == null) return TerrainHash.Fold(hash, 0);

        hash = TerrainHash.Fold(hash, sprite.name);

        Rect rect = sprite.rect;
        hash = TerrainHash.Fold(hash, (int)rect.x);
        hash = TerrainHash.Fold(hash, (int)rect.y);
        hash = TerrainHash.Fold(hash, (int)rect.width);
        hash = TerrainHash.Fold(hash, (int)rect.height);

        return hash;
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
