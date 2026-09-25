using UnityEngine;


// Spawns its animal on a placed tile that ends up orthogonally next to another tile, on the same
// level. The neighbour can be authored terrain or something placed earlier - the terrain does not
// remember which, and a lake the level already has should count.
[CreateAssetMenu(
    fileName = "NinePatchTileRule",
    menuName = "Animals/Spawn Rules/Nine Patch Tile"
)]
public class NinePatchTileRule : AnimalSpawnRule
{
    private static readonly Vector3Int[] CENTRE_OFFSETS =
    {
        //top row
        new Vector3Int(-1, 0, 1),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1),
        //center row
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        //bottom row
        new Vector3Int(-1, 0, -1),
        new Vector3Int(0, 0, -1),
        new Vector3Int(1, 0, -1),
    };


    [SerializeField] private TerrainTile placedTile;
    [SerializeField] private TerrainTile neighbourTile;


    public override void OnItemPlaced(AnimalSpawnContext context, GameItem item, Vector3Int cell)
    {
        if (context == null || item == null) return;
        if (placedTile == null || neighbourTile == null) return;

        // tiles are matched rather than items, so a tile-only item like Water works
        if (item.Tile != placedTile) return;

        if (context.HasFired(this)) return;

        TerrainManager terrain = context.Terrain;
        if (terrain == null) return;

        for (int i = 0; i < CENTRE_OFFSETS.Length; i++)
        {
            //assume the center of the 3x3 is one of the neighbouring tiles to the one just placed
            Vector3Int center = cell + CENTRE_OFFSETS[i];

            //check if our new center is itself valid
            if (terrain.GetTile(center) != placedTile) continue;


            //assume the center we have picked is surrounded by valid neighbours
            bool complete = true;

            //find potential center
            for (int j = 0; j < CENTRE_OFFSETS.Length; j++) {
                //try to prove assumption false by check surrounding (3x3) for an incorrect tile
                if (terrain.GetTile(center + CENTRE_OFFSETS[j]) != neighbourTile) {
                    complete = false;
                    //every tile needs to pass the check, so if it fails once we can immediately stop
                    break;
                }
            }
            
            
            if (!complete) continue;

            context.Spawn(this, terrain.CellToWorld(center));
            return;
        }
    }
}
