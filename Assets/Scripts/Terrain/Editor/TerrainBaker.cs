using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs the terrain bake and writes the result as shipped mesh assets
public static class TerrainBaker
{
    private const string TOPS_SUFFIX = "_Tops";
    private const string WALLS_SUFFIX = "_Walls";
    private const string COLLISION_SUFFIX = "_Collision";


    public static void Bake(TerrainBakeSettings settings)
    {
        TerrainLayer[] terrainLayers = settings.GetComponentsInChildren<TerrainLayer>();
        if (terrainLayers.Length == 0)
        {
            Debug.LogError("No TerrainLayer components found under the bake settings object", settings);
            return;
        }

        TerrainRaster raster = TerrainRasterizer.Rasterize(terrainLayers, settings.pixelsPerTile, settings.alphaThreshold, settings);
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

        Mesh topMesh = LoadOrCreateMesh(
            MeshPath(settings, sceneName, TOPS_SUFFIX),
            sceneName + TOPS_SUFFIX,
            mesh => TerrainTopMeshBuilder.Fill(mesh, raster, render, settings)
        );

        Mesh wallMesh = LoadOrCreateMesh(
            MeshPath(settings, sceneName, WALLS_SUFFIX),
            sceneName + WALLS_SUFFIX,
            mesh => TerrainWallMeshBuilder.Fill(mesh, raster, render, settings.wallAtlas, settings)
        );

        Mesh collisionMesh = LoadOrCreateMesh(
            MeshPath(settings, sceneName, COLLISION_SUFFIX),
            sceneName + COLLISION_SUFFIX,
            mesh => TerrainCollisionMeshBuilder.Fill(mesh, raster, collision)
        );

        MeshRenderer topRenderer = WireRenderMesh(settings, "Terrain Tops", topMesh, settings.topMaterial);
        MeshRenderer wallRenderer = WireRenderMesh(settings, "Terrain Walls", wallMesh, settings.wallMaterial);
        WireCollision(settings, collisionMesh);
        WireRuntimeVisibility(settings, new MeshRenderer[] { topRenderer, wallRenderer });

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);

        Debug.Log($"Terrain baked successfully (render: {topMesh.vertexCount + wallMesh.vertexCount} vertices, collision: {collisionMesh.vertexCount} vertices)\n");
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


    private static MeshRenderer WireRenderMesh(TerrainBakeSettings settings, string childName, Mesh mesh, Material material)
    {
        Transform holder = GetOrCreateChild(settings.transform, childName);

        MeshFilter filter = GetOrAddComponent<MeshFilter>(holder.gameObject);
        MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(holder.gameObject);

        filter.sharedMesh = mesh;

        if (material != null) meshRenderer.sharedMaterial = material;
        else Debug.LogWarning($"No material assigned for '{childName}' - it will render as the magenta default", settings);

        // this will later be toggled by TerrainBakeSettingsEditor:ToggleRenderers
        meshRenderer.enabled = false;

        return meshRenderer;
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
