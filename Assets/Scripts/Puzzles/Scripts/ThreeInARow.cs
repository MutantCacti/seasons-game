using System.Collections.Generic;
using UnityEngine;


// Tier 0. Done once the player has placed as much dirt as the puzzle hands out, in any shape: the
// count is the puzzle's own ingredient list, so the number lives in one place. Credited to the dirt
// cell that finished it, so its ping hovers over that cell.
[CreateAssetMenu(
    fileName = "Three In A Row",
    menuName = "Puzzles/Three In A Row"
)]
public class ThreeInARow : ScriptedPuzzle
{
    [SerializeField] private GameItem fernSummer;

    [System.NonSerialized] private List<Vector3> placedItems;
    [System.NonSerialized] private EntityId lastPlacedEntity;
    [System.NonSerialized] private bool hasLastPlacedEntity;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item != fernSummer) return;

        lastPlacedEntity = PlacedEntityAt(cell, fernSummer);
        hasLastPlacedEntity = lastPlacedEntity != null;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit) {
        credit = PuzzleCredit.None;

        if (fernSummer == null) {
            Debug.LogError("ThreeInARow: fernSummer GameItem is null, so this puzzle can never complete", this);
            return false;
        }

        if (!HasLineOf3(context)) return false;

        credit = Credit();
        return true;
    }


    private bool HasLineOf3(WorldContext context) {
        if (placedItems == null) placedItems = new List<Vector3>();
        placedItems.Clear();

        //get positions of all types of seeds we'd like to count for this puzzle
        context.CollectPositionsOf(fernSummer, placedItems);

        //this puzzle is for a 2x2 patch, so if there's less than 4 seeds total, we know it's not possible
        if (placedItems.Count < 3) return false;

        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();

        foreach (Vector3 position in placedItems) {
            occupied.Add(context.Terrain.WorldToCell(position));
        }

        foreach (Vector3Int cell in occupied) {
            //horizontal
            if (Is3InARow(occupied, cell, Vector3Int.right)) return true;
            //vertical
            if (Is3InARow(occupied, cell, Vector3Int.forward)) return true;
        }

        return false;
    }
    
    private bool Is3InARow(HashSet<Vector3Int> occupied, Vector3Int origin, Vector3Int direction) {
        //check that our list of found seeds has the cells around our current patch origin cell
        return occupied.Contains(origin) &&
                occupied.Contains(origin + direction) &&
                occupied.Contains(origin + direction * 2);
    }


    // the fern just placed, so the ping sits on it and follows it; caught up after a load or an unlock,
    // when there is no just placed one, any fern the player has
    private PuzzleCredit Credit()
    {
        EntityId entity = hasLastPlacedEntity && IsLive(lastPlacedEntity) ? lastPlacedEntity : AnyPlacedEntity(fernSummer);

        hasLastPlacedEntity = false;
        lastPlacedEntity = null;

        return entity != null ? PuzzleCredit.Entity(entity) : PuzzleCredit.None;
    }
}
