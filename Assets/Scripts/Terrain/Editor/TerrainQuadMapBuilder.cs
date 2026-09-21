using System.Collections.Generic;
using UnityEngine;


// Packs recorded quad rows into a TerrainQuadMap, sorted by (level, z, x)
public static class TerrainQuadMapBuilder
{
    public static void Fill(TerrainQuadMap map, TerrainQuadRecorder recorder, Mesh mesh,
        int pixelsPerTile, Texture2D atlas, uint occupancyHash)
    {
        List<TerrainQuadRecorder.Row> rows = recorder.rows;

        rows.Sort(CompareRows);

        List<int> cellX = new List<int>();
        List<int> cellLevel = new List<int>();
        List<int> cellZ = new List<int>();
        List<int> firstQuad = new List<int>();
        List<int> quadCount = new List<int>();
        List<int> quadVertexStart = new List<int>(rows.Count);
        List<Vector4> quadTexelOffsets = new List<Vector4>(rows.Count);

        int index = 0;

        while (index < rows.Count)
        {
            Vector3Int cell = rows[index].cell;
            int first = quadVertexStart.Count;

            // the rows are sorted, so one cell's quads are contiguous
            while (index < rows.Count && rows[index].cell == cell)
            {
                quadVertexStart.Add(rows[index].vertexStart);
                quadTexelOffsets.Add(rows[index].texelOffsets);
                index++;
            }

            cellX.Add(cell.x);
            cellLevel.Add(cell.y);
            cellZ.Add(cell.z);
            firstQuad.Add(first);
            quadCount.Add(quadVertexStart.Count - first);
        }

        map.mesh = mesh;
        map.pixelsPerTile = pixelsPerTile;
        map.atlasWidth = atlas != null ? atlas.width : 0;
        map.atlasHeight = atlas != null ? atlas.height : 0;
        map.vertexCount = mesh != null ? mesh.vertexCount : 0;
        map.occupancyHash = occupancyHash;

        map.cellX = cellX.ToArray();
        map.cellLevel = cellLevel.ToArray();
        map.cellZ = cellZ.ToArray();
        map.firstQuad = firstQuad.ToArray();
        map.quadCount = quadCount.ToArray();
        map.quadVertexStart = quadVertexStart.ToArray();
        map.quadTexelOffsets = quadTexelOffsets.ToArray();
    }


    // must match TerrainQuadMap.CompareCell
    private static int CompareRows(TerrainQuadRecorder.Row a, TerrainQuadRecorder.Row b)
    {
        if (a.cell.y != b.cell.y) return a.cell.y.CompareTo(b.cell.y);
        if (a.cell.z != b.cell.z) return a.cell.z.CompareTo(b.cell.z);

        return a.cell.x.CompareTo(b.cell.x);
    }
}
