using System.Collections.Generic;
using UnityEngine;


// Tier 0. Done once the player has placed as much dirt as the puzzle hands out, in any shape: the
// count is the puzzle's own ingredient list, so the number lives in one place. Credited to the dirt
// cell that finished it, so its ping hovers over that cell.
[CreateAssetMenu(
    fileName = "Used Fertiliser",
    menuName = "Puzzles/Used Fertiliser"
)]
public class UsedFertiliser : ScriptedPuzzle
{
    [SerializeField] private GameItem fertiliser;


    // [System.NonSerialized] private List<Vector3Int> placedCells;
    // [System.NonSerialized] private bool hasLastPlaced;
    // [System.NonSerialized] private Vector3Int lastPlaced;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (InventoryManager.Instance.GetCount(fertiliser) >= 4) return;

        // lastPlaced = cell;
        // hasLastPlaced = true;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (fertiliser == null) {
            Debug.LogError("BreakGround: no dirt item, so this puzzle can never complete", this);
            return false;
        }

        if (InventoryManager.Instance.GetCount(fertiliser) >= 4) return false;

        // credit = PuzzleCredit.Cell(CreditCell(context));
        return true;
    }


    // private Vector3Int CreditCell(WorldContext context)
    // {
    //     if (hasLastPlaced)
    //     {
    //         hasLastPlaced = false;
    //         return lastPlaced;
    //     }

    //     return default;
    // }
}
