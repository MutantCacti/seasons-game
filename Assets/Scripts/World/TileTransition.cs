using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;


// A one shot cover over a cell whose tile just changed. It starts as the exact pixels of the old tile,
// cut to the cell's own baked top quads so a trimmed edge never flashes a full square, then breaks into
// 4px blocks that shrink away from the middle outwards, revealing the new tile the remap already put
// underneath. It never touches the terrain, is never registered with EntityManager (so it is never
// saved and never blocks a placement) and destroys itself when it's done.
public class TileTransition : MonoBehaviour
{
    // 8x8 blocks, so 4px blocks on a 32px tile
    private const int BLOCKS_PER_SIDE = 8;

    // just under the placement ripple's 0.002 so the ripple draws on top
    private const float COVER_OFFSET = 0.001f;


    // one shrinking rect of the cover. A block the top quads cut into several pieces shrinks
    // each piece to its own centre, so no piece drifts over a trimmed (transparent) corner
    private struct Piece
    {
        public Vector3 centre;
        public Vector3 min;
        public Vector3 max;
        public float delay;
    }


    private Mesh mesh;
    private Piece[] pieces;
    private Vector3[] vertices;
    private float blockDuration;
    private uint seed;


    // cellOrigin is the cell's (x0, height, z0) corner in world space. texelRects are the cell's top
    // quads as (x0, y0, x1, y1) texels inside the slice. Returns null when there is nothing to cover
    public static TileTransition Spawn(Vector3 cellOrigin, Sprite oldSprite, List<Vector4> texelRects,
        int pixelsPerTile, Material material, float duration, float blockDuration, float jitter)
    {
        if (oldSprite == null || material == null || texelRects == null || texelRects.Count == 0) return null;

        GameObject cover = new GameObject("Tile Transition");
        cover.transform.position = cellOrigin + Vector3.up * COVER_OFFSET;

        TileTransition transition = cover.AddComponent<TileTransition>();
        transition.Build(oldSprite, texelRects, pixelsPerTile, material, duration, blockDuration, jitter);
        transition.Play(duration);

        return transition;
    }


    // MARK: build


