using System.Collections.Generic;
using UnityEngine;


// Tier 0. Done once the player has placed as much dirt as the puzzle hands out, in any shape: the
// count is the puzzle's own ingredient list, so the number lives in one place. Credited to the dirt
// cell that finished it, so its ping hovers over that cell.
[CreateAssetMenu(
    fileName = "Break Ground",
    menuName = "Puzzles/Break Ground"
)]
public class BreakGround : ScriptedPuzzle
{
    [SerializeField] private GameItem dirt;


    [System.NonSerialized] private List<Vector3Int> placedCells;
    [System.NonSerialized] private bool hasLastPlaced;
    [System.NonSerialized] private Vector3Int lastPlaced;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item != dirt) return;

        lastPlaced = cell;
        hasLastPlaced = true;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (dirt == null)
        {
            Debug.LogError("BreakGround: no dirt item, so this puzzle can never complete", this);
            return false;
        }

        if (context.CountPlaced(dirt) < RequiredCount(dirt)) return false;

        credit = PuzzleCredit.Cell(CreditCell(context));
        return true;
    }


    // the dirt just placed; caught up after a load or an unlock, any of the player's dirt cells
    private Vector3Int CreditCell(WorldContext context)
    {
        if (hasLastPlaced)
        {
            hasLastPlaced = false;
            return lastPlaced;
        }

        if (placedCells == null) placedCells = new List<Vector3Int>();

        context.CollectPlacedCells(dirt, placedCells);

        return placedCells.Count > 0 ? placedCells[placedCells.Count - 1] : default;
    }
}
