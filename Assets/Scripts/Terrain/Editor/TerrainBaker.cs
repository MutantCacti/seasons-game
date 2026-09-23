using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Runs the terrain bake and writes the result as shipped mesh assets
public static class TerrainBaker
{
    private const string TOPS_SUFFIX = "_Tops";
    private const string WALLS_SUFFIX = "_Walls";
    private const string COLLISION_SUFFIX = "_Collision";
    private const string QUAD_MAP_SUFFIX = "_Quads";


    public static void Bake(TerrainBakeSettings settings)
    {
        List<TerrainManager.Level> levels = TerrainManager.DiscoverLevels(settings.transform);
        if (levels.Count == 0)
        {
            Debug.LogError("No tilemaps with tiles in them were found under the bake settings object", settings);
            return;
        }

        TerrainRaster raster = TerrainRasterizer.Rasterize(levels, settings.pixelsPerTile, settings.alphaThreshold, settings);
        if (raster == null)
        {
            Debug.LogError("Terrain bake produced no levels, probably there are no tiles", settings);
            return;
        }

        // check to make sure the lowest layer is still above the void
        float lowestLevel = raster.levelHeights[0];
        if (settings.voidDepth >= lowestLevel)
        {
            Debug.LogError($"voidDepth ({settings.voidDepth}) must be below the lowest terrain layer ({lowestLevel})", settings);
            return;
        }

        // scan into single-pixel runs that can be looked up from a texture atlas
        TerrainSurfaces render = TerrainSurfaceBuilder.Build(raster, settings.voidDepth, true);
        // scan into single-pixel runs and merge runs of the same length to reduce triangles for collider
        TerrainSurfaces collision = TerrainSurfaceBuilder.Build(raster, settings.voidDepth, false);

        string sceneName = settings.gameObject.scene.name;
        if (string.IsNullOrEmpty(sceneName)) sceneName = "Terrain";

        EnsureFolder(settings.outputFolder);

        // the quad maps are what let a runtime remap patch UVs instead of rebaking, so the
        // builders record where every quad's cell is as they emit it
        TerrainQuadRecorder topRecorder = new TerrainQuadRecorder();
        TerrainQuadRecorder wallRecorder = new TerrainQuadRecorder();

        Mesh topMesh = LoadOrCreateMesh(
            MeshPath(settings, sceneName, TOPS_SUFFIX),
            sceneName + TOPS_SUFFIX,
            mesh => TerrainTopMeshBuilder.Fill(mesh, raster, render, settings, topRecorder)
        );

        Mesh wallMesh = LoadOrCreateMesh(
            MeshPath(settings, sceneName, WALLS_SUFFIX),
            sceneName + WALLS_SUFFIX,
            mesh => TerrainWallMeshBuilder.Fill(mesh, raster, render, settings.wallAtlas, settings, wallRecorder)
        );

        Mesh collisionMesh = LoadOrCreateMesh(
            MeshPath(settings, sceneName, COLLISION_SUFFIX),
            sceneName + COLLISION_SUFFIX,
            mesh => TerrainCollisionMeshBuilder.Fill(mesh, raster, collision)
        );

        // one stamp for both maps: they share the map's occupancy and sprites, and it is what a load
        // compares against the scene's tilemaps to catch a map that was edited, or whose tiles were
        // re-sliced or reassigned, without re-baking
        uint visualHash = TerrainManager.VisualHash(levels);

        TerrainQuadMap topQuadMap = LoadOrCreateQuadMap(
            QuadMapPath(settings, sceneName, TOPS_SUFFIX),
            sceneName + TOPS_SUFFIX + QUAD_MAP_SUFFIX,
            map => TerrainQuadMapBuilder.Fill(map, topRecorder, topMesh, settings.pixelsPerTile,
                TerrainAtlasValidation.FindTileAtlas(raster), visualHash)
        );

        TerrainQuadMap wallQuadMap = LoadOrCreateQuadMap(
            QuadMapPath(settings, sceneName, WALLS_SUFFIX),
            sceneName + WALLS_SUFFIX + QUAD_MAP_SUFFIX,
            map => TerrainQuadMapBuilder.Fill(map, wallRecorder, wallMesh, settings.pixelsPerTile,
                settings.wallAtlas, visualHash)
        );

        // baking shouldn't flip which of the two views you are looking at, so work out
        // what is on screen right now and leave it that way
        bool showBaked = !AnyTilemapRendererEnabled(settings);

        MeshRenderer topRenderer = WireRenderMesh(settings, "Terrain Tops", topMesh, settings.topMaterial, showBaked);
        MeshRenderer wallRenderer = WireRenderMesh(settings, "Terrain Walls", wallMesh, settings.wallMaterial, showBaked);
        WireCollision(settings, collisionMesh);
        WireRuntimeVisibility(settings, new MeshRenderer[] { topRenderer, wallRenderer });
        WireQuadMaps(settings, new TerrainQuadMap[] { topQuadMap, wallQuadMap });

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);

