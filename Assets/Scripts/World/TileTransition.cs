using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;


// A one shot cover over a cell whose tile just changed. It starts as the exact pixels of the old tile,
// cut to the cell's own baked top quads so a trimmed edge never flashes a full square. Then the new
// tile grows over it in 4px blocks, middle first: each block scales up out of its edge facing the middle,
// so it looks like it's pushing out of the block before it. The remap already happened underneath, so
// once every block is grown the cover matches the terrain exactly and destroys itself. It's never
// registered with EntityManager (so it is never saved and never blocks a placement).
public class TileTransition : MonoBehaviour
{
    // 8x8 blocks, so 4px blocks on a 32px tile
    private const int BLOCKS_PER_SIDE = 8;

    // just under the placement ripple's 0.002 so the ripple draws on top
    private const float COVER_OFFSET = 0.001f;

    // every piece is drawn as three quads that never overlap (they'd z-fight at the same height):
    // the grown new-tile rect, and the old-tile L left over around it (one arm is empty unless the
    // piece grows out of a corner)
    private const int QUADS_PER_PIECE = 3;


    // Every block has grown, so the cover matches the terrain underneath and is about to destroy
    // itself. Raised from the tween alone, never from OnDestroy: a scene unload kills a cover
    // mid-tween, and a listener must not act on a dying scene. Fires at most once
    public event Action Finished;


    // one block, or the part of it that the cell's top quads keep. All in texels inside the slice
    private struct Piece
    {
        public float x0, z0, x1, z1;
        public int growX;   // +1 grows from x0 towards x1, -1 from x1 towards x0, 0 is always full width
        public int growZ;
        public float delay;
    }


    private Mesh mesh;
    private Piece[] pieces;
    private Vector3[] vertices;
    private Vector2[] uvs;
    private float blockDuration;
    private float pixelsPerTile;
    private Vector2 oldOrigin;
    private Vector2 newOrigin;
    private Vector2 atlasSize;
    private uint seed;
    private bool finished;


    // cellOrigin is the cell's (x0, height, z0) corner in world space. texelRects are the cell's top
    // quads as (x0, y0, x1, y1) texels inside the slice. Both sprites must share one atlas (the
    // material's). Returns null when there is nothing to cover
    public static TileTransition Spawn(Vector3 cellOrigin, Sprite oldSprite, Sprite newSprite, List<Vector4> texelRects,
        int pixelsPerTile, Material material, float duration, float blockDuration, float jitter)
    {
        if (oldSprite == null || newSprite == null || material == null) return null;
        if (texelRects == null || texelRects.Count == 0 || pixelsPerTile <= 0) return null;

        GameObject cover = new GameObject("Tile Transition");
        cover.transform.position = cellOrigin + Vector3.up * COVER_OFFSET;

        TileTransition transition = cover.AddComponent<TileTransition>();
        transition.Build(oldSprite, newSprite, texelRects, pixelsPerTile, material, duration, blockDuration, jitter);
        transition.Play(duration);

        return transition;
    }


    // MARK: build


