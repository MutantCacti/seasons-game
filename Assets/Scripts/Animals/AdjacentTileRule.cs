using UnityEngine;


// Spawns its animal on a placed tile that ends up orthogonally next to another tile, on the same
// level. The neighbour can be authored terrain or something placed earlier - the terrain does not
// remember which, and a lake the level already has should count.
[CreateAssetMenu(
    fileName = "AdjacentTileRule",
    menuName = "Animals/Spawn Rules/Adjacent Tile"
)]
public class AdjacentTileRule : AnimalSpawnRule
{
    private static readonly Vector3Int[] ORTHOGONALS =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1),
    };


    [SerializeField] private TerrainTile placedTile;
    [SerializeField] private TerrainTile neighbourTile;


    public override void OnItemPlaced(AnimalSpawnContext context, GameItem item, Vector3Int cell)
    {
        if (context == null || item == null) return;
        if (placedTile == null || neighbourTile == null) return;

        // tiles are matched rather than items, so a tile-only item like Water works
        if (item.Tile != placedTile) return;

        TerrainManager terrain = context.Terrain;
        if (terrain == null) return;

        for (int i = 0; i < ORTHOGONALS.Length; i++)
        {
            if (terrain.GetTile(cell + ORTHOGONALS[i]) != neighbourTile) continue;

            context.Spawn(this, terrain.CellToWorld(cell));
            return;
        }
    }


    // placement only until this rule is ported to the new spawn check
    protected override bool TryFindSpawn(AnimalSpawnContext context, out Vector3 position)
    {
        position = default;
        return false;
    }
}
