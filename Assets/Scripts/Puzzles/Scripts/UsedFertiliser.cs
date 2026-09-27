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


    [System.NonSerialized] private EntityId lastPlacedEntity;
    [System.NonSerialized] private bool hasLastPlacedEntity;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (InventoryManager.Instance.GetCount(fertiliser) >= 4) return;

        // the placement hears the grown result, not the fertiliser that made it, so this takes whatever
        // non-fixture entity now stands on the cell
        lastPlacedEntity = PlacedEntityAt(cell, null);
        hasLastPlacedEntity = lastPlacedEntity != null;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (fertiliser == null) {
            Debug.LogError("UsedFertiliser: no fertiliser item, so this puzzle can never complete", this);
            return false;
        }

        if (InventoryManager.Instance.GetCount(fertiliser) >= 4) return false;

        credit = Credit();
        return true;
    }


    // the entity the fertilising made. Only the just placed one will do: the world's entities say
    // nothing about which of them a fertiliser grew, so a load or an unlock catches up with None rather
    // than pointing the ping at something unrelated
    private PuzzleCredit Credit()
    {
        EntityId entity = hasLastPlacedEntity && IsLive(lastPlacedEntity) ? lastPlacedEntity : null;

        hasLastPlacedEntity = false;
        lastPlacedEntity = null;

        return entity != null ? PuzzleCredit.Entity(entity) : PuzzleCredit.None;
    }
}
