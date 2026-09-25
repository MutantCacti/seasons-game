using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-V41-Flash


[CustomEditor(typeof(TerrainTileRegistry))]
public class TerrainTileRegistryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainTileRegistry registry = (TerrainTileRegistry)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Gather All Terrain Tiles In Project"))
        {
            GatherAllTiles(registry);
        }

        if (GUILayout.Button("Assign Missing Ids"))
        {
            AssignMissingIds(registry);
        }

        if (GUILayout.Button("Validate"))
        {
            Validate(registry);
        }
    }


    // MARK: actions


    internal static void GatherAllTiles(TerrainTileRegistry registry)
    {
        if (registry == null)
        {
            Debug.LogError("TerrainTileRegistryEditor: no registry to gather into, so nothing was gathered");
            return;
        }

        List<TerrainTile> found = FindAllTiles();

        // deterministic order
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(registry, "Gather Terrain Tiles");

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("tiles");

        list.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        serialized.ApplyModifiedProperties();

        registry.Rebuild();

        Debug.Log($"TerrainTileRegistry: gathered {found.Count} terrain tiles", registry);
    }


    internal static void AssignMissingIds(TerrainTileRegistry registry)
    {
        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("tiles");

        HashSet<string> taken = new HashSet<string>();
        List<TerrainTile> missing = new List<TerrainTile>();

        for (int i = 0; i < list.arraySize; i++)
        {
            TerrainTile tile = list.GetArrayElementAtIndex(i).objectReferenceValue as TerrainTile;
            if (tile == null) continue;

            if (string.IsNullOrEmpty(tile.Id)) missing.Add(tile);
            else taken.Add(tile.Id);
        }

        foreach (TerrainTile tile in missing)
        {
            string id = MakeUnique(Slug(tile.name), taken);

            Undo.RecordObject(tile, "Assign Terrain Tile Id");
            tile.Id = id;
            EditorUtility.SetDirty(tile);

            taken.Add(id);
            Debug.Log($"TerrainTileRegistry: assigned id '{id}' to '{tile.name}'", tile);
        }

        registry.Rebuild();

        if (missing.Count == 0) Debug.Log("TerrainTileRegistry: no missing ids", registry);
    }


    internal static int Validate(TerrainTileRegistry registry)
    {
        if (registry == null)
        {
            Debug.LogError("TerrainTileRegistryEditor: no registry to validate, so nothing was validated");
            return 1;
        }

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("tiles");

        Dictionary<string, TerrainTile> seen = new Dictionary<string, TerrainTile>();
        int problems = 0;

        for (int i = 0; i < list.arraySize; i++)
        {
            TerrainTile tile = list.GetArrayElementAtIndex(i).objectReferenceValue as TerrainTile;

            if (tile == null)
            {
                Debug.LogError($"TerrainTileRegistry: entry {i} is empty", registry);
                problems++;
                continue;
            }

            if (string.IsNullOrEmpty(tile.Id))
            {
                Debug.LogError($"TerrainTileRegistry: '{tile.name}' has no Id", tile);
                problems++;
                continue;
            }

            if (seen.TryGetValue(tile.Id, out TerrainTile other))
            {
                Debug.LogError($"TerrainTileRegistry: duplicate Id '{tile.Id}' on '{tile.name}' " +
                    $"and '{other.name}'", tile);
                problems++;
                continue;
            }

            seen.Add(tile.Id, tile);
        }

        // A TerrainTile that exists in the project but was forgotten here can never be saved,
        // because the registry is the only thing that resolves an id back to an asset
        HashSet<TerrainTile> registered = new HashSet<TerrainTile>(seen.Values);

        foreach (TerrainTile tile in FindAllTiles())
        {
            if (registered.Contains(tile)) continue;

            Debug.LogError($"TerrainTileRegistry: '{tile.name}' is a TerrainTile in the project " +
                $"but is not in this registry", tile);
            problems++;
        }

        if (problems == 0)
        {
            Debug.Log($"TerrainTileRegistry: {seen.Count} tiles, all ids present and unique", registry);
        }
        else
        {
            Debug.LogError($"TerrainTileRegistry: {problems} problem(s) found", registry);
        }

        return problems;
    }


    // MARK: helpers


    private static List<TerrainTile> FindAllTiles()
    {
        List<TerrainTile> found = new List<TerrainTile>();

        foreach (string guid in AssetDatabase.FindAssets("t:TerrainTile"))
        {
            TerrainTile tile = AssetDatabase.LoadAssetAtPath<TerrainTile>(AssetDatabase.GUIDToAssetPath(guid));
            if (tile != null) found.Add(tile);
        }

        return found;
    }


    // "Spring Grass" becomes "spring_grass"
    private static string Slug(string name)
    {
        StringBuilder builder = new StringBuilder(name.Length);

        for (int i = 0; i < name.Length; i++)
        {
            char c = char.ToLowerInvariant(name[i]);

            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                builder.Append(c);
            }
            // runs of separators collapse to a single underscore, and never lead with one
            else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
            {
                builder.Append('_');
            }
        }

        while (builder.Length > 0 && builder[builder.Length - 1] == '_')
        {
            builder.Length--;
        }

        return builder.Length > 0 ? builder.ToString() : "tile";
    }


    private static string MakeUnique(string baseId, HashSet<string> taken)
    {
        if (!taken.Contains(baseId)) return baseId;

        for (int suffix = 2; ; suffix++)
        {
            string candidate = baseId + "_" + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}