using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the sea mesh: one flat sheet over the world's silhouette (every pixel that is solid on
// any level), plus skirt quads around its edge that the sea shader can drop to the void.
// Authored at local y = 0, SeaLevel moves the transform to the actual level.
//
// uv0 is world XZ in world units. uv1 flags the skirt: x = 1 on skirt vertices, y = 1 on the
// skirt's bottom edge. The skirt is always baked; the shader collapses it when the material's
// skirt is off.
public static class TerrainSeaMeshBuilder
{
    // extra room under the skirt's bounds, because the shader pins the skirt's bottom to the void
    // in world space, and that lands below the baked bounds by however high the level is set
    private const float SKIRT_BOUNDS_MARGIN = 4.0f;


    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<Vector3> normals = new List<Vector3>();
    private static readonly List<Vector2> uv0 = new List<Vector2>();
    private static readonly List<Vector2> uv1 = new List<Vector2>();
    private static readonly List<int> triangles = new List<int>();


    public static void Fill(Mesh mesh, TerrainRaster raster, float voidDepth)
    {
        vertices.Clear();
        normals.Clear();
        uv0.Clear();
        uv1.Clear();
        triangles.Clear();

        bool[] occupied = BuildOccupancy(raster);

        // skirts go first in the index buffer: blending follows primitive order within a draw,
        // so the sheet always blends over the skirt, which is the right way round from above
        EmitSkirts(raster, occupied, voidDepth);
        EmitSheet(raster, occupied);

        mesh.Clear();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        Bounds bounds = mesh.bounds;
        bounds.Encapsulate(new Vector3(bounds.center.x, voidDepth - SKIRT_BOUNDS_MARGIN, bounds.center.z));
        mesh.bounds = bounds;
    }


    // the union of every level's mask, i.e. the world's footprint seen from above
    public static bool[] BuildOccupancy(TerrainRaster raster)
    {
        bool[] occupied = new bool[raster.pixelWidth * raster.pixelHeight];

        foreach (bool[] mask in raster.levelMasks)
        {
            for (int i = 0; i < occupied.Length; i++)
            {
                if (mask[i]) occupied[i] = true;
            }
        }

        return occupied;
    }


    // MARK: sheet


    // greedy rectangles: take the widest run from the first open pixel, then grow it down for as
    // long as the rows below are open across the same span
    private static void EmitSheet(TerrainRaster raster, bool[] occupied)
    {
        int width = raster.pixelWidth;
        int height = raster.pixelHeight;
        bool[] open = (bool[])occupied.Clone();

        for (int v = 0; v < height; v++)
        {
            for (int u = 0; u < width; u++)
            {
                if (!open[v * width + u]) continue;

                int endU = u;
                while (endU < width && open[v * width + endU]) endU++;

                int endV = v + 1;
                while (endV < height && RowOpen(open, width, endV, u, endU)) endV++;

                for (int fillV = v; fillV < endV; fillV++)
                {
                    for (int fillU = u; fillU < endU; fillU++) open[fillV * width + fillU] = false;
                }

                float x0 = raster.PixelToWorldX(u);
                float x1 = raster.PixelToWorldX(endU);
                float z0 = raster.PixelToWorldZ(v);
                float z1 = raster.PixelToWorldZ(endV);

                // clockwise from above, same as the tops
                AddQuad(new Vector3(x0, 0.0f, z0), new Vector3(x0, 0.0f, z1),
                        new Vector3(x1, 0.0f, z1), new Vector3(x1, 0.0f, z0),
                        Vector3.up, false);
            }
        }
    }


    private static bool RowOpen(bool[] open, int width, int v, int startU, int endU)
    {
        for (int u = startU; u < endU; u++)
        {
            if (!open[v * width + u]) return false;
        }

        return true;
    }


    // MARK: skirts


