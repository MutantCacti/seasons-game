using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the collision mesh, of all the tops and walls in a single surface
// optimised with some very simple quad merging based on run length
public static class TerrainCollisionMeshBuilder
{
    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<int> triangles = new List<int>();


    public static void Fill(Mesh mesh, TerrainRaster raster, TerrainSurfaces surfaces)
    {
        vertices.Clear();
        triangles.Clear();

        foreach (TopRun run in surfaces.tops)
        {
            float y = raster.levelHeights[run.levelIndex];
            float x0 = raster.PixelToWorldX(run.startU);
            float x1 = raster.PixelToWorldX(run.endU);
            float z0 = raster.PixelToWorldZ(run.startV);
            float z1 = raster.PixelToWorldZ(run.endV);

            AddQuad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0));
        }

        foreach (WallRun run in surfaces.walls)
        {
            float bottom = run.bottom;
            float top = run.top;

            if (run.du != 0)
            {
                float x = raster.PixelToWorldX(run.du > 0 ? run.outer + 1 : run.outer);
                float z0 = raster.PixelToWorldZ(run.startInner);
                float z1 = raster.PixelToWorldZ(run.endInner);
                float near = run.du > 0 ? z0 : z1;
                float far = run.du > 0 ? z1 : z0;

                AddQuad(new Vector3(x, bottom, near), new Vector3(x, top, near),
                        new Vector3(x, top, far), new Vector3(x, bottom, far));
            }
            else
            {
                float z = raster.PixelToWorldZ(run.dv > 0 ? run.outer + 1 : run.outer);
                float x0 = raster.PixelToWorldX(run.startInner);
                float x1 = raster.PixelToWorldX(run.endInner);
                float near = run.dv < 0 ? x0 : x1;
                float far = run.dv < 0 ? x1 : x0;

                AddQuad(new Vector3(near, bottom, z), new Vector3(near, top, z),
                        new Vector3(far, top, z), new Vector3(far, bottom, z));
            }
        }

        mesh.Clear();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }


    private static void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int baseIndex = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }
}
