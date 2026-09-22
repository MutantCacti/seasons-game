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

    private bool initialised;
    private uint visualHash;
    private bool hashKnown;
    private TerrainRuntimeVisibility visibility;


    public TerrainQuadMap[] QuadMaps { get { return quadMaps; } }


    // called by the bake
    public void SetQuadMaps(TerrainQuadMap[] maps)
    {
        quadMaps = maps;
        initialised = false;
    }


    // Called by TerrainGrid once it has built its state, because that is the only thing that
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
            if (ok) mesh.SetUVs(0, meshUvs);
        }
    }


    // Lazy. it does not matter whether this runs before or after TerrainGrid's Awake
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
