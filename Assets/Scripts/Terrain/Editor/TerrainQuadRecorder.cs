using System.Collections.Generic;
using UnityEngine;


// Bake time utility to generate quad maps
public class TerrainQuadRecorder
{
    public struct Row
    {
        public Vector3Int cell;
        public int vertexStart;
        public Vector4 texelOffsets;
    }


    public readonly List<Row> rows = new List<Row>();


    public void Record(Vector3Int cell, int vertexStart, Vector4 texelOffsets)
    {
        rows.Add(new Row { cell = cell, vertexStart = vertexStart, texelOffsets = texelOffsets });
    }
}