    private void Build(Sprite sprite, List<Vector4> texelRects, int pixelsPerTile, Material material,
        float duration, float blockDuration, float jitter)
    {
        seed = (uint)Random.Range(int.MinValue, int.MaxValue);
        this.blockDuration = Mathf.Max(0.0001f, blockDuration);

        List<Piece> built = new List<Piece>();
        List<Vector2> uvs = new List<Vector2>();

        Rect slice = sprite.rect;
        float atlasWidth = sprite.texture.width;
        float atlasHeight = sprite.texture.height;
        float block = (float)pixelsPerTile / BLOCKS_PER_SIDE;
        float half = pixelsPerTile * 0.5f;
        float farthest = Mathf.Sqrt(2.0f) * half;

        // blocks in the middle start first and the corners last, with the jitter on top, all fitted
        // inside duration so the last block has exactly blockDuration to finish
        float spread = Mathf.Max(0.0f, duration - blockDuration - jitter);

        for (int i = 0; i < texelRects.Count; i++)
        {
            Vector4 rect = texelRects[i];

            // cut the quad along the block grid so each piece belongs to exactly one block
            for (float y0 = rect.y; y0 < rect.w; )
            {
                float y1 = Mathf.Min(rect.w, (Mathf.Floor(y0 / block) + 1.0f) * block);

                for (float x0 = rect.x; x0 < rect.z; )
                {
                    float x1 = Mathf.Min(rect.z, (Mathf.Floor(x0 / block) + 1.0f) * block);

                    // every piece of one block shares the block's delay, so a block goes as one
                    int blockX = Mathf.FloorToInt(x0 / block);
                    int blockY = Mathf.FloorToInt(y0 / block);
                    Vector2 blockCentre = new Vector2((blockX + 0.5f) * block, (blockY + 0.5f) * block);
                    float radial = Vector2.Distance(blockCentre, new Vector2(half, half)) / farthest;

                    Piece piece = new Piece
                    {
                        min = new Vector3(x0 / pixelsPerTile, 0.0f, y0 / pixelsPerTile),
                        max = new Vector3(x1 / pixelsPerTile, 0.0f, y1 / pixelsPerTile),
                        delay = radial * spread + BlockJitter(blockX, blockY) * jitter,
                    };
                    piece.centre = (piece.min + piece.max) * 0.5f;
                    built.Add(piece);

                    // same winding and UV maths as TerrainTopMeshBuilder / TerrainVisuals.Patch
                    float u0 = (slice.x + x0) / atlasWidth;
                    float u1 = (slice.x + x1) / atlasWidth;
                    float v0 = (slice.y + y0) / atlasHeight;
                    float v1 = (slice.y + y1) / atlasHeight;

                    uvs.Add(new Vector2(u0, v0));
                    uvs.Add(new Vector2(u0, v1));
                    uvs.Add(new Vector2(u1, v1));
                    uvs.Add(new Vector2(u1, v0));

                    x0 = x1;
                }

                y0 = y1;
            }
        }

        pieces = built.ToArray();
        vertices = new Vector3[pieces.Length * 4];

        int[] triangles = new int[pieces.Length * 6];
        Vector3[] normals = new Vector3[vertices.Length];

        for (int i = 0; i < pieces.Length; i++)
        {
            int v = i * 4;
            int t = i * 6;

            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;

            for (int corner = 0; corner < 4; corner++) normals[v + corner] = Vector3.up;
        }

        WriteVertices(0.0f);

        mesh = new Mesh();
        mesh.name = "Tile Transition (runtime)";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.SetUVs(0, uvs);
        mesh.triangles = triangles;
        mesh.RecalculateBounds();   // the pieces only ever shrink, so these bounds hold throughout

        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = true;
    }


    // a stable 0..1 per block within one cover, so a block's pieces agree
    private float BlockJitter(int blockX, int blockY)
    {
        unchecked
        {
            uint hash = (uint)(blockX * 73856093) ^ (uint)(blockY * 19349663) ^ seed;
            hash ^= hash >> 13;
            hash *= 0x5bd1e995u;
            hash ^= hash >> 15;
            return (hash & 0xFFFF) / 65535.0f;
        }
    }


    // MARK: animation


    private void Play(float duration)
    {
        // one tween drives every block, rather than a tween per block
        DOVirtual.Float(0.0f, duration, duration, WriteAndUpload)
            .SetEase(Ease.Linear)
            .SetLink(gameObject)
            .OnComplete(() => Destroy(gameObject));
    }


    private void WriteAndUpload(float elapsed)
    {
        if (mesh == null) return;

        WriteVertices(elapsed);
        mesh.vertices = vertices;
    }


    private void WriteVertices(float elapsed)
    {
        for (int i = 0; i < pieces.Length; i++)
        {
            Piece piece = pieces[i];

            // slow then quick, and never past 1 so a piece can't grow over a trimmed corner or its
            // neighbour (they sit at the same height and would z-fight)
            float t = Mathf.Clamp01((elapsed - piece.delay) / blockDuration);
            float scale = Mathf.Clamp01(1.0f - DOVirtual.EasedValue(0.0f, 1.0f, t, Ease.InQuad));

            Vector3 min = piece.centre + (piece.min - piece.centre) * scale;
            Vector3 max = piece.centre + (piece.max - piece.centre) * scale;

            int v = i * 4;
            vertices[v] = new Vector3(min.x, 0.0f, min.z);
            vertices[v + 1] = new Vector3(min.x, 0.0f, max.z);
            vertices[v + 2] = new Vector3(max.x, 0.0f, max.z);
            vertices[v + 3] = new Vector3(max.x, 0.0f, min.z);
        }
    }


    // the mesh is a runtime object, so it has to go with the cover
    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
