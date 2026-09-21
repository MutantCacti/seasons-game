using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the rendered wall mesh
public static class TerrainWallMeshBuilder
{
    // absorbs float error when a wall height is a whole number of units but does not
    // land exactly on it, e.g. 2.0 arriving as 1.9999999
    private const float EPSILON = 1e-4f;

    private enum WallShading
    {
        PerFace,
        Flat,
        SmoothedOutline,
    }

    private static readonly WallShading SHADING = WallShading.Flat;

    private static readonly Vector3 FLAT_WALL_NORMAL = Vector3.up;

    private const int SMOOTH_RADIUS = 5;
    private const float SMOOTH_AXIS_BLEND = 0.5f;

    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<Vector3> normals = new List<Vector3>();
    private static readonly List<Vector2> uvs = new List<Vector2>();
    private static readonly List<int> triangles = new List<int>();


    public static void Fill(Mesh mesh, TerrainRaster raster, TerrainSurfaces surfaces, Texture2D wallAtlas,
        UnityEngine.Object context, TerrainQuadRecorder recorder)
    {
        vertices.Clear();
        normals.Clear();
        uvs.Clear();
        triangles.Clear();

        bool useAtlas = TerrainAtlasValidation.ValidateWallAtlas(raster, wallAtlas, context);
        float atlasWidth = useAtlas ? wallAtlas.width : 1.0f;
        float atlasHeight = useAtlas ? wallAtlas.height : 1.0f;
        int pixelsPerTile = raster.pixelsPerTile;

        foreach (WallRun run in surfaces.walls)
        {
            bool facingX = run.du != 0;

            int tileInnerBase = (facingX ? run.ownerCellY - raster.cellOriginY : run.ownerCellX - raster.cellOriginX) * pixelsPerTile;

            float uStart, uEnd, vBottom, vTop;

            // where this run's quads sit inside its tile, in texels from the slice origin
            float offsetU0 = 0.0f;
            float offsetU1 = 0.0f;
            float sliceHeight = 0.0f;
            bool recordable = false;

            if (useAtlas)
            {
                Sprite sprite = raster.GetSprite(run.levelIndex, run.ownerCellX, run.ownerCellY);
                if (sprite == null) continue;

                Rect slice = sprite.rect;
                offsetU0 = run.startInner - tileInnerBase;
                offsetU1 = run.endInner - tileInnerBase;
                sliceHeight = slice.height;

                uStart = (slice.x + offsetU0) / atlasWidth;
                uEnd = (slice.x + offsetU1) / atlasWidth;
                vBottom = slice.y / atlasHeight;
                vTop = (slice.y + sliceHeight) / atlasHeight;

                // the branch below projects UVs from world position, which no remap can change,
                // so there is nothing to record there and nothing to patch later
                recordable = recorder != null;
            }
            else
            {
                // fall back to material
                uStart = facingX ? raster.PixelToWorldZ(run.startInner) : raster.PixelToWorldX(run.startInner);
                uEnd = facingX ? raster.PixelToWorldZ(run.endInner) : raster.PixelToWorldX(run.endInner);
                vBottom = 0.0f;
                vTop = 1.0f;
            }

            float height = run.top - run.bottom;

            // the epsilon is what stops a height a hair over a whole number from asking for one
            // more segment than it needs, which would leave a sliver quad microns tall
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(height - EPSILON));

            for (int segment = 0; segment < segmentCount; segment++)
            {
                float segmentTop = run.top - segment;

                // the last one lands exactly on the run's bottom rather than a unit below its
                // own top, so the segments always tile the run with no gap and no overlap
                float segmentBottom = segment == segmentCount - 1
                    ? run.bottom
                    : Mathf.Max(segmentTop - 1.0f, run.bottom);

                float fromBottom = run.top - segmentBottom;
                float fracBottom = Mathf.Clamp01(fromBottom - Mathf.Floor(Mathf.Max(fromBottom - EPSILON, 0.0f)));
                float vSegmentBottom = vTop - fracBottom * (vTop - vBottom);

                int vertexStart = vertices.Count;

                AddWallQuad(raster, run, segmentBottom, segmentTop, uStart, uEnd, vSegmentBottom, vTop);

                if (recordable)
                {
                    // the segment's bottom edge sits fracBottom of a slice up from the slice
                    // bottom, and its top edge is the slice top
                    float offsetV0 = sliceHeight * (1.0f - fracBottom);

                    recorder.Record(new Vector3Int(run.ownerCellX, run.levelIndex, run.ownerCellY),
                        vertexStart, new Vector4(offsetU0, offsetV0, offsetU1, sliceHeight));
                }
            }
        }

