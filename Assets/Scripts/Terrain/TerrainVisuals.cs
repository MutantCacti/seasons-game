using System.Collections.Generic;
using UnityEngine;


// The meshes are project assets and modifying sharedMesh in editor play mode messes up the asset,
// so every mesh is copied once and the copy is what gets patched
public class TerrainVisuals : MonoBehaviour
{
    [SerializeField] private TerrainQuadMap[] quadMaps;


    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<MeshFilter> filters = new List<MeshFilter>();
    private readonly List<Vector2[]> uvs = new List<Vector2[]>();
    private readonly List<bool> patchable = new List<bool>();

    // batched patching: the queued cells, the meshes with unflushed writes, and scratch buffers so
    // a flush allocates nothing after the first one. Reused between batches
    private readonly List<Vector3Int> deferredCells = new List<Vector3Int>();
    private readonly List<Sprite> deferredSprites = new List<Sprite>();
    private readonly List<Vector3Int> scratchCells = new List<Vector3Int>();
    private readonly List<Sprite> scratchSprites = new List<Sprite>();
    private readonly List<int> dirtyMeshes = new List<int>();

    private bool initialised;
    private uint visualHash;
    private bool hashKnown;
    private TerrainRuntimeVisibility visibility;


    public TerrainQuadMap[] QuadMaps { get { return quadMaps; } }

    // TerrainBaker always wires the maps as { tops, walls }, so the first one is the tops
    public TerrainQuadMap TopQuadMap { get { return quadMaps != null && quadMaps.Length > 0 ? quadMaps[0] : null; } }


    // called by the bake
    public void SetQuadMaps(TerrainQuadMap[] maps)
    {
        quadMaps = maps;
        initialised = false;
    }


    // Called by TerrainManager once it has built its state, because that is the only thing that
    // knows whether the scene's tilemaps still match what was baked
    public void Validate(uint currentVisualHash)
    {
        visualHash = currentVisualHash;
        hashKnown = true;

        EnsureInitialised();
    }


    public void Patch(Vector3Int cell, Sprite sprite)
    {
        if (sprite == null) return;

        EnsureInitialised();
        if (quadMaps == null) return;

        // the immediate path shares the batched writer and its dirty list, then uploads straight away
        dirtyMeshes.Clear();

        WriteCellUVs(cell, sprite, dirtyMeshes);

        for (int i = 0; i < dirtyMeshes.Count; i++)
        {
            int map = dirtyMeshes[i];
            if (meshes[map] != null) meshes[map].SetUVs(0, uvs[map]);
        }
    }


    // A batch: writes the cell's UVs into the cached buffer but uploads nothing. FlushDeferred is
    // what touches the GPU, once per dirty mesh rather than once per cell
    public void PatchDeferred(Vector3Int cell, Sprite sprite)
    {
        if (sprite == null) return;

        deferredCells.Add(cell);
        deferredSprites.Add(sprite);
    }


    // Ends a batch. One whole-mesh SetUVs per dirty mesh, which is the bounded cost the flood needs:
    // a level of cells costs at most one upload per mesh, whatever the level's size.
    //
    // Mesh.SetUVs' (start, length) overload cannot patch a subrange: the docs say the resulting mesh
    // has `length` vertices, so it takes a slice of the input array rather than of the mesh, and
    // passing a level's vertex range would shrink the terrain to that level. Whole-mesh it is.
    //
    // The queue is emptied before anything can early-return or upload, so nothing deferred is
    // flushed twice and a scene with no baked visuals cannot accumulate cells forever
    public void FlushDeferred()
    {
        if (deferredCells.Count == 0) return;

        // take the cells away first: the rest of this can return early without stranding them
        scratchCells.Clear();
        scratchSprites.Clear();

        scratchCells.AddRange(deferredCells);
        scratchSprites.AddRange(deferredSprites);

        int count = scratchCells.Count;

        deferredCells.Clear();
        deferredSprites.Clear();

        EnsureInitialised();
        if (quadMaps == null) return;

        dirtyMeshes.Clear();

        for (int i = 0; i < count; i++)
        {
            WriteCellUVs(scratchCells[i], scratchSprites[i], dirtyMeshes);
        }

        for (int i = 0; i < dirtyMeshes.Count; i++)
        {
            int map = dirtyMeshes[i];
            if (meshes[map] != null) meshes[map].SetUVs(0, uvs[map]);
        }
    }


