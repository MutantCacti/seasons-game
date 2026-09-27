using System.Collections.Generic;
using UnityEngine;


// Tier 1. Done once the player has placed as much water as the puzzle hands out, in any shape: the
// count is the puzzle's own ingredient list, so the number lives in one place. Credited to the water
// cell that finished it, so its ping hovers over that cell.
//
// Water only goes on dirt (see Water's placed-on tags) and the authored map holds no dirt at all, so
// the one water this hands out can only land on the one dirt cell Break Ground left behind. That is
// what makes "place one water" the same as "put the rain on the dirt": placing remaps the cell in
// place, so once the water is down the cell holds water and the dirt it fell on cannot be read back
// afterwards - the placed water can.
[CreateAssetMenu(
    fileName = "I Am The Rain",
    menuName = "Puzzles/I Am The Rain"
)]
public class IAmTheRain : ScriptedPuzzle
{
    [SerializeField] private GameItem water;


    [System.NonSerialized] private List<Vector3Int> placedCells;
    [System.NonSerialized] private bool hasLastPlaced;
    [System.NonSerialized] private Vector3Int lastPlaced;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item != water) return;

        lastPlaced = cell;
        hasLastPlaced = true;

        TryComplete(context);
    }


    // water's tile is grass, so a deer's grass counts as rain too
    public override void OnTerrainMutated(WorldContext context, Vector3Int cell, TerrainTile from, TerrainTile to)
    {
        if (water == null || to != water.Tile) return;

        lastPlaced = cell;
        hasLastPlaced = true;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (water == null)
        {
            Debug.LogError("IAmTheRain: no water item, so this puzzle can never complete", this);
            return false;
        }

        if (context.CountPlaced(water) < RequiredCount(water)) return false;

        credit = PuzzleCredit.Cell(CreditCell(context));
        return true;
    }


    // the water just placed; caught up after a load or an unlock, any of the player's water cells
    private Vector3Int CreditCell(WorldContext context)
    {
        if (hasLastPlaced)
        {
            hasLastPlaced = false;
            return lastPlaced;
        }

        if (placedCells == null) placedCells = new List<Vector3Int>();

        context.CollectPlacedCells(water, placedCells);

        return placedCells.Count > 0 ? placedCells[placedCells.Count - 1] : default;
    }
}
