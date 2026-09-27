using System.Collections.Generic;
using UnityEngine;


// Tier 7, the orchard. Done once a fruit bush stands within radius cells of any flower; one kind is
// enough, since both were already needed to bring the bees. Distance is in whole cells across x and
// z, so the radius is a square around the bush.
//
// Credited to the bush, so its ping hovers over it, even when a flower (or a sprout growing into
// one) is what arrives last.
[CreateAssetMenu(
    fileName = "A Fruity Bloom",
    menuName = "Puzzles/A Fruity Bloom"
)]
public class AFruityBloom : ScriptedPuzzle
{
    // NB: the asset's serialized values win over these defaults
    [SerializeField] private GameItem fruitBush;
    [SerializeField] private List<GameItem> flowers = new List<GameItem>();
    [SerializeField] [Min(0)] private int radius = 2;


    [System.NonSerialized] private List<Vector3> positions;
    [System.NonSerialized] private List<Vector3Int> flowerCells;
    [System.NonSerialized] private EntityId lastPlacedEntity;
    [System.NonSerialized] private Vector3Int lastPlacedCell;
    [System.NonSerialized] private bool hasLastPlacedEntity;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item == null) return;

        if (item == fruitBush)
        {
            lastPlacedEntity = PlacedEntityAt(cell, item);
            lastPlacedCell = cell;
            hasLastPlacedEntity = lastPlacedEntity != null;
        }
        else if (!flowers.Contains(item)) return;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        TerrainManager terrain = context.Terrain;
        if (terrain == null || fruitBush == null) return false;

        if (!CollectFlowerCells(context, terrain)) return false;

        positions.Clear();
        context.CollectPositionsOf(fruitBush, positions);

        EntityId found = null;
        bool anyBloom = false;

        for (int p = 0; p < positions.Count; p++)
        {
            Vector3Int cell = terrain.WorldToCell(positions[p]);
            if (!IsNearFlower(cell)) continue;

            anyBloom = true;
            found = PlacedEntityAt(cell, fruitBush);
            if (found != null) break;
        }

        if (!anyBloom) return false;

        credit = Credit(found);
        return true;
    }


    // MARK: flowers


    // every placed flower's cell, false when there are none
    private bool CollectFlowerCells(WorldContext context, TerrainManager terrain)
    {
        if (positions == null) positions = new List<Vector3>();
        if (flowerCells == null) flowerCells = new List<Vector3Int>();

        flowerCells.Clear();

        for (int i = 0; i < flowers.Count; i++)
        {
            if (flowers[i] == null) continue;

            positions.Clear();
            context.CollectPositionsOf(flowers[i], positions);

            for (int p = 0; p < positions.Count; p++) flowerCells.Add(terrain.WorldToCell(positions[p]));
        }

        return flowerCells.Count > 0;
    }


    private bool IsNearFlower(Vector3Int cell)
    {
        for (int i = 0; i < flowerCells.Count; i++)
        {
            Vector3Int flower = flowerCells[i];
            if (Mathf.Abs(cell.x - flower.x) <= radius && Mathf.Abs(cell.z - flower.z) <= radius) return true;
        }

        return false;
    }


    // MARK: credit


    // the bush just placed if it's the one in bloom, else whichever blooming bush was found
    private PuzzleCredit Credit(EntityId found)
    {
        bool useLast = hasLastPlacedEntity && IsLive(lastPlacedEntity) && IsNearFlower(lastPlacedCell);
        EntityId entity = useLast ? lastPlacedEntity : found;

        hasLastPlacedEntity = false;
        lastPlacedEntity = null;

        return entity != null ? PuzzleCredit.Entity(entity) : PuzzleCredit.None;
    }


    // MARK: validation


    private void OnValidate()
    {
        if (fruitBush == null)
        {
            Debug.LogError($"AFruityBloom: '{name}' has no fruit bush, so it can never complete", this);
        }

        if (flowers.Count == 0 || flowers.Contains(null))
        {
            Debug.LogError($"AFruityBloom: '{name}' has a missing flower, so a bush by it would never count", this);
        }
    }
}
