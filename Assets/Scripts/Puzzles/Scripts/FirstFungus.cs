using System.Collections.Generic;
using UnityEngine;


// Tier 7. Done once a mushroom grows in a tree's shade: within shadeRadius cells of any of trees,
// diagonals included (1 is the eight cells around it). Either can come first, so a sapling growing
// into a tree next to a mushroom finishes it too.
//
// Credited to the mushroom, so its ping hovers over it, even when the tree is what arrives last.
[CreateAssetMenu(
    fileName = "First Fungus",
    menuName = "Puzzles/First Fungus"
)]
public class FirstFungus : ScriptedPuzzle
{
    // NB: the asset's serialized values win over these defaults
    [SerializeField] private GameItem mushroom;
    [SerializeField] private List<GameItem> trees = new List<GameItem>();
    [SerializeField] [Min(1)] private int shadeRadius = 1;


    [System.NonSerialized] private List<Vector3> positions;
    [System.NonSerialized] private List<Vector3Int> treeCells;
    [System.NonSerialized] private EntityId lastPlacedEntity;
    [System.NonSerialized] private Vector3Int lastPlacedCell;
    [System.NonSerialized] private bool hasLastPlacedEntity;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item == null) return;

        if (item == mushroom)
        {
            lastPlacedEntity = PlacedEntityAt(cell, item);
            lastPlacedCell = cell;
            hasLastPlacedEntity = lastPlacedEntity != null;
        }
        else if (!trees.Contains(item)) return;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        TerrainManager terrain = context.Terrain;
        if (terrain == null || mushroom == null) return false;

        if (!CollectTreeCells(context, terrain)) return false;

        positions.Clear();
        context.CollectPositionsOf(mushroom, positions);

        EntityId found = null;
        bool anyShaded = false;

        for (int p = 0; p < positions.Count; p++)
        {
            Vector3Int cell = terrain.WorldToCell(positions[p]);
            if (!IsShaded(cell)) continue;

            anyShaded = true;
            found = PlacedEntityAt(cell, mushroom);
            if (found != null) break;
        }

        if (!anyShaded) return false;

        credit = Credit(found);
        return true;
    }


    // MARK: shade


    // every placed tree's cell, false when there are none
    private bool CollectTreeCells(WorldContext context, TerrainManager terrain)
    {
        if (positions == null) positions = new List<Vector3>();
        if (treeCells == null) treeCells = new List<Vector3Int>();

        treeCells.Clear();

        for (int i = 0; i < trees.Count; i++)
        {
            if (trees[i] == null) continue;

            positions.Clear();
            context.CollectPositionsOf(trees[i], positions);

            for (int p = 0; p < positions.Count; p++) treeCells.Add(terrain.WorldToCell(positions[p]));
        }

        return treeCells.Count > 0;
    }


    private bool IsShaded(Vector3Int cell)
    {
        for (int i = 0; i < treeCells.Count; i++)
        {
            Vector3Int tree = treeCells[i];
            if (Mathf.Abs(cell.x - tree.x) <= shadeRadius && Mathf.Abs(cell.z - tree.z) <= shadeRadius) return true;
        }

        return false;
    }


    // MARK: credit


    // the mushroom just placed if it's in the shade, else whichever shaded one was found
    private PuzzleCredit Credit(EntityId found)
    {
        bool useLast = hasLastPlacedEntity && IsLive(lastPlacedEntity) && IsShaded(lastPlacedCell);
        EntityId entity = useLast ? lastPlacedEntity : found;

        hasLastPlacedEntity = false;
        lastPlacedEntity = null;

        return entity != null ? PuzzleCredit.Entity(entity) : PuzzleCredit.None;
    }


    // MARK: validation


    private void OnValidate()
    {
        if (mushroom == null)
        {
            Debug.LogError($"FirstFungus: '{name}' has no mushroom, so it can never complete", this);
        }

        if (trees.Count == 0 || trees.Contains(null))
        {
            Debug.LogError($"FirstFungus: '{name}' has a missing tree, so a mushroom beside it would never count", this);
        }
    }
}
