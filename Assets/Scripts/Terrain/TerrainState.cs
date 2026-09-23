using UnityEngine;


// Invariant: the map never changes shape at runtime, so this is sized once and SetTile only ever
// swaps the tile in an already-occupied cell.
// State is a private class of TerrainManager because nobody else should be calling SetTile!
// otherwise it will not get saved correctly to disk
public partial class TerrainManager
{
    private class State
    {
        // cell space bounds
        public readonly int originX;
        public readonly int originZ;
        public readonly int width;
        public readonly int depth;

        public readonly float[] levelHeights;  // world y per level, ascending

        // this is called a jagged array and is apparently much faster than using a
        // 3D array for caching reasons. The 2nd dimsension flattens the XZ plane
        private readonly TerrainTile[][] tileByCell;  // [level][z * width + x]


        public State(int originX, int originZ, int width, int depth, float[] levelHeights)
        {
            this.originX = originX;
            this.originZ = originZ;
            this.width = width;
            this.depth = depth;
            this.levelHeights = levelHeights;

            tileByCell = new TerrainTile[levelHeights.Length][];

            for (int level = 0; level < levelHeights.Length; level++)
            {
                tileByCell[level] = new TerrainTile[width * depth];
            }
        }


        public int LevelCount { get { return levelHeights.Length; } }


        // cell is (x, level, z); see TerrainManager
        public bool Contains(Vector3Int cell)
        {
            if (cell.y < 0 || cell.y >= levelHeights.Length) return false;

            int localX = cell.x - originX;
            int localZ = cell.z - originZ;

            return localX >= 0 && localX < width && localZ >= 0 && localZ < depth;
        }


        // null for an empty or nonexistent cell
        public TerrainTile GetTile(Vector3Int cell)
        {
            if (!Contains(cell)) return null;

            return tileByCell[cell.y][(cell.z - originZ) * width + (cell.x - originX)];
        }


        // only reachable from inside TerrainManager
        public void SetTile(Vector3Int cell, TerrainTile tile)
        {
            if (!Contains(cell)) return;

            tileByCell[cell.y][(cell.z - originZ) * width + (cell.x - originX)] = tile;
        }
    }
}