    // one quad per straight run of silhouette edge, facing out of the water. Holes inside the
    // footprint get skirts too, facing into the hole
    private static void EmitSkirts(TerrainRaster raster, bool[] occupied, float voidDepth)
    {
        int width = raster.pixelWidth;
        int height = raster.pixelHeight;

        // x-facing edges sit on the pixel columns' boundaries
        for (int u = 0; u <= width; u++)
        {
            EmitEdgeRuns(height, v => IsOccupied(occupied, width, height, u - 1, v) && !IsOccupied(occupied, width, height, u, v),
                (start, end) => AddXFace(raster, u, start, end, 1, voidDepth));
            EmitEdgeRuns(height, v => !IsOccupied(occupied, width, height, u - 1, v) && IsOccupied(occupied, width, height, u, v),
                (start, end) => AddXFace(raster, u, start, end, -1, voidDepth));
        }

        // z-facing edges sit on the pixel rows' boundaries
        for (int v = 0; v <= height; v++)
        {
            EmitEdgeRuns(width, u => IsOccupied(occupied, width, height, u, v - 1) && !IsOccupied(occupied, width, height, u, v),
                (start, end) => AddZFace(raster, v, start, end, 1, voidDepth));
            EmitEdgeRuns(width, u => !IsOccupied(occupied, width, height, u, v - 1) && IsOccupied(occupied, width, height, u, v),
                (start, end) => AddZFace(raster, v, start, end, -1, voidDepth));
        }
    }


    private static void EmitEdgeRuns(int length, System.Func<int, bool> isEdge, System.Action<int, int> emit)
    {
        int runStart = -1;

        for (int i = 0; i <= length; i++)
        {
            bool edge = i < length && isEdge(i);

            if (edge && runStart < 0) runStart = i;

            if (!edge && runStart >= 0)
            {
                emit(runStart, i);
                runStart = -1;
            }
        }
    }


    // outside the raster counts as empty, so the outer rim gets its skirt
    private static bool IsOccupied(bool[] occupied, int width, int height, int u, int v)
    {
        if (u < 0 || u >= width || v < 0 || v >= height) return false;

        return occupied[v * width + u];
    }


    // winding matches TerrainCollisionMeshBuilder's walls, clockwise seen from outside
    private static void AddXFace(TerrainRaster raster, int boundaryU, int startV, int endV, int du, float voidDepth)
    {
        float x = raster.PixelToWorldX(boundaryU);
        float z0 = raster.PixelToWorldZ(startV);
        float z1 = raster.PixelToWorldZ(endV);
        float near = du > 0 ? z0 : z1;
        float far = du > 0 ? z1 : z0;

        AddQuad(new Vector3(x, voidDepth, near), new Vector3(x, 0.0f, near),
                new Vector3(x, 0.0f, far), new Vector3(x, voidDepth, far),
                new Vector3(du, 0.0f, 0.0f), true);
    }


    private static void AddZFace(TerrainRaster raster, int boundaryV, int startU, int endU, int dv, float voidDepth)
    {
        float z = raster.PixelToWorldZ(boundaryV);
        float x0 = raster.PixelToWorldX(startU);
        float x1 = raster.PixelToWorldX(endU);
        float near = dv < 0 ? x0 : x1;
        float far = dv < 0 ? x1 : x0;

        AddQuad(new Vector3(near, voidDepth, z), new Vector3(near, 0.0f, z),
                new Vector3(far, 0.0f, z), new Vector3(far, voidDepth, z),
                new Vector3(0.0f, 0.0f, dv), true);
    }


    // MARK: quads


    private static void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool skirt)
    {
        int baseIndex = vertices.Count;

        AddVertex(a, normal, skirt);
        AddVertex(b, normal, skirt);
        AddVertex(c, normal, skirt);
        AddVertex(d, normal, skirt);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }


    private static void AddVertex(Vector3 position, Vector3 normal, bool skirt)
    {
        vertices.Add(position);
        normals.Add(normal);
        uv0.Add(new Vector2(position.x, position.z));

        // anything below the sheet is the skirt's bottom edge
        bool bottom = skirt && position.y < 0.0f;
        uv1.Add(new Vector2(skirt ? 1.0f : 0.0f, bottom ? 1.0f : 0.0f));
    }
}
