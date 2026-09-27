using System.Collections.Generic;
using UnityEngine;


// Spawns its animal in the center of a 3x3 patch whose center must be a neighbour of the tile just
// placed, on the same level.
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

    // Catching up on a load or an unlock only looks for centres the player changed, so the world's
    // own authored patches can't spawn the animal the moment its puzzle unlocks. The neighbours can
    // still be authored. Off, the rule only reacts to placements, as it always has
    [SerializeField] private bool catchUpFromWorld = false;

    [System.NonSerialized] private List<Vector3Int> candidates;


    public override void OnLoaded(WorldContext context) {
        if (catchUpFromWorld) base.OnLoaded(context);
    }


    public override void OnPuzzleUnlocked(WorldContext context) {
        if (catchUpFromWorld) base.OnPuzzleUnlocked(context);
    }


    // The world is remapped the moment a tile goes down, but its cover takes a moment to grow over the
    // old one. While any cover is still playing, a 3x3 can look complete before the player can see it,
    // so the rule waits: the last cover to finish is the one that asks, and it looks over the whole
    // settled world rather than only the cell it was told about
    public override void OnItemShown(WorldContext context, GameItem itemOrTileJustPlaced, Vector3Int cell) {
        if (context == null) return;
        if (context.IsPresenting) return;

        TrySpawn(context);
    }


    // an animal greened a cell. Same whole-world look as a placement, so animal-made grass can finish a
    // 3x3 too. A placement's cover still playing means its own OnItemShown will ask once it settles
    public override void OnTerrainChanged(WorldContext context, Vector3Int cell, TerrainTile from, TerrainTile to) {
        if (context == null) return;
        if (to != requiredTileToBePlaced && to != desiredNeighbouringTile) return;
        if (context.IsPresenting) return;

        TrySpawn(context);
    }


    // any valid 3x3 around a player-changed centre, for catching up on a load or an unlock
    protected override bool TryFindSpawn(WorldContext context, out Vector3 position) {
        position = default;
        if (requiredTileToBePlaced == null || desiredNeighbouringTile == null) return false;

        TerrainManager terrain = context.Terrain;
        if (terrain == null) return false;

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
