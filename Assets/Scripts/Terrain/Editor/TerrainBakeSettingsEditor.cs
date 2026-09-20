using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[CustomEditor(typeof(TerrainBakeSettings))]
public class TerrainBakeSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainBakeSettings settings = (TerrainBakeSettings)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Bake Terrain"))
        {
            TerrainBaker.Bake(settings);
        }

        if (GUILayout.Button("Toggle Renderers"))
        {
            ToggleRenderers(settings);
        }
    }


    // toggles tilemap renderers and mesh renderers so only one renders at a time
    // at runtime only meshes render, in editor we can look at the tilemaps to help build the map
    private static void ToggleRenderers(TerrainBakeSettings settings)
    {
        TerrainRuntimeVisibility visibility = settings.GetComponent<TerrainRuntimeVisibility>();
        if (visibility == null || visibility.BakedRenderers == null)
        {
            Debug.LogWarning("You need to bake the terrain first to render it", settings);
            return;
        }

        bool showBaked = false;
        foreach (MeshRenderer bakedRenderer in visibility.BakedRenderers)
        {
            if (bakedRenderer != null && !bakedRenderer.enabled) showBaked = true;
        }

        foreach (MeshRenderer bakedRenderer in visibility.BakedRenderers)
        {
            if (bakedRenderer == null) continue;

            Undo.RecordObject(bakedRenderer, "Toggle Terrain Renderers");
            bakedRenderer.enabled = showBaked;
        }

        foreach (TilemapRenderer tilemapRenderer in settings.GetComponentsInChildren<TilemapRenderer>(true))
        {
            Undo.RecordObject(tilemapRenderer, "Toggle Terrain Renderers");
            tilemapRenderer.enabled = !showBaked;
        }
    }
}