    private void Build(Sprite oldSprite, Sprite newSprite, List<Vector4> texelRects, int pixelsPerTile,
        Material material, float duration, float blockDuration, float jitter)
    {
        seed = (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        this.blockDuration = Mathf.Max(0.0001f, blockDuration);
        this.pixelsPerTile = pixelsPerTile;

        oldOrigin = oldSprite.rect.position;
        newOrigin = newSprite.rect.position;
        atlasSize = new Vector2(oldSprite.texture.width, oldSprite.texture.height);

        float block = (float)pixelsPerTile / BLOCKS_PER_SIDE;
        float farthest = Mathf.Sqrt(2.0f) * (BLOCKS_PER_SIDE - 1);

        // middle blocks start first and the corners last, with the jitter on top, all fitted
        // inside duration so the last block has exactly blockDuration to finish
        float spread = Mathf.Max(0.0f, duration - blockDuration - jitter);

        List<Piece> built = new List<Piece>();

        for (int i = 0; i < texelRects.Count; i++)
        {
            Vector4 rect = texelRects[i];

            // cut the quad along the block grid so each piece belongs to exactly one block
            for (float z0 = rect.y; z0 < rect.w; )
            {
                float z1 = Mathf.Min(rect.w, (Mathf.Floor(z0 / block) + 1.0f) * block);

                for (float x0 = rect.x; x0 < rect.z; )
                {
                    float x1 = Mathf.Min(rect.z, (Mathf.Floor(x0 / block) + 1.0f) * block);

                    int blockX = Mathf.FloorToInt(x0 / block);
                    int blockZ = Mathf.FloorToInt(z0 / block);

                    // the block's offset from the middle in half blocks, so always odd and never 0
                    int offsetX = 2 * blockX + 1 - BLOCKS_PER_SIDE;
                    int offsetZ = 2 * blockZ + 1 - BLOCKS_PER_SIDE;
                    int reachX = Mathf.Abs(offsetX);
                    int reachZ = Mathf.Abs(offsetZ);

                    // grows along whichever axis points at the middle, out of the edge on the middle's
                    // side; a block on a diagonal grows out of its corner that faces the middle
                    built.Add(new Piece
                    {
                        x0 = x0, z0 = z0, x1 = x1, z1 = z1,
                        growX = reachX >= reachZ ? (offsetX > 0 ? 1 : -1) : 0,
                        growZ = reachZ >= reachX ? (offsetZ > 0 ? 1 : -1) : 0,
                        delay = Mathf.Sqrt(reachX * reachX + reachZ * reachZ - 2) / farthest * spread
                            + BlockJitter(blockX, blockZ) * jitter,
                    });

                    x0 = x1;
                }

                z0 = z1;
            }
        }

        pieces = built.ToArray();

        int quadCount = pieces.Length * QUADS_PER_PIECE;
        vertices = new Vector3[quadCount * 4];
        uvs = new Vector2[quadCount * 4];

        int[] triangles = new int[quadCount * 6];
        Vector3[] normals = new Vector3[vertices.Length];

        // same winding as TerrainTopMeshBuilder
        for (int quad = 0; quad < quadCount; quad++)
        {
            int v = quad * 4;
            int t = quad * 6;

            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;

            for (int corner = 0; corner < 4; corner++) normals[v + corner] = Vector3.up;
        }

        Write(0.0f);

        mesh = new Mesh();
        mesh.name = "Tile Transition (runtime)";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();   // every quad stays inside its piece, so these bounds hold throughout

        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = true;
    }


    // a stable 0..1 per block within one cover, so a block's pieces agree
    private float BlockJitter(int blockX, int blockZ)
    {
        unchecked
        {
            uint hash = (uint)(blockX * 73856093) ^ (uint)(blockZ * 19349663) ^ seed;
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
            .OnComplete(Finish);
    }


    // the one completion, and it never looks back: the event is dropped so a late subscriber cannot
    // fire it, and the guard covers a stray second OnComplete
    private void Finish()
    {
        if (finished) return;

        finished = true;

        Action listeners = Finished;
        Finished = null;

        if (listeners != null) listeners();

        Destroy(gameObject);
    }


    private void WriteAndUpload(float elapsed)
    {
        if (mesh == null) return;

        Write(elapsed);
        mesh.vertices = vertices;
        mesh.uv = uvs;
    }


    private void Write(float elapsed)
    {
        for (int i = 0; i < pieces.Length; i++)
        {
            Piece piece = pieces[i];

            // quick then settling, and never past 1 so a block can't grow over a trimmed corner or
            // into its neighbour
            float t = Mathf.Clamp01((elapsed - piece.delay) / blockDuration);
            float grown = Mathf.Clamp01(DOVirtual.EasedValue(0.0f, 1.0f, t, Ease.OutQuad));

            // the grown rect, pinned to the piece's middle-facing edge (or corner)
            float gx0 = piece.x0, gx1 = piece.x1, gz0 = piece.z0, gz1 = piece.z1;

            if (piece.growX > 0) gx1 = Mathf.Lerp(piece.x0, piece.x1, grown);
            else if (piece.growX < 0) gx0 = Mathf.Lerp(piece.x1, piece.x0, grown);

            if (piece.growZ > 0) gz1 = Mathf.Lerp(piece.z0, piece.z1, grown);
            else if (piece.growZ < 0) gz0 = Mathf.Lerp(piece.z1, piece.z0, grown);

            int quad = i * QUADS_PER_PIECE;

            // the new tile scales up: the whole block's texels squashed into the grown rect
            WriteQuad(quad, gx0, gz0, gx1, gz1, newOrigin, piece.x0, piece.z0, piece.x1, piece.z1);

            // the old tile stays put and is just cropped: the full-width strip beyond the grown rect
            // in z, then the rest of the grown rect's rows beyond it in x
            float oz0 = gz0 > piece.z0 ? piece.z0 : gz1;
            float oz1 = gz0 > piece.z0 ? gz0 : piece.z1;
            WriteQuad(quad + 1, piece.x0, oz0, piece.x1, oz1, oldOrigin, piece.x0, oz0, piece.x1, oz1);

            float ox0 = gx0 > piece.x0 ? piece.x0 : gx1;
            float ox1 = gx0 > piece.x0 ? gx0 : piece.x1;
            WriteQuad(quad + 2, ox0, gz0, ox1, gz1, oldOrigin, ox0, gz0, ox1, gz1);
        }
    }


    // corners in texels, drawn as the slice's (u0, v0)..(u1, v1) texels. An empty rect is a degenerate
    // quad, which draws nothing
    private void WriteQuad(int quad, float x0, float z0, float x1, float z1,
        Vector2 slice, float u0, float v0, float u1, float v1)
    {
        int v = quad * 4;

        vertices[v] = new Vector3(x0 / pixelsPerTile, 0.0f, z0 / pixelsPerTile);
        vertices[v + 1] = new Vector3(x0 / pixelsPerTile, 0.0f, z1 / pixelsPerTile);
        vertices[v + 2] = new Vector3(x1 / pixelsPerTile, 0.0f, z1 / pixelsPerTile);
        vertices[v + 3] = new Vector3(x1 / pixelsPerTile, 0.0f, z0 / pixelsPerTile);

        // same UV maths as TerrainTopMeshBuilder / TerrainVisuals.Patch
        Vector2 uv0 = new Vector2((slice.x + u0) / atlasSize.x, (slice.y + v0) / atlasSize.y);
        Vector2 uv1 = new Vector2((slice.x + u1) / atlasSize.x, (slice.y + v1) / atlasSize.y);

        uvs[v] = new Vector2(uv0.x, uv0.y);
        uvs[v + 1] = new Vector2(uv0.x, uv1.y);
        uvs[v + 2] = new Vector2(uv1.x, uv1.y);
        uvs[v + 3] = new Vector2(uv1.x, uv0.y);
    }


    // the mesh is a runtime object, so it has to go with the cover
    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
