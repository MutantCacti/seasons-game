using UnityEngine;


// Stores each quad's texel offsets within its slice (which are invariant)
// patch by (newSlice.origin + offset) / atlasSize
// Written by TerrainQuadMapBuilder at bake time
public class TerrainQuadMap : ScriptableObject
{
    public Mesh mesh;           // the mesh these quads belong to
    public int pixelsPerTile;
    public int atlasWidth;
    public int atlasHeight;
    public int vertexCount;     // for validation purposes

    // Also for validation purposes
    public uint occupancyHash;


    // one entry per cell, sorted by (level, z, x)
    public int[] cellX;
    public int[] cellLevel;
    public int[] cellZ;
    public int[] firstQuad;
    public int[] quadCount;

    // one entry per quad
    public int[] quadVertexStart;
    public Vector4[] quadTexelOffsets;


    public int CellCount { get { return cellX != null ? cellX.Length : 0; } }
    public int QuadCount { get { return quadVertexStart != null ? quadVertexStart.Length : 0; } }


    // Binary search over sorted cell rows
    public bool TryGetQuads(Vector3Int cell, out int first, out int count)
    {
        first = 0;
        count = 0;

        if (cellX == null || cellLevel == null || cellZ == null) return false;
        if (firstQuad == null || quadCount == null) return false;
        if (cellX.Length == 0) return false;

        int low = 0;
        int high = cellX.Length - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            int order = CompareCell(mid, cell);

            if (order == 0)
            {
                first = firstQuad[mid];
                count = quadCount[mid];
                return count > 0;
            }

            if (order < 0) low = mid + 1;
            else high = mid - 1;
        }

        return false;
    }


    // must match the order TerrainQuadMapBuilder sorts rows into
    private int CompareCell(int index, Vector3Int cell)
    {
        if (cellLevel[index] != cell.y) return cellLevel[index].CompareTo(cell.y);
        if (cellZ[index] != cell.z) return cellZ[index].CompareTo(cell.z);

        return cellX[index].CompareTo(cell.x);
    }
}
