using System.Collections.Generic;
using UnityEngine;


// Spawns its animal in the center of a 3x3 patch the player just completed by placing a tile: the
// placed cell must be part of the patch, on the same level. Only the player's own placements count, so
// grass an animal spread (deer greening dirt) never spawns one, and can't fill the mob cap before the
// player's patches get a turn.
[CreateAssetMenu(
    fileName = "NinePatchTileRule",
    menuName = "Animals/Spawn Rules/Nine Patch Tile"
)]
public class NinePatchTileRule : AnimalSpawnRule
{

    private static readonly Vector3Int[] CENTRE_OFFSETS =
    {
        //top row
        new Vector3Int(-1, 0, 1),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1),
        //center row
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        //bottom row
        new Vector3Int(-1, 0, -1),
        new Vector3Int(0, 0, -1),
        new Vector3Int(1, 0, -1),
    };


    //the below function will return early if the tile just placed is not (the) requiredTileToBePlaced
    [SerializeField] private TerrainTile requiredTileToBePlaced;
    [SerializeField] private TerrainTile desiredNeighbouringTile;

    // Catching up looks over the whole world for any valid patch around a centre the player changed,
    // but only while this rule's puzzle is unlocked and not yet completed: at the unlock, and on a load
    // (a quit during the last watering's cover). Once the puzzle is done it's off, so a reload never
    // spawns from the patches already standing, deer-made ones included. With no puzzle, never.
    // Off, the rule only reacts to placements
    [SerializeField] private bool catchUpFromWorld = false;

    [System.NonSerialized] private List<Vector3Int> candidates;

    // cells the player placed whose patch hasn't been looked at yet: held while any cover is still
    // playing, then all looked at once the last one finishes
    [System.NonSerialized] private List<Vector3Int> placedCells;

    // true within a catch-up's TrySpawn, which looks over the whole world instead of placedCells
    [System.NonSerialized] private bool catchingUp;


    public override void OnLoaded(WorldContext context) {
        // a reset or reload's leftovers belong to a world that is gone
        if (placedCells != null) placedCells.Clear();

        CatchUp(context);
    }


    public override void OnPuzzleUnlocked(WorldContext context) {
        CatchUp(context);
    }


    // The world is remapped the moment a tile goes down, but its cover takes a moment to grow over the
    // old one. While any cover is still playing, a 3x3 can look complete before the player can see it,
    // so the placed cell is held and the rule waits: the last cover to finish looks at every held cell.
    // A growth is heard here too, but only a tile placement can finish a patch
    public override void OnItemShown(WorldContext context, GameItem itemOrTileJustPlaced, Vector3Int cell) {
        if (context == null) return;
        if (itemOrTileJustPlaced == null || itemOrTileJustPlaced.Tile == null) return;

        if (placedCells == null) placedCells = new List<Vector3Int>();
        placedCells.Add(cell);

        if (context.IsPresenting) return;

        TrySpawn(context);
        placedCells.Clear();
    }


    private void CatchUp(WorldContext context) {
        if (!catchUpFromWorld || context == null) return;
        if (Completes == null || context.IsCompleted(Completes)) return;

        catchingUp = true;

        try {
            TrySpawn(context);
        }
        finally {
            catchingUp = false;
        }
    }


    // a valid 3x3 one of the held cells is part of; catching up, any valid 3x3 around a player-changed
    // centre. One spawn per look: two patches finished by the same cover only get one
    protected override bool TryFindSpawn(WorldContext context, out Vector3 position) {
        position = default;
        if (requiredTileToBePlaced == null || desiredNeighbouringTile == null) return false;

        TerrainManager terrain = context.Terrain;
        if (terrain == null) return false;

        if (catchingUp) return TryFindAnywhere(terrain, out position);

        if (placedCells == null) return false;

        for (int i = 0; i < placedCells.Count; i++) {
            for (int j = 0; j < CENTRE_OFFSETS.Length; j++) {
                Vector3Int center = placedCells[i] + CENTRE_OFFSETS[j];
                if (!IsValidCentre(terrain, center)) continue;

                position = terrain.CellToWorld(center);
                return true;
            }
        }

        return false;
    }


    private bool TryFindAnywhere(TerrainManager terrain, out Vector3 position) {
        position = default;

        if (candidates == null) candidates = new List<Vector3Int>();

        terrain.CollectPlayerCellsOf(requiredTileToBePlaced, candidates);

        for (int i = 0; i < candidates.Count; i++) {
            if (!IsValidCentre(terrain, candidates[i])) continue;

            position = terrain.CellToWorld(candidates[i]);
            return true;
        }

        return false;
    }


    private bool IsValidCentre(TerrainManager terrain, Vector3Int center) {
        //check if our new center is itself valid
        if (terrain.GetTile(center) != requiredTileToBePlaced) return false;

        //try to prove it false by checking the surrounding 3x3 for an incorrect tile
        for (int j = 0; j < CENTRE_OFFSETS.Length; j++) {
            //every tile needs to pass the check, so if it fails once we can immediately stop
            if (terrain.GetTile(center + CENTRE_OFFSETS[j]) != desiredNeighbouringTile) return false;
        }

        return true;
    }
}
