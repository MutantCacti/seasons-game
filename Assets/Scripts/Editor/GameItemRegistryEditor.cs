using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-Flash


[CustomEditor(typeof(GameItemRegistry))]
public class GameItemRegistryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GameItemRegistry registry = (GameItemRegistry)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Gather All Game Items In Project"))
        {
            GatherAllItems(registry);
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


    private static void GatherAllItems(GameItemRegistry registry)
    {
        List<GameItem> found = FindAllItems();

        // deterministic order
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(registry, "Gather Game Items");

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("items");

        list.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        serialized.ApplyModifiedProperties();

        registry.Rebuild();

        Debug.Log($"GameItemRegistry: gathered {found.Count} game items", registry);
    }


    private static void AssignMissingIds(GameItemRegistry registry)
    {
        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("items");

        HashSet<string> taken = new HashSet<string>();
        List<GameItem> missing = new List<GameItem>();

        for (int i = 0; i < list.arraySize; i++)
        {
            GameItem item = list.GetArrayElementAtIndex(i).objectReferenceValue as GameItem;
            if (item == null) continue;

            if (string.IsNullOrEmpty(item.Id)) missing.Add(item);
            else taken.Add(item.Id);
        }

        foreach (GameItem item in missing)
        {
            string id = MakeUnique(Slug(item.name), taken);

            // GameItem.Id is a read-only property, so the private backing field is the only way in
            Undo.RecordObject(item, "Assign Game Item Id");

            SerializedObject serializedItem = new SerializedObject(item);
            serializedItem.FindProperty("id").stringValue = id;
            serializedItem.ApplyModifiedProperties();

            EditorUtility.SetDirty(item);

            taken.Add(id);
            Debug.Log($"GameItemRegistry: assigned id '{id}' to '{item.name}'", item);
        }

        registry.Rebuild();

        if (missing.Count == 0) Debug.Log("GameItemRegistry: no missing ids", registry);
    }


    private static void Validate(GameItemRegistry registry)
    {
        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("items");

        Dictionary<string, GameItem> seen = new Dictionary<string, GameItem>();
        int problems = 0;

        for (int i = 0; i < list.arraySize; i++)
        {
            GameItem item = list.GetArrayElementAtIndex(i).objectReferenceValue as GameItem;

            if (item == null)
            {
                Debug.LogError($"GameItemRegistry: entry {i} is empty", registry);
                problems++;
                continue;
            }

            if (string.IsNullOrEmpty(item.Id))
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' has no Id", item);
                problems++;
                continue;
            }

            if (seen.TryGetValue(item.Id, out GameItem other))
            {
                Debug.LogError($"GameItemRegistry: duplicate Id '{item.Id}' on '{item.name}' " +
                    $"and '{other.name}'", item);
                problems++;
                continue;
            }

            seen.Add(item.Id, item);
        }

        // A GameItem that exists in the project but was forgotten here can never be saved, because
        // the registry is the only thing that resolves an id back to an asset
        HashSet<GameItem> registered = new HashSet<GameItem>(seen.Values);

        foreach (GameItem item in FindAllItems())
        {
            if (registered.Contains(item)) continue;

            Debug.LogError($"GameItemRegistry: '{item.name}' is a GameItem in the project " +
                $"but is not in this registry", item);
            problems++;
        }

        if (problems == 0)
        {
            Debug.Log($"GameItemRegistry: {seen.Count} items, all ids present and unique", registry);
        }
        else
        {
            Debug.LogError($"GameItemRegistry: {problems} problem(s) found", registry);
        }
    }


    // MARK: helpers


    private static List<GameItem> FindAllItems()
    {
        List<GameItem> found = new List<GameItem>();

        foreach (string guid in AssetDatabase.FindAssets("t:GameItem"))
        {
            GameItem item = AssetDatabase.LoadAssetAtPath<GameItem>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) found.Add(item);
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

        return builder.Length > 0 ? builder.ToString() : "item";
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
