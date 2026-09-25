using UnityEngine;


// Spawns its animal in the center of a 3x3 patch whose center must be a neighbour of the tile just
// placed, on the same level.
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


    //the below function will return early if the tile just placed is not (the) requiredTileToBePlaced
    [SerializeField] private TerrainTile requiredTileToBePlaced;
    [SerializeField] private TerrainTile desiredNeighbouringTile;


    public override void OnItemPlaced(AnimalSpawnContext context, GameItem itemOrTileJustPlaced, Vector3Int cell) {
        if (context == null || itemOrTileJustPlaced == null) return;
        if (requiredTileToBePlaced == null || desiredNeighbouringTile == null) return;

        // tiles are matched rather than items, so a tile-only item like Water works
        if (itemOrTileJustPlaced.Tile != requiredTileToBePlaced) return;

        if (context.HasFired(this)) return;

        TerrainManager terrain = context.Terrain;
        if (terrain == null) return;


        for (int i = 0; i < CENTRE_OFFSETS.Length; i++) {
            //assume the center of the 3x3 is one of the neighbouring tiles to the one just placed
            Vector3Int center = cell + CENTRE_OFFSETS[i];

            //check if our new center is itself valid
            if (terrain.GetTile(center) != requiredTileToBePlaced) continue;


            //assume the center we have picked is surrounded by valid neighbours
            bool valid = true;

            //find potential center
            for (int j = 0; j < CENTRE_OFFSETS.Length; j++) {
                //try to prove assumption false by check surrounding (3x3) for an incorrect tile
                if (terrain.GetTile(center + CENTRE_OFFSETS[j]) != desiredNeighbouringTile) {
                    valid = false;
                    //every tile needs to pass the check, so if it fails once we can immediately stop
                    break;
                }
            }
            
            
            if (!valid) continue;

            context.Spawn(this, terrain.CellToWorld(center));
            return;
        }
    }
    
}
