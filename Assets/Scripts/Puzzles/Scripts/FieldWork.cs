using System.Collections.Generic;
using UnityEngine;


// Tier 7. Done once requiredGrass grass, short or tall in any mix, stands within radius cells of a
// beehive: any placed thing carrying a Beehive (a hive up a tree), found the way A Bee Cs finds them.
// Distance is in whole cells across x and z, so the radius is a square around the hive.
//
// Credited to the grass that finished it, so its ping hovers over it. When the hive is what arrives
// last, or it's caught up after a load or an unlock, any grass by a hive gets it.
[CreateAssetMenu(
    fileName = "Field Work",
    menuName = "Puzzles/Field Work"
)]
public class FieldWork : ScriptedPuzzle
{
    // NB: the asset's serialized values win over these defaults
    [SerializeField] private List<GameItem> grasses = new List<GameItem>();
    [SerializeField] [Min(1)] private int requiredGrass = 4;
    [SerializeField] [Min(0)] private int radius = 3;


    [System.NonSerialized] private List<Vector3> positions;
    [System.NonSerialized] private List<Vector3Int> hiveCells;
    [System.NonSerialized] private EntityId lastPlacedEntity;
    [System.NonSerialized] private Vector3Int lastPlacedCell;
    [System.NonSerialized] private bool hasLastPlacedEntity;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item == null) return;

        if (grasses.Contains(item))
        {
            lastPlacedEntity = PlacedEntityAt(cell, item);
            lastPlacedCell = cell;
            hasLastPlacedEntity = lastPlacedEntity != null;
        }
        // a hive put in a tree after the grass went down can finish it too
        else if (!CarriesHive(item)) return;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        TerrainManager terrain = context.Terrain;
        if (terrain == null || grasses.Count == 0) return false;

        if (!CollectHiveCells(terrain)) return false;

        if (positions == null) positions = new List<Vector3>();

        int nearHive = 0;
        EntityId found = null;

        for (int i = 0; i < grasses.Count; i++)
        {
            GameItem grass = grasses[i];
            if (grass == null) continue;

            positions.Clear();
            context.CollectPositionsOf(grass, positions);

            for (int p = 0; p < positions.Count; p++)
            {
                Vector3Int cell = terrain.WorldToCell(positions[p]);
                if (!IsNearHive(cell)) continue;

                nearHive++;
                if (found == null) found = PlacedEntityAt(cell, grass);
            }
        }

        if (nearHive < requiredGrass) return false;

        credit = Credit(found);
        return true;
    }


    // MARK: hives


    // every live hive's cell, false when there are none
    private bool CollectHiveCells(TerrainManager terrain)
    {
        if (hiveCells == null) hiveCells = new List<Vector3Int>();
        hiveCells.Clear();

        for (int i = 0; i < Beehive.All.Count; i++)
        {
            Beehive hive = Beehive.All[i];
            if (hive == null || hive.Entity == null) continue;

            hiveCells.Add(terrain.WorldToCell(hive.Entity.transform.position));
        }

        return hiveCells.Count > 0;
    }


    private bool IsNearHive(Vector3Int cell)
    {
        for (int i = 0; i < hiveCells.Count; i++)
        {
            Vector3Int hive = hiveCells[i];
            if (Mathf.Abs(cell.x - hive.x) <= radius && Mathf.Abs(cell.z - hive.z) <= radius) return true;
        }

        return false;
    }


    // a hive tree is a replace of a plain one, heard as a placement of the item it became
    private static bool CarriesHive(GameItem item)
    {
        return item.Prefab != null && item.Prefab.GetComponentInChildren<Beehive>(true) != null;
    }


    // MARK: credit


    // the grass just placed if it's by a hive, else whichever grass by a hive was found first
    private PuzzleCredit Credit(EntityId found)
    {
        bool useLast = hasLastPlacedEntity && IsLive(lastPlacedEntity) && IsNearHive(lastPlacedCell);
        EntityId entity = useLast ? lastPlacedEntity : found;

        hasLastPlacedEntity = false;
        lastPlacedEntity = null;

        return entity != null ? PuzzleCredit.Entity(entity) : PuzzleCredit.None;
    }


    // MARK: validation


    private void OnValidate()
    {
        if (grasses.Count == 0 || grasses.Contains(null))
        {
            Debug.LogError($"FieldWork: '{name}' has a missing grass, so it can't count every grass by a hive", this);
        }
    }
}
