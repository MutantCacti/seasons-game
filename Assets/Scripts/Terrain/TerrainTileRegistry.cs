using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(
    fileName="TerrainTileRegistry",
    menuName="Terrain/Terrain Tile Registry"
)]
public class TerrainTileRegistry : ScriptableObject
{
    [SerializeField] private List<TerrainTile> tiles = new List<TerrainTile>();


    [System.NonSerialized] private Dictionary<string, TerrainTile> tileById;


    public IReadOnlyList<TerrainTile> Tiles { get { return tiles; } }
    public int Count { get { return tiles != null ? tiles.Count : 0; } }


    // MARK: lookup


    public bool TryGetTileById(string id, out TerrainTile tile)
    {
        tile = null;
        if (string.IsNullOrEmpty(id)) return false;

        EnsureLookup();

        return tileById.TryGetValue(id, out tile) && tile != null;
    }


    public bool TryGetId(TerrainTile tile, out string id)
    {
        id = null;
        if (tile == null) return false;

        EnsureLookup();

        if (!string.IsNullOrEmpty(tile.Id) &&
            tileById.TryGetValue(tile.Id, out TerrainTile registered) &&
            registered == tile)
        {
            id = tile.Id;
            return true;
        }

        Debug.LogError($"TerrainTileRegistry: '{tile.name}' is not registered or its id '{tile.Id}' is empty or duplicated, so it cannot be written to a save", this);

        return false;
    }


    // MARK: construction


    // the lookup goes stale whenever the editor changes the tiles list
    private void OnValidate()
    {
        tileById = null;
    }


    public void Rebuild()
    {
        tileById = new Dictionary<string, TerrainTile>();
        if (tiles == null) return;

        for (int i = 0; i < tiles.Count; i++)
        {
            TerrainTile tile = tiles[i];

            if (tile == null)
            {
                Debug.LogError($"TerrainTileRegistry: entry {i} is empty", this);
                continue;
            }

            if (string.IsNullOrEmpty(tile.Id))
            {
                Debug.LogError($"TerrainTileRegistry: '{tile.name}' has no Id, so it can never be saved or loaded", tile);
                continue;
            }

            // first entry wins on duplicate
            if (tileById.TryGetValue(tile.Id, out TerrainTile existing))
            {
                Debug.LogError($"TerrainTileRegistry: duplicate Id '{tile.Id}' on '{tile.name}', keeping '{existing.name}' and ignoring this one", tile);
                continue;
            }

            tileById.Add(tile.Id, tile);
        }
    }


    private void EnsureLookup()
    {
        if (tileById != null) return;

        Rebuild();
    }
}