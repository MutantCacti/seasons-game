using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(AnimalSpawnRuleRegistry))]
public class AnimalSpawnRuleRegistryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        AnimalSpawnRuleRegistry registry = (AnimalSpawnRuleRegistry)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Gather All Spawn Rules In Project"))
        {
            GatherAllRules(registry);
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


    internal static void GatherAllRules(AnimalSpawnRuleRegistry registry)
    {
        if (registry == null)
        {
            Debug.LogError("AnimalSpawnRuleRegistryEditor: no registry to gather into, so nothing was gathered");
            return;
        }

        List<AnimalSpawnRule> found = AnimalRegistryEditor.FindAllRules();

        // idempotent: a plain A->Z sort by asset name, so gathering twice changes nothing. Ordering by
        // the animal's place in the AnimalRegistry was the alternative, but A->Z keeps this registry's
        // gather independent of whether the animal registry has been gathered yet.
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        Undo.RecordObject(registry, "Gather Spawn Rules");

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("rules");

        list.arraySize = found.Count;
        for (int i = 0; i < found.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        serialized.ApplyModifiedProperties();

        registry.Rebuild();

        Debug.Log($"AnimalSpawnRuleRegistry: gathered {found.Count} spawn rules", registry);
    }


    internal static void AssignMissingIds(AnimalSpawnRuleRegistry registry)
    {
        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("rules");

        int assigned = 0;

        for (int i = 0; i < list.arraySize; i++)
        {
            AnimalSpawnRule rule = list.GetArrayElementAtIndex(i).objectReferenceValue as AnimalSpawnRule;
            if (rule == null) continue;

            SerializedObject serializedRule = new SerializedObject(rule);
            SerializedProperty id = serializedRule.FindProperty("id");

            if (!string.IsNullOrEmpty(id.stringValue)) continue;

            // writes the id the rule already reports, which is the asset name while id is empty, so
            // assigning an id never renames a rule: the registry lookup and every log that names it
            // keep matching what they matched before.
            string effective = rule.Id;

            if (string.IsNullOrEmpty(effective))
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: '{rule.name}' has neither an id nor a name, " +
                    $"so no id can be assigned to it", rule);
                continue;
            }

            Undo.RecordObject(rule, "Assign Spawn Rule Id");

            id.stringValue = effective;
            serializedRule.ApplyModifiedProperties();

            EditorUtility.SetDirty(rule);

            assigned++;
            Debug.Log($"AnimalSpawnRuleRegistry: assigned id '{effective}' to '{rule.name}'", rule);
        }

        registry.Rebuild();

        if (assigned == 0) Debug.Log("AnimalSpawnRuleRegistry: no missing ids", registry);
    }


    internal static int Validate(AnimalSpawnRuleRegistry registry)
    {
        if (registry == null)
        {
            // every Validate below logs the registry as its context, and a SerializedObject/Undo call
            // on null throws, so a missing asset has to be reported rather than passed down
            Debug.LogError("AnimalSpawnRuleRegistryEditor: no registry to validate, so nothing was validated");
            return 1;
        }

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("rules");

        Dictionary<string, AnimalSpawnRule> seen = new Dictionary<string, AnimalSpawnRule>();
        int problems = 0;

        for (int i = 0; i < list.arraySize; i++)
        {
            AnimalSpawnRule rule = list.GetArrayElementAtIndex(i).objectReferenceValue as AnimalSpawnRule;

            if (rule == null)
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: entry {i} is empty", registry);
                problems++;
                continue;
            }

            if (string.IsNullOrEmpty(rule.Id))
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: '{rule.name}' has no Id", rule);
                problems++;
                continue;
            }

            if (seen.TryGetValue(rule.Id, out AnimalSpawnRule other))
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: duplicate Id '{rule.Id}' on '{rule.name}' " +
                    $"and '{other.name}'", rule);
                problems++;
                continue;
            }

            seen.Add(rule.Id, rule);
        }

        // A spawn rule that exists in the project but was forgotten here can never fire, because
        // AnimalManager only forwards world events to the rules in this registry
        HashSet<AnimalSpawnRule> registered = new HashSet<AnimalSpawnRule>(seen.Values);

        foreach (AnimalSpawnRule rule in AnimalRegistryEditor.FindAllRules())
        {
            if (registered.Contains(rule)) continue;

            Debug.LogError($"AnimalSpawnRuleRegistry: '{rule.name}' is an AnimalSpawnRule in the project " +
                $"but is not in this registry, so it can never fire", rule);
            problems++;
        }

        problems += ValidateAnimals(registry, seen);

        if (problems == 0)
        {
            Debug.Log($"AnimalSpawnRuleRegistry: {seen.Count} spawn rules, all ids present and unique", registry);
        }
        else
        {
            Debug.LogError($"AnimalSpawnRuleRegistry: {problems} problem(s) found", registry);
        }

        return problems;
    }


    // MARK: validation passes


    // A rule whose animal is not in the AnimalRegistry can never spawn, because AnimalManager resolves
    // the animal through that registry before it instantiates anything.
    private static int ValidateAnimals(AnimalSpawnRuleRegistry registry, Dictionary<string, AnimalSpawnRule> seen)
    {
        AnimalRegistry animals = FindExactlyOneAnimalRegistry(registry);
        if (animals == null) return 0;

        int problems = 0;

        foreach (KeyValuePair<string, AnimalSpawnRule> pair in seen)
        {
            AnimalSpawnRule rule = pair.Value;

            if (rule.Animal == null)
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: '{rule.name}' has no animal, so it has nothing " +
                    $"to spawn", rule);
                problems++;
                continue;
            }

            if (!animals.TryGetIdByEntry(rule.Animal, out string _))
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: '{rule.name}' spawns '{rule.Animal.name}', " +
                    $"which is not in the AnimalRegistry, so it could never spawn", rule);
                problems++;
            }
        }

        return problems;
    }


    // MARK: helpers


    private static AnimalRegistry FindExactlyOneAnimalRegistry(AnimalSpawnRuleRegistry registry)
    {
        string[] guids = AssetDatabase.FindAssets("t:AnimalRegistry");

        if (guids.Length == 1)
        {
            return AssetDatabase.LoadAssetAtPath<AnimalRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        Debug.LogWarning($"AnimalSpawnRuleRegistry: found {guids.Length} AnimalRegistry assets instead of " +
            $"one, so rule animals were not checked", registry);
        return null;
    }
}
