using UnityEngine;


// Greenery an animal spreads by itself: a worm turning wasteland into dirt, a deer turning dirt into
// grass. Each animal decides when and where; this only decides whether a cell may change and then
// changes it.
//
// A spread is an ordinary remap, so it counts as the player's work: it saves, it can solve a puzzle,
// and a checkpoint rolls it back. What makes it different from a placement is the feel: the tile
// transition and a sound, but no ripple, pop or shake, since nobody dropped anything.
//
// It only creeps out from ground that already has the new tile (the frontier), so it never appears
// out of nowhere.
public static class TerrainSpread
{
    private static readonly Vector3Int[] ORTHOGONAL =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1),
    };


    // MARK: checks


    // Whether cell may go from `from` to `to` right now: it's the visible top of its column, it holds
    // from, it touches to orthogonally on the same level, the risen sea isn't over it, and nothing
    // standing on it would be turned out by the new tile
    public static bool CanSpread(Vector3Int cell, TerrainTile from, TerrainTile to)
    {
        if (from == null || to == null || from == to) return false;

        TerrainManager terrain = TerrainManager.Instance;
        if (terrain == null) return false;

        // a covered cell can't be seen, so greening it would look like nothing happened
        if (cell.y < 0 || terrain.GetTopLevel(cell.x, cell.z) != cell.y) return false;
        if (terrain.GetTile(cell) != from) return false;

        if (!TouchesTile(terrain, cell, to)) return false;
        if (IsUnderSea(terrain, cell)) return false;

        // the same rule a drop follows, so a spread never strands a flower on the wrong ground
        PlacementController placement = PlacementController.Instance;
        if (placement == null) return false;

        return placement.ResidentsAllowTile(to, cell);
    }


    // the frontier: a neighbour on the same level, at the top of its own column, already holds tile
    private static bool TouchesTile(TerrainManager terrain, Vector3Int cell, TerrainTile tile)
    {
        for (int i = 0; i < ORTHOGONAL.Length; i++)
        {
            Vector3Int neighbour = cell + ORTHOGONAL[i];

            if (terrain.GetTopLevel(neighbour.x, neighbour.z) != cell.y) continue;
            if (terrain.GetTile(neighbour) == tile) return true;
        }

        return false;
    }


    // the live surface, like the deer: it counts from the moment the rise starts, before the flood
    // has remapped the level under it
    private static bool IsUnderSea(TerrainManager terrain, Vector3Int cell)
    {
        SeaLevel sea = SeaLevel.Instance;
        if (sea == null || !sea.IsRisen) return false;

        return terrain.GetLevelHeight(cell.y) < sea.SurfaceY;
    }


    // MARK: spreading


    // Changes cell and shows it: the tile transition, the sound, a save at the end of the frame, and
    // OnTerrainChanged once the transition has finished (straight away when none plays), so a rule
    // reacting to the new tile never fires while the ground is still growing over
    public static bool TrySpread(Vector3Int cell, TerrainTile from, TerrainTile to, SoundObject sound)
    {
        if (!CanSpread(cell, from, to)) return false;

        TerrainManager terrain = TerrainManager.Instance;

        if (!terrain.Remap(cell, to)) return false;

        PlacementController placement = PlacementController.Instance;
        TileTransition transition = placement != null ? placement.PlayTileTransition(cell, from) : null;

        if (sound != null && AudioManager.Instance != null) AudioManager.Instance.PlaySFX(sound, terrain.CellToWorld(cell));

        if (SaveDataManager.Instance != null) SaveDataManager.Instance.SaveSoon();

        if (transition == null)
        {
            RaiseChanged(cell, from, to);
            return true;
        }

        transition.Finished += () => RaiseChanged(cell, from, to);

        return true;
    }


    private static void RaiseChanged(Vector3Int cell, TerrainTile from, TerrainTile to)
    {
        WorldEvents events = WorldEvents.Instance;
        if (events != null) events.RaiseTerrainChanged(cell, from, to);
    }
}
