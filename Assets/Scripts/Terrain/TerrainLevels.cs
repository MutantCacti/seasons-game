using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Finds the tilemaps under a Grid and sorts them into terrain levels
public static class TerrainLevels
{
    private const float HEIGHT_OFFSET = 1000.0f;


    public struct Level
    {
        public float height;  // world y of this layer's surface
        public List<Tilemap> tilemaps;
    }


    // Ascending by height
    public static List<Level> Discover(Transform root)
    {
        SortedDictionary<int, List<Tilemap>> tilemapsByHeight = new SortedDictionary<int, List<Tilemap>>();

        foreach (Tilemap tilemap in root.GetComponentsInChildren<Tilemap>())
        {
            BoundsInt bounds = tilemap.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0) continue;

            int key = Mathf.RoundToInt(tilemap.transform.position.y * HEIGHT_OFFSET);

            if (!tilemapsByHeight.TryGetValue(key, out List<Tilemap> sharing))
            {
                sharing = new List<Tilemap>();
                tilemapsByHeight[key] = sharing;
            }

            sharing.Add(tilemap);
        }

        List<Level> levels = new List<Level>(tilemapsByHeight.Count);

        foreach (KeyValuePair<int, List<Tilemap>> entry in tilemapsByHeight)
        {
            levels.Add(new Level
            {
                height = entry.Key / HEIGHT_OFFSET,
                tilemaps = entry.Value,
            });
        }

        return levels;
    }
}
