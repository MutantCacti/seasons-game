using System.Collections.Generic;
using UnityEngine;


// Tier 7, the shore. Done once requiredRocks rocks, any mix of them, stand on the shore: a cell that
// isn't under water itself but has water on one of its four sides. Water is the seafloor tile (what
// the risen sea floods every submerged level into) or a column the risen sea is over right now, which
// also covers the moment mid-rise before a level has been remapped, as TerrainSpread tests it.
//
// Credited to the rock that finished it, so its ping hovers over it; caught up after a load or an
// unlock, or finished by the water rather than a rock, any rock on the shore.
[CreateAssetMenu(
    fileName = "Rocky Shores",
    menuName = "Puzzles/Rocky Shores"
)]
public class RockyShores : ScriptedPuzzle
{
    private static readonly Vector3Int[] ORTHOGONAL =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1),
    };


    // NB: the asset's serialized values win over these defaults
    [SerializeField] private List<GameItem> rocks = new List<GameItem>();
    [Tooltip("the tile the risen sea floods the seafloor into (Riverbed)")]
    [SerializeField] private TerrainTile seafloor;
    [SerializeField] [Min(1)] private int requiredRocks = 4;


    [System.NonSerialized] private List<Vector3> positions;
    [System.NonSerialized] private EntityId lastPlacedEntity;
    [System.NonSerialized] private Vector3Int lastPlacedCell;
    [System.NonSerialized] private bool hasLastPlacedEntity;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item == null || !rocks.Contains(item)) return;

        lastPlacedEntity = PlacedEntityAt(cell, item);
        lastPlacedCell = cell;
        hasLastPlacedEntity = lastPlacedEntity != null;

        TryComplete(context);
    }


    // a neighbour going under can turn a rock that was inland into one on the shore
    public override void OnTerrainMutated(WorldContext context, Vector3Int cell, TerrainTile from, TerrainTile to)
    {
        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        TerrainManager terrain = context.Terrain;
        if (terrain == null || rocks.Count == 0) return false;

        if (positions == null) positions = new List<Vector3>();

        int onShore = 0;
        EntityId found = null;

        for (int i = 0; i < rocks.Count; i++)
        {
            GameItem rock = rocks[i];
            if (rock == null) continue;

            positions.Clear();
            context.CollectPositionsOf(rock, positions);

            for (int p = 0; p < positions.Count; p++)
            {
                Vector3Int cell = terrain.WorldToCell(positions[p]);
                if (!IsShore(terrain, cell)) continue;

                onShore++;
                if (found == null) found = PlacedEntityAt(cell, rock);
            }
        }

        if (onShore < requiredRocks) return false;

        credit = Credit(terrain, found);
        return true;
    }


    // MARK: shore


    private bool IsShore(TerrainManager terrain, Vector3Int cell)
    {
        if (cell.y < 0 || IsWater(terrain, cell)) return false;

        for (int i = 0; i < ORTHOGONAL.Length; i++)
        {
            Vector3Int neighbour = cell + ORTHOGONAL[i];

            // the water is whatever tops the next column over, usually a level or two down
            int top = terrain.GetTopLevel(neighbour.x, neighbour.z);
            if (top < 0) continue;

            if (IsWater(terrain, new Vector3Int(neighbour.x, top, neighbour.z))) return true;
        }

        return false;
    }


    private bool IsWater(TerrainManager terrain, Vector3Int cell)
    {
        if (seafloor != null && terrain.GetTile(cell) == seafloor) return true;

        SeaLevel sea = SeaLevel.Instance;
        if (sea == null || !sea.IsRisen) return false;

        return terrain.GetLevelHeight(cell.y) < sea.SurfaceY;
    }


    // MARK: credit


    // the rock just placed if it's one of the shore ones, else whichever shore rock was found first
    private PuzzleCredit Credit(TerrainManager terrain, EntityId found)
    {
        bool useLast = hasLastPlacedEntity && IsLive(lastPlacedEntity) && IsShore(terrain, lastPlacedCell);
        EntityId entity = useLast ? lastPlacedEntity : found;

        hasLastPlacedEntity = false;
        lastPlacedEntity = null;

        return entity != null ? PuzzleCredit.Entity(entity) : PuzzleCredit.None;
    }


    // MARK: validation


    private void OnValidate()
    {
        if (rocks.Count == 0 || rocks.Contains(null))
        {
            Debug.LogError($"RockyShores: '{name}' has a missing rock, so it can't count every rock on the shore", this);
        }

        if (seafloor == null)
        {
            Debug.LogError($"RockyShores: '{name}' has no seafloor tile, so only the live sea surface counts as water", this);
        }
    }
}
