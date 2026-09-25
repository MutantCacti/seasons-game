using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(GlobalOptionsRegistry))]
public class GlobalOptionsRegistryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GlobalOptionsRegistry registry = (GlobalOptionsRegistry)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Gather All Global Options In Project"))
        {
            GatherAllOptions(registry);
        }

        if (GUILayout.Button("Validate"))
        {
            Validate(registry);
        }
    }


    // MARK: actions


    internal static void GatherAllOptions(GlobalOptionsRegistry registry)
    {
        if (registry == null)
        {
            Debug.LogError("GlobalOptionsRegistryEditor: no registry to gather into, so nothing was gathered");
            return;
        }

        List<GlobalOption> found = FindAllOptions();

        // idempotent: a plain A->Z sort by asset name, so gathering twice changes nothing
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(registry, "Gather Global Options");

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("options");

        list.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        serialized.ApplyModifiedProperties();

        registry.Rebuild();

        Debug.Log($"GlobalOptionsRegistry: gathered {found.Count} options", registry);
    }


    internal static int Validate(GlobalOptionsRegistry registry)
    {
        if (registry == null)
        {
            Debug.LogError("GlobalOptionsRegistryEditor: no registry to validate, so nothing was validated");
            return 1;
        }

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("options");

        Dictionary<string, GlobalOption> seen = new Dictionary<string, GlobalOption>();
        int problems = 0;

        for (int i = 0; i < list.arraySize; i++)
        {
            GlobalOption option = list.GetArrayElementAtIndex(i).objectReferenceValue as GlobalOption;

            if (option == null)
            {
                Debug.LogError($"GlobalOptionsRegistry: entry {i} is empty", registry);
                problems++;
                continue;
            }

            if (string.IsNullOrEmpty(option.Id))
            {
                Debug.LogError($"GlobalOptionsRegistry: '{option.name}' has no Id - an id must be an exposed mixer parameter, so assign it by hand", option);
                problems++;
                continue;
            }

            if (seen.TryGetValue(option.Id, out GlobalOption other))
            {
                Debug.LogError($"GlobalOptionsRegistry: duplicate Id '{option.Id}' on '{option.name}' " +
                    $"and '{other.name}'", option);
                problems++;
                continue;
            }

            seen.Add(option.Id, option);
        }

        // A GlobalOption that exists in the project but was forgotten here can never be loaded,
        // because the registry is the only thing that resolves an id back to an asset
        HashSet<GlobalOption> registered = new HashSet<GlobalOption>(seen.Values);

        foreach (GlobalOption option in FindAllOptions())
        {
            if (registered.Contains(option)) continue;

            Debug.LogError($"GlobalOptionsRegistry: '{option.name}' is a GlobalOption in the project " +
                $"but is not in this registry", option);
            problems++;
        }

        if (problems == 0)
        {
            Debug.Log($"GlobalOptionsRegistry: {seen.Count} options, all ids present and unique", registry);
        }
        else
        {
            Debug.LogError($"GlobalOptionsRegistry: {problems} problem(s) found", registry);
        }

        return problems;
    }


    // MARK: helpers


    private static List<GlobalOption> FindAllOptions()
    {
        List<GlobalOption> found = new List<GlobalOption>();

        foreach (string guid in AssetDatabase.FindAssets("t:GlobalOption"))
        {
            GlobalOption option = AssetDatabase.LoadAssetAtPath<GlobalOption>(AssetDatabase.GUIDToAssetPath(guid));
            if (option != null) found.Add(option);
        }

        return found;
    }
}