        mesh.Clear();
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
    }


    private static Vector3 ShadingNormal(TerrainRaster raster, WallRun run, Vector3 faceNormal)
    {
        switch (SHADING)
        {
            case WallShading.Flat: return FLAT_WALL_NORMAL;
            case WallShading.SmoothedOutline: return OutlineNormal(raster, run, faceNormal);
            default: return faceNormal;
        }
    }


    private static Vector3 OutlineNormal(TerrainRaster raster, WallRun run, Vector3 faceNormal)
    {
        int middle = (run.startInner + run.endInner) / 2;
        int u = run.du != 0 ? run.outer : middle;
        int v = run.du != 0 ? middle : run.outer;

        float awayX = 0.0f;
        float awayZ = 0.0f;

        for (int offsetV = -SMOOTH_RADIUS; offsetV <= SMOOTH_RADIUS; offsetV++)
        {
            for (int offsetU = -SMOOTH_RADIUS; offsetU <= SMOOTH_RADIUS; offsetU++)
            {
                if (offsetU * offsetU + offsetV * offsetV > SMOOTH_RADIUS * SMOOTH_RADIUS) continue;
                if (!IsSolid(raster, run.levelIndex, u + offsetU, v + offsetV)) continue;

                awayX -= offsetU;
                awayZ -= offsetV;
            }
        }

        Vector3 outward = new Vector3(awayX, 0.0f, awayZ);

        if (outward.sqrMagnitude < 1e-6f) return faceNormal;

        outward.Normalize();

        return Vector3.Normalize(Vector3.Lerp(outward, faceNormal, SMOOTH_AXIS_BLEND));
    }


    private static bool IsSolid(TerrainRaster raster, int levelIndex, int u, int v)
    {
        if (u < 0 || u >= raster.pixelWidth || v < 0 || v >= raster.pixelHeight) return false;

        return raster.IsSolid(levelIndex, u, v);
    }


    // wound clockwise which Unity renders as front facing
    private static void AddWallQuad(TerrainRaster raster, WallRun run, float bottom, float top,
        float uStart, float uEnd, float vBottom, float vTop)
    {
        Vector3 a, b, c, d;
        Vector3 normal;
        bool startFirst;

        if (run.du != 0)
        {
            float x = raster.PixelToWorldX(run.du > 0 ? run.outer + 1 : run.outer);
            float z0 = raster.PixelToWorldZ(run.startInner);
            float z1 = raster.PixelToWorldZ(run.endInner);
            normal = new Vector3(run.du, 0.0f, 0.0f);
            startFirst = run.du > 0;

            float near = startFirst ? z0 : z1;
            float far = startFirst ? z1 : z0;

            a = new Vector3(x, bottom, near);
            b = new Vector3(x, top, near);
            c = new Vector3(x, top, far);
            d = new Vector3(x, bottom, far);
        }
        else
        {
            float z = raster.PixelToWorldZ(run.dv > 0 ? run.outer + 1 : run.outer);
            float x0 = raster.PixelToWorldX(run.startInner);
            float x1 = raster.PixelToWorldX(run.endInner);
            normal = new Vector3(0.0f, 0.0f, run.dv);
            startFirst = run.dv < 0;

            float near = startFirst ? x0 : x1;
            float far = startFirst ? x1 : x0;

            a = new Vector3(near, bottom, z);
            b = new Vector3(near, top, z);
            c = new Vector3(far, top, z);
            d = new Vector3(far, bottom, z);
        }

        float uNear = startFirst ? uStart : uEnd;
        float uFar = startFirst ? uEnd : uStart;

        int baseIndex = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        // the quad still faces the way it faces, this only changes how it is lit
        Vector3 shaded = ShadingNormal(raster, run, normal);

        normals.Add(shaded);
        normals.Add(shaded);
        normals.Add(shaded);
        normals.Add(shaded);

        uvs.Add(new Vector2(uNear, vBottom));
        uvs.Add(new Vector2(uNear, vTop));
        uvs.Add(new Vector2(uFar, vTop));
        uvs.Add(new Vector2(uFar, vBottom));

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }
}
