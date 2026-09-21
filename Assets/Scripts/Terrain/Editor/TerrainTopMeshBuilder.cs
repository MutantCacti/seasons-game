using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the rendered top surface mesh
public static class TerrainTopMeshBuilder
{
    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<Vector3> normals = new List<Vector3>();
    private static readonly List<Vector2> uvs = new List<Vector2>();
    private static readonly List<int> triangles = new List<int>();


    public static void Fill(Mesh mesh, TerrainRaster raster, TerrainSurfaces surfaces,
        UnityEngine.Object context, TerrainQuadRecorder recorder)
    {
        vertices.Clear();
        normals.Clear();
        uvs.Clear();
        triangles.Clear();

        Texture2D atlas = TerrainAtlasValidation.FindTileAtlas(raster);
        TerrainAtlasValidation.WarnIfUnsuitable(atlas, context);

        float atlasWidth = atlas != null ? atlas.width : 1.0f;
        float atlasHeight = atlas != null ? atlas.height : 1.0f;
        int pixelsPerTile = raster.pixelsPerTile;
        int missingSprites = 0;

        foreach (TopRun run in surfaces.tops)
        {
            float y = raster.levelHeights[run.levelIndex];
            float x0 = raster.PixelToWorldX(run.startU);
            float x1 = raster.PixelToWorldX(run.endU);
            float z0 = raster.PixelToWorldZ(run.startV);
            float z1 = raster.PixelToWorldZ(run.endV);

            float u0, u1, v0, v1;

            // where this quad sits inside its tile, in texels from the slice origin. Invariant
            // across remaps, which is what lets the quad map store these rather than UVs
            Vector4 texelOffsets = Vector4.zero;
            bool recordable = false;

            if (atlas != null)
            {
                Sprite sprite = raster.GetSprite(run.levelIndex, run.ownerCellX, run.ownerCellY);
                if (sprite == null)
                {
                    missingSprites++;
                    continue;
                }

                Rect slice = sprite.rect;
                int tileBaseU = (run.ownerCellX - raster.cellOriginX) * pixelsPerTile;
                int tileBaseV = (run.ownerCellY - raster.cellOriginY) * pixelsPerTile;

                texelOffsets = new Vector4(
                    run.startU - tileBaseU, run.startV - tileBaseV,
                    run.endU - tileBaseU, run.endV - tileBaseV);

                u0 = (slice.x + texelOffsets.x) / atlasWidth;
                u1 = (slice.x + texelOffsets.z) / atlasWidth;
                v0 = (slice.y + texelOffsets.y) / atlasHeight;
                v1 = (slice.y + texelOffsets.w) / atlasHeight;

                // the branch below projects UVs from world position, which no remap can change,
                // so there is nothing to record there and nothing to patch later
                recordable = recorder != null;
            }
            else
            {
                u0 = x0; u1 = x1; v0 = z0; v1 = z1;
            }

            int vertexStart = vertices.Count;

            AddQuad(new Vector3(x0, y, z0), new Vector2(u0, v0),
                    new Vector3(x0, y, z1), new Vector2(u0, v1),
                    new Vector3(x1, y, z1), new Vector2(u1, v1),
                    new Vector3(x1, y, z0), new Vector2(u1, v0));

            if (recordable)
            {
                recorder.Record(new Vector3Int(run.ownerCellX, run.levelIndex, run.ownerCellY),
                    vertexStart, texelOffsets);
            }
        }

        if (missingSprites > 0)
        {
            Debug.LogWarning($"was not able to find sprites for {missingSprites} top runs", context);
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


    // clockwise winding which Unity renders as front facing
    private static void AddQuad(Vector3 a, Vector2 uvA, Vector3 b, Vector2 uvB,
        Vector3 c, Vector2 uvC, Vector3 d, Vector2 uvD)
    {
        int baseIndex = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }
}
