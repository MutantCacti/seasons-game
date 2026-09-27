using System.Collections.Generic;
using UnityEngine;


// A reusable "place them all": done once the player has placed as many of item as this puzzle hands
// out, in any shape and any order. That is BreakGround's rule, but for any item, entity or tile.
//
// The count is the puzzle's own ingredient list, so the number lives in one place; requiredOverride is
// only for a puzzle that wants a different number (or that needs no ingredients of its own).
//
// Credited to the thing that finished it: the entity a prefab item just placed, so the ping follows it
// (a lilypad riding the sea), or the cell a tile item changed, as BreakGround does. Caught up after a
// load or an unlock there is no "just placed", so any of the player's does.
[CreateAssetMenu(
    fileName = "Place All",
    menuName = "Puzzles/Place All"
)]
public class PlaceAll : ScriptedPuzzle
{
    [SerializeField] private GameItem item;

    [Tooltip("0 uses this puzzle's own ingredients, however many of item they hand out; >0 needs this many instead")]
    [SerializeField] [Min(0)] private int requiredOverride;


    [System.NonSerialized] private List<Vector3Int> placedCells;
    [System.NonSerialized] private bool hasLastPlaced;
    [System.NonSerialized] private Vector3Int lastPlaced;
    [System.NonSerialized] private EntityId lastPlacedEntity;
    [System.NonSerialized] private bool hasLastPlacedEntity;
    [System.NonSerialized] private bool loggedUnusable;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (this.item == null || item != this.item) return;

        // a tile item is counted by its cells, so it is credited to one; a prefab item is an entity,
        // and the ping can then follow it around (a lilypad riding the sea)
        if (this.item.Tile != null)
        {
            lastPlaced = cell;
            hasLastPlaced = true;
        }
        else
        {
            lastPlacedEntity = PlacedEntityAt(cell, item);
            hasLastPlacedEntity = lastPlacedEntity != null;
        }

        TryComplete(context);
    }


    // a worm's greenery is a remap like the player's, so it counts too
    public override void OnTerrainChanged(WorldContext context, Vector3Int cell, TerrainTile from, TerrainTile to)
    {
        if (item == null || item.Tile == null) return;
        if (to != item.Tile) return;

        lastPlaced = cell;
        hasLastPlaced = true;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (item == null)
        {
            LogUnusableOnce($"PlaceAll: '{name}' has no item, so it can never complete");
            return false;
        }

        int required = requiredOverride > 0 ? requiredOverride : RequiredCount(item);

        // no ingredient of its own and no override to stand in for one: nothing could ever be enough
        if (required <= 0)
        {
            LogUnusableOnce($"PlaceAll: '{name}' needs 0 of '{item.name}' (no ingredient for it and no required override), " +
                $"so it can never complete");
            return false;
        }

        if (context.CountPlaced(item) < required) return false;

        credit = Credit(context);
        return true;
    }


    // MARK: credit


    // the just-placed thing, or any one of the player's after a load or an unlock
    private PuzzleCredit Credit(WorldContext context)
    {
        if (item.Tile != null) return PuzzleCredit.Cell(CreditCell(context));

        // a checkpoint reset despawns what was just placed while this asset, which outlives the scene,
        // still remembers it: a dead reference would complete with nothing for the ping to follow
        EntityId entity = hasLastPlacedEntity && IsLive(lastPlacedEntity) ? lastPlacedEntity : AnyPlacedEntity(item);

        hasLastPlacedEntity = false;
        lastPlacedEntity = null;

        // None is legal: the completion still counts, it just has nothing to point at
        return entity != null ? PuzzleCredit.Entity(entity) : PuzzleCredit.None;
    }


    // the tile the player just changed; caught up after a load or an unlock, any of the player's cells
    private Vector3Int CreditCell(WorldContext context)
    {
        if (hasLastPlaced)
        {
            hasLastPlaced = false;
            return lastPlaced;
        }

        if (placedCells == null) placedCells = new List<Vector3Int>();

        context.CollectPlacedCells(item, placedCells);

        return placedCells.Count > 0 ? placedCells[placedCells.Count - 1] : default;
    }


    // once, not on every event: an unfinished asset would otherwise fill the console on every placement
    private void LogUnusableOnce(string message)
    {
        if (loggedUnusable) return;

        loggedUnusable = true;
        Debug.LogError(message, this);
    }


    // MARK: validation


    private void OnValidate()
    {
        if (item == null)
        {
            Debug.LogError($"PlaceAll: '{name}' has no item, so it can never complete", this);
            return;
        }

        if (requiredOverride <= 0) return;

        int given = RequiredCount(item);

        // legal, an outside source could still hand them over, but it is almost always a typo
        if (requiredOverride > given)
        {
            Debug.LogWarning($"PlaceAll: '{name}' needs {requiredOverride} of '{item.name}', but its ingredients only hand " +
                $"out {given}, so it could only complete with more from somewhere else", this);
        }
    }
}
