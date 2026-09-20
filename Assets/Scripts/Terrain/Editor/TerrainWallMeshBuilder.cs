using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the rendered wall mesh
public static class TerrainWallMeshBuilder
{
    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<Vector3> normals = new List<Vector3>();
    private static readonly List<Vector2> uvs = new List<Vector2>();
    private static readonly List<int> triangles = new List<int>();


    public static void Fill(Mesh mesh, TerrainRaster raster, TerrainSurfaces surfaces, Texture2D wallAtlas, UnityEngine.Object context)
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

            if (useAtlas)
            {
                Sprite sprite = raster.GetSprite(run.levelIndex, run.ownerCellX, run.ownerCellY);
                if (sprite == null) continue;

                Rect slice = sprite.rect;
                uStart = (slice.x + run.startInner - tileInnerBase) / atlasWidth;
                uEnd = (slice.x + run.endInner - tileInnerBase) / atlasWidth;
                vBottom = slice.y / atlasHeight;
                vTop = (slice.y + slice.height) / atlasHeight;
            }
            else
            {
                // fall back to material
                uStart = facingX ? raster.PixelToWorldZ(run.startInner) : raster.PixelToWorldX(run.startInner);
                uEnd = facingX ? raster.PixelToWorldZ(run.endInner) : raster.PixelToWorldX(run.endInner);
                vBottom = 0.0f;
                vTop = 1.0f;
            }

            for (int segment = run.bottom; segment < run.top; segment++)
            {
                AddWallQuad(raster, run, segment, segment + 1, uStart, uEnd, vBottom, vTop);
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

        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

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
