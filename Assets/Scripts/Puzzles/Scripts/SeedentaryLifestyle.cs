using System.Collections.Generic;
using UnityEngine;


// Tier 0. Done once the player has placed as much dirt as the puzzle hands out, in any shape: the
// count is the puzzle's own ingredient list, so the number lives in one place. Credited to the dirt
// cell that finished it, so its ping hovers over that cell.
[CreateAssetMenu(
    fileName = "Seedentary Lifestyle",
    menuName = "Puzzles/Seedentary Lifestyle"
)]
public class SeedentaryLifestyle : ScriptedPuzzle
{
    [SerializeField] private GameItem seeds_1;
    [SerializeField] private GameItem seeds_2;


    [System.NonSerialized] private List<Vector3> seed1Positions;
    [System.NonSerialized] private List<Vector3> seed2Positions;
    [System.NonSerialized] private bool hasLastPlaced;
    [System.NonSerialized] private Vector3Int lastPlaced;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item != seeds_1 && item != seeds_2) return;

        lastPlaced = cell;
        hasLastPlaced = true;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit) {
        credit = PuzzleCredit.None;

        if (seeds_1 == null || seeds_2 == null) {
            Debug.LogError("PlantSeeds: either or both seeds_1 and seeds_2 are null, so this puzzle can never complete", this);
            return false;
        }

        if (!Has2x2Patch(context)) return false;

        credit = PuzzleCredit.Cell(CreditCell(context));
        return true;
    }


    private bool Has2x2Patch(WorldContext context) {
        if (seed1Positions == null) seed1Positions = new List<Vector3>();
        if (seed2Positions == null) seed2Positions = new List<Vector3>();
        seed1Positions.Clear();
        seed2Positions.Clear();

        context.CollectPositionsOf(seeds_1, seed1Positions);
        context.CollectPositionsOf(seeds_2, seed2Positions);

        if (seed1Positions.Count + seed2Positions.Count < 4) return false;

        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();

        foreach (Vector3 position in seed1Positions) {
            occupied.Add(context.Terrain.WorldToCell(position));
        }
        foreach (Vector3 position in seed2Positions) {
            occupied.Add(context.Terrain.WorldToCell(position));
        }

        foreach (Vector3Int cell in occupied) {
            if (Is2x2(occupied, cell, Vector3Int.right, Vector3Int.forward)) return true;
            if (Is2x2(occupied, cell, Vector3Int.left, Vector3Int.forward)) return true;
            if (Is2x2(occupied, cell, Vector3Int.right, Vector3Int.back)) return true;
            if (Is2x2(occupied, cell, Vector3Int.left, Vector3Int.back)) return true;
        }

        return false;
    }
    
    private bool Is2x2(HashSet<Vector3Int> occupied, Vector3Int origin, Vector3Int horizontal, Vector3Int vertical) {
        return
                occupied.Contains(origin) &&
                occupied.Contains(origin + horizontal) &&
                occupied.Contains(origin + vertical) &&
                occupied.Contains(origin + horizontal + vertical);
    }


    // the dirt just placed; caught up after a load or an unlock, any of the player's dirt cells
    private Vector3Int CreditCell(WorldContext context)
    {
        if (hasLastPlaced)
        {
            hasLastPlaced = false;
            return lastPlaced;
        }

        return default;
    }
}
