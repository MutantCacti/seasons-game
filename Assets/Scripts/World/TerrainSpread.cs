using UnityEngine;


// Greenery an animal spreads by itself: a worm turning wasteland into dirt, a deer turning dirt into
// grass. Each animal decides when and where; this only decides whether a cell may change and then
// changes it.
//
// A spread is an ordinary remap, so it counts as the player's work: it saves, it can solve a puzzle,
// and a checkpoint rolls it back. What makes it different from a placement is the feel: the tile
// transition and a sound, but no ripple, pop or shake, since nobody dropped anything.
//
// It only creeps out from ground next to it (the frontier), so it never appears out of nowhere. The
// frontier is the new tile itself unless the animal names others: a worm lives on grass and avoids
// wasteland, so its dirt creeps out from grass as well as dirt.
public static class TerrainSpread
{
    private static readonly Vector3Int[] ORTHOGONAL =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1),
    };


    private static bool warnedAboutClock;


    // MARK: budget


    // An animal's spread credit after dt more seconds: tilesPerDay per whole day and night, read live
    // since the day length is still being tuned, and capped so a long stall (nowhere to spread) can't
    // come out as a burst. Spend one per conversion. Scaled dt, so a paused game earns nothing
    public static float Accrue(float credit, float tilesPerDay, float cap, float dt)
    {
        WorldStateManager clock = WorldStateManager.Instance;

        if (clock == null)
        {
            if (!warnedAboutClock)
            {
                warnedAboutClock = true;
                Debug.LogError("TerrainSpread: no WorldStateManager, so there is no day length and animals never spread");
            }

            return credit;
        }

        float cycle = clock.DayDuration + clock.NightDuration;
        if (cycle <= 0.0f) return credit;

        return Mathf.Min(credit + tilesPerDay * dt / cycle, cap);
    }


    // MARK: checks


    // Whether cell may go from `from` to `to` right now: it's the visible top of its column, it holds
    // from, it touches one of nextTo orthogonally on the same level (just `to` when nextTo is empty),
    // the risen sea isn't over it, and nothing standing on it would be turned out by the new tile
    public static bool CanSpread(Vector3Int cell, TerrainTile from, TerrainTile to, TerrainTile[] nextTo)
    {
        if (from == null || to == null || from == to) return false;

        TerrainManager terrain = TerrainManager.Instance;
        if (terrain == null) return false;

        // a covered cell can't be seen, so greening it would look like nothing happened
        if (cell.y < 0 || terrain.GetTopLevel(cell.x, cell.z) != cell.y) return false;
        if (terrain.GetTile(cell) != from) return false;

        if (!TouchesFrontier(terrain, cell, to, nextTo)) return false;
        if (IsUnderSea(terrain, cell)) return false;

        // the same rule a drop follows, so a spread never strands a flower on the wrong ground
        PlacementController placement = PlacementController.Instance;
        if (placement == null) return false;

        return placement.ResidentsAllowTile(to, cell);
    }


    // the frontier: a neighbour on the same level, at the top of its own column, holds one of nextTo,
    // or `to` itself when nextTo is empty
    private static bool TouchesFrontier(TerrainManager terrain, Vector3Int cell, TerrainTile to, TerrainTile[] nextTo)
    {
        for (int i = 0; i < ORTHOGONAL.Length; i++)
        {
            Vector3Int neighbour = cell + ORTHOGONAL[i];

            if (terrain.GetTopLevel(neighbour.x, neighbour.z) != cell.y) continue;
            if (IsFrontier(terrain.GetTile(neighbour), to, nextTo)) return true;
        }

        return false;
    }


    private static bool IsFrontier(TerrainTile tile, TerrainTile to, TerrainTile[] nextTo)
    {
        if (tile == null) return false;
        if (nextTo == null || nextTo.Length == 0) return tile == to;

        for (int i = 0; i < nextTo.Length; i++)
        {
            if (nextTo[i] == tile) return true;
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
    // OnTerrainMutated once the transition has finished (straight away when none plays), so a rule
    // reacting to the new tile never fires while the ground is still growing over
    public static bool TrySpread(Vector3Int cell, TerrainTile from, TerrainTile to, TerrainTile[] nextTo, SoundObject sound)
    {
        if (!CanSpread(cell, from, to, nextTo)) return false;

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