    // Writes the cell's quads into the cached UV array, for the maps that have any, and marks those
    // maps as needing an upload. Shared by the immediate and the deferred paths so they cannot
    // disagree about what a cell resolves to
    private void WriteCellUVs(Vector3Int cell, Sprite sprite, List<int> dirty)
    {
        // the wall atlas is laid out the same as the tile atlas, so one slice rect describes the
        // quad in both meshes and the sprite's own texture gives the right dimensions for either
        // TODO: change the way that works because holy shit that's gonna be impossible
        Rect slice = sprite.rect;
        float atlasWidth = sprite.texture.width;
        float atlasHeight = sprite.texture.height;

        for (int i = 0; i < quadMaps.Length; i++)
        {
            if (!patchable[i]) continue;

            TerrainQuadMap map = quadMaps[i];
            if (!map.TryGetQuads(cell, out int first, out int count)) continue;

            Mesh mesh = meshes[i];
            Vector2[] meshUvs = uvs[i];
            bool ok = true;

            for (int quad = 0; quad < count; quad++)
            {
                int row = first + quad;
                int vertexStart = map.quadVertexStart[row];

                if (vertexStart < 0 || vertexStart + 3 >= meshUvs.Length)
                {
                    Debug.LogError($"TerrainVisuals: '{map.name}' points at vertex {vertexStart} but " +
                        $"'{mesh.name}' has {meshUvs.Length} - the map and mesh are not a pair", map);
                    ok = false;
                    break;
                }

                Vector4 offsets = map.quadTexelOffsets[row];

                float u0 = (slice.x + offsets.x) / atlasWidth;
                float v0 = (slice.y + offsets.y) / atlasHeight;
                float u1 = (slice.x + offsets.z) / atlasWidth;
                float v1 = (slice.y + offsets.w) / atlasHeight;

                meshUvs[vertexStart] = new Vector2(u0, v0);
                meshUvs[vertexStart + 1] = new Vector2(u0, v1);
                meshUvs[vertexStart + 2] = new Vector2(u1, v1);
                meshUvs[vertexStart + 3] = new Vector2(u1, v0);
            }

            patchable[i] = ok;

            if (!ok) continue;
            if (!dirty.Contains(i)) dirty.Add(i);
        }
    }


    // Lazy. it does not matter whether this runs before or after TerrainManager's Awake
    private void EnsureInitialised()
    {
        if (initialised) return;
        initialised = true;

        meshes.Clear();
        uvs.Clear();
        patchable.Clear();

        if (quadMaps == null)
        {
            filters.Clear();
            return;
        }

        if (visibility == null) visibility = GetComponent<TerrainRuntimeVisibility>();

        if (filters.Count != quadMaps.Length) ResolveFilters();

        for (int i = 0; i < quadMaps.Length; i++)
        {
            TerrainQuadMap map = quadMaps[i];

            Mesh mesh = null;
            Vector2[] meshUvs = null;
            bool ok = map != null && map.mesh != null;

            if (!ok)
            {
                Debug.LogError($"TerrainVisuals: quad map {i} is empty or has no mesh", this);
            }
            else if (hashKnown && map.visualHash != visualHash)
            {
                Debug.LogError($"TerrainVisuals: '{map.name}' was baked from a different map, or from " +
                    $"different tile sprites, than the scene's tilemaps now describe - re-bake the " +
                    $"terrain. Remapped tiles will not show until you do", map);
                ok = false;
            }
            else if (map.quadVertexStart == null || map.quadTexelOffsets == null ||
                map.quadVertexStart.Length != map.quadTexelOffsets.Length)
            {
                Debug.LogError($"TerrainVisuals: '{map.name}' has inconsistent quad arrays", map);
                ok = false;
            }

            if (ok)
            {
                // make a copy of the mesh
                mesh = Instantiate(map.mesh);
                mesh.name = map.mesh.name + " (runtime)";

                if (!mesh.isReadable)
                {
                    Debug.LogError($"TerrainVisuals: '{map.mesh.name}' is not readable, so its UVs " +
                        $"cannot be patched - the mesh needs Read/Write enabled", map);
                    ok = false;
                }
                else
                {
                    meshUvs = mesh.uv;
                }
            }

            if (ok && (meshUvs == null || meshUvs.Length != mesh.vertexCount))
            {
                Debug.LogError($"TerrainVisuals: '{mesh.name}' returned {meshUvs?.Length ?? 0} UVs " +
                    $"for {mesh.vertexCount} vertices", map);
                ok = false;
            }

            if (mesh != null && filters[i] != null)
            {
                filters[i].sharedMesh = mesh;
            }

            meshes.Add(mesh);
            uvs.Add(meshUvs);
            patchable.Add(ok);
        }
    }


    private void ResolveFilters()
    {
        filters.Clear();

        if (quadMaps == null) return;
        if (visibility == null || visibility.BakedRenderers == null) return;

        for (int i = 0; i < quadMaps.Length; i++)
        {
            MeshFilter found = null;

            foreach (MeshRenderer renderer in visibility.BakedRenderers)
            {
                if (renderer == null) continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh != quadMaps[i].mesh) continue;

                found = filter;
                break;
            }

            if (found == null)
            {
                Debug.LogWarning($"TerrainVisuals: no baked renderer is using " +
                    $"'{quadMaps[i].mesh.name}', so remaps on it will not be visible", this);
            }

            filters.Add(found);
        }
    }


    // copies are runtime objects, so clean them up
    private void OnDestroy()
    {
        for (int i = 0; i < meshes.Count; i++)
        {
            if (meshes[i] != null) Destroy(meshes[i]);
        }

        meshes.Clear();
        uvs.Clear();
        patchable.Clear();
        filters.Clear();
    }
}