        Debug.Log($"Terrain baked successfully (render: {topMesh.vertexCount + wallMesh.vertexCount} vertices, " +
            $"collision: {collisionMesh.vertexCount} vertices, quad maps: " +
            $"{topQuadMap.CellCount + wallQuadMap.CellCount} cells / {topQuadMap.QuadCount + wallQuadMap.QuadCount} quads)\n");
    }


    private static string MeshPath(TerrainBakeSettings settings, string sceneName, string suffix)
    {
        return Path.Combine(settings.outputFolder, sceneName + suffix + ".asset").Replace('\\', '/');
    }


    private static Mesh LoadOrCreateMesh(string path, string name, System.Action<Mesh> fill)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = mesh == null;

        if (isNew) mesh = new Mesh();
        mesh.name = name;

        fill(mesh);

        if (isNew) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);

        return mesh;
    }


    private static string QuadMapPath(TerrainBakeSettings settings, string sceneName, string suffix)
    {
        return Path.Combine(settings.outputFolder, sceneName + suffix + QUAD_MAP_SUFFIX + ".asset").Replace('\\', '/');
    }


    private static TerrainQuadMap LoadOrCreateQuadMap(string path, string name, System.Action<TerrainQuadMap> fill)
    {
        TerrainQuadMap map = AssetDatabase.LoadAssetAtPath<TerrainQuadMap>(path);
        bool isNew = map == null;

        if (isNew) map = ScriptableObject.CreateInstance<TerrainQuadMap>();
        map.name = name;

        fill(map);

        if (isNew) AssetDatabase.CreateAsset(map, path);
        else EditorUtility.SetDirty(map);

        return map;
    }


    private static MeshRenderer WireRenderMesh(TerrainBakeSettings settings, string childName, Mesh mesh, Material material, bool showBaked)
    {
        Transform holder = GetOrCreateChild(settings.transform, childName);

        MeshFilter filter = GetOrAddComponent<MeshFilter>(holder.gameObject);
        MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(holder.gameObject);

        filter.sharedMesh = mesh;

        if (material != null) meshRenderer.sharedMaterial = material;
        else Debug.LogWarning($"No material assigned for '{childName}' - it will render as the magenta default", settings);

        // tilemaps and meshes sit at the same height, so only ever one of them renders
        Undo.RecordObject(meshRenderer, "Bake Terrain");
        meshRenderer.enabled = showBaked;

        return meshRenderer;
    }


    // the tilemaps are the authoring view and always exist, so they decide which view is up
    private static bool AnyTilemapRendererEnabled(TerrainBakeSettings settings)
    {
        foreach (TilemapRenderer tilemapRenderer in settings.GetComponentsInChildren<TilemapRenderer>(true))
        {
            if (tilemapRenderer.enabled) return true;
        }

        return false;
    }


    private static void WireCollision(TerrainBakeSettings settings, Mesh collisionMesh)
    {
        Transform holder = GetOrCreateChild(settings.transform, "Terrain Collision");

        MeshCollider collider = GetOrAddComponent<MeshCollider>(holder.gameObject);

        collider.sharedMesh = null;
        collider.sharedMesh = collisionMesh;

        int layer = LayerMask.NameToLayer(settings.collisionLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"Layer '{settings.collisionLayerName}' does not exist - add it in Tags and Layers", settings);
        }
        else
        {
            holder.gameObject.layer = layer;
        }
    }


    private static void WireRuntimeVisibility(TerrainBakeSettings settings, MeshRenderer[] bakedRenderers)
    {
        TerrainRuntimeVisibility visibility = GetOrAddComponent<TerrainRuntimeVisibility>(settings.gameObject);

        Undo.RecordObject(visibility, "Bake Terrain");
        visibility.SetBakedRenderers(bakedRenderers);
        EditorUtility.SetDirty(visibility);
    }


    private static void WireQuadMaps(TerrainBakeSettings settings, TerrainQuadMap[] quadMaps)
    {
        TerrainVisuals visuals = GetOrAddComponent<TerrainVisuals>(settings.gameObject);

        Undo.RecordObject(visuals, "Bake Terrain");
        visuals.SetQuadMaps(quadMaps);
        EditorUtility.SetDirty(visuals);

        // also point the grid at it, so the link shows in the inspector instead of only working
        // through Awake's GetComponent fallback
        TerrainManager grid = settings.GetComponent<TerrainManager>();
        if (grid == null) return;

        Undo.RecordObject(grid, "Bake Terrain");
        grid.SetVisuals(visuals);
        EditorUtility.SetDirty(grid);
    }


    // NB: Grid parent has a 90 degree rotation because of 2D tilemap in 3D scene weirdness
    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);

        if (child == null)
        {
            GameObject childGO = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(childGO, "Bake Terrain");
            child = childGO.transform;
            child.SetParent(parent, false);
        }

        child.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        child.localScale = Vector3.one;

        return child;
    }


    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null) component = Undo.AddComponent<T>(target);

        return component;
    }


    private static void EnsureFolder(string folder)
    {
        folder = folder.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string path = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = path + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, parts[i]);
            path = next;
        }
    }
}
