using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(AnimalRegistry))]
public class AnimalRegistryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        AnimalRegistry registry = (AnimalRegistry)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Gather All Animals In Project"))
        {
            GatherAllAnimals(registry);
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


    private static void GatherAllAnimals(AnimalRegistry registry)
    {
        List<Animal> found = FindAllAnimals();

        List<Animal> ordered = OrderAnimals(registry, found, out int unrankedCount);

        Undo.RecordObject(registry, "Gather Animals");

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("animals");

        list.arraySize = ordered.Count;
        for (int i = 0; i < ordered.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
        }

        serialized.ApplyModifiedProperties();

        registry.Rebuild();

        Debug.Log($"AnimalRegistry: gathered {ordered.Count} animals ({ordered.Count - unrankedCount} ordered " +
            $"by the puzzle they complete, {unrankedCount} completing none)", registry);
    }


    private static void AssignMissingIds(AnimalRegistry registry)
    {
        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("animals");

        HashSet<string> taken = new HashSet<string>();
        List<Animal> missing = new List<Animal>();

        for (int i = 0; i < list.arraySize; i++)
        {
            Animal animal = list.GetArrayElementAtIndex(i).objectReferenceValue as Animal;
            if (animal == null) continue;

            if (string.IsNullOrEmpty(animal.Id)) missing.Add(animal);
            else taken.Add(animal.Id);
        }

        foreach (Animal animal in missing)
        {
            string id = MakeUnique(Slug(animal.name), taken);

            // Animal.Id is a read-only property, so the private backing field is the only way in
            Undo.RecordObject(animal, "Assign Animal Id");

            SerializedObject serializedAnimal = new SerializedObject(animal);
            serializedAnimal.FindProperty("id").stringValue = id;
            serializedAnimal.ApplyModifiedProperties();

            EditorUtility.SetDirty(animal);

            taken.Add(id);
            Debug.Log($"AnimalRegistry: assigned id '{id}' to '{animal.name}'", animal);
        }

        registry.Rebuild();

        if (missing.Count == 0) Debug.Log("AnimalRegistry: no missing ids", registry);
    }


    private static void Validate(AnimalRegistry registry)
    {
        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("animals");

        Dictionary<string, Animal> seen = new Dictionary<string, Animal>();
        int problems = 0;

        for (int i = 0; i < list.arraySize; i++)
        {
            Animal animal = list.GetArrayElementAtIndex(i).objectReferenceValue as Animal;

            if (animal == null)
            {
                Debug.LogError($"AnimalRegistry: entry {i} is empty", registry);
                problems++;
                continue;
            }

            if (string.IsNullOrEmpty(animal.Id))
            {
                Debug.LogError($"AnimalRegistry: '{animal.name}' has no Id", animal);
                problems++;
                continue;
            }

            if (seen.TryGetValue(animal.Id, out Animal other))
            {
                Debug.LogError($"AnimalRegistry: duplicate Id '{animal.Id}' on '{animal.name}' " +
                    $"and '{other.name}'", animal);
                problems++;
                continue;
            }

            seen.Add(animal.Id, animal);
        }

        // an Animal forgotten here can never be saved, because the registry is the only thing that
        // resolves an id back to an asset
        HashSet<Animal> registered = new HashSet<Animal>(seen.Values);

        foreach (Animal animal in FindAllAnimals())
        {
            if (registered.Contains(animal)) continue;

            Debug.LogError($"AnimalRegistry: '{animal.name}' is an Animal in the project " +
                $"but is not in this registry", animal);
            problems++;
        }

        // rule ids key the save's fired list, so two rules sharing one would fire only once between them
        Dictionary<string, AnimalSpawnRule> ruleIds = new Dictionary<string, AnimalSpawnRule>();

        foreach (AnimalSpawnRule rule in FindAllRules())
        {
            if (rule.Animal != null && !registered.Contains(rule.Animal))
            {
                Debug.LogError($"AnimalRegistry: rule '{rule.name}' spawns '{rule.Animal.name}', " +
                    $"which is not in this registry", rule);
                problems++;
            }

            if (ruleIds.TryGetValue(rule.Id, out AnimalSpawnRule otherRule))
            {
                Debug.LogError($"AnimalRegistry: duplicate rule Id '{rule.Id}' on '{rule.name}' " +
                    $"and '{otherRule.name}'", rule);
                problems++;
                continue;
            }

            ruleIds.Add(rule.Id, rule);
        }

        if (problems == 0)
        {
            Debug.Log($"AnimalRegistry: {seen.Count} animals and {ruleIds.Count} spawn rules, all ids " +
                $"present and unique", registry);
        }
        else
        {
            Debug.LogError($"AnimalRegistry: {problems} problem(s) found", registry);
        }
    }


    // MARK: ordering


    // Orders animals by when the player meets them: the earliest place in the puzzle walk of any
    // puzzle one of their rules completes. Animals whose rules complete nothing reachable go last.
    // Ties break A->Z by name. Falls back to plain A->Z when there is no single PuzzleRegistry.
    private static List<Animal> OrderAnimals(AnimalRegistry registry, List<Animal> found, out int unrankedCount)
    {
        unrankedCount = 0;

        PuzzleRegistry puzzleRegistry = FindPuzzleRegistry(registry);

        // puzzle -> its place in the walk: depth-major, and A->Z within a depth as the walker sorts it
        Dictionary<Puzzle, int> positions = new Dictionary<Puzzle, int>();

        if (puzzleRegistry != null)
        {
            PuzzleTreeWalker.Result walk = PuzzleTreeWalker.Walk(puzzleRegistry.StartingPuzzles);

            for (int depth = 0; depth < walk.Levels.Count; depth++)
            {
                List<Puzzle> level = walk.Levels[depth];

                for (int i = 0; i < level.Count; i++)
                {
                    if (!positions.ContainsKey(level[i])) positions.Add(level[i], positions.Count);
                }
            }
        }

        Dictionary<Animal, int> ranks = new Dictionary<Animal, int>();

        for (int i = 0; i < found.Count; i++)
        {
            ranks[found[i]] = int.MaxValue;
        }

        foreach (AnimalSpawnRule rule in FindAllRules())
        {
            if (rule.Animal == null || rule.Completes == null) continue;
            if (!ranks.TryGetValue(rule.Animal, out int rank)) continue;
            if (!positions.TryGetValue(rule.Completes, out int position)) continue;

            if (position < rank) ranks[rule.Animal] = position;
        }

        List<Animal> ordered = new List<Animal>(found);

        ordered.Sort((a, b) =>
        {
            int byRank = ranks[a].CompareTo(ranks[b]);
            if (byRank != 0) return byRank;

            return string.CompareOrdinal(a.name, b.name);
        });

        for (int i = 0; i < ordered.Count; i++)
        {
            if (ranks[ordered[i]] == int.MaxValue) unrankedCount++;
        }

        return ordered;
    }


    // AnimalRegistry has no PuzzleRegistry field, so the project's one registry is looked up instead
    private static PuzzleRegistry FindPuzzleRegistry(AnimalRegistry registry)
    {
        string[] guids = AssetDatabase.FindAssets("t:PuzzleRegistry");

        if (guids.Length != 1)
        {
            Debug.LogWarning($"AnimalRegistry: found {guids.Length} PuzzleRegistry assets instead of one, " +
                $"so animals are gathered in plain A->Z order", registry);
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<PuzzleRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }


    // MARK: helpers


    private static List<Animal> FindAllAnimals()
    {
        List<Animal> found = new List<Animal>();

        foreach (string guid in AssetDatabase.FindAssets("t:Animal"))
        {
            Animal animal = AssetDatabase.LoadAssetAtPath<Animal>(AssetDatabase.GUIDToAssetPath(guid));
            if (animal != null) found.Add(animal);
        }

        return found;
    }


    // internal rather than private so AnimalSpawnRuleRegistryEditor and the Tools > Registries menu can
    // ask the same question without a second copy of the scan drifting from this one
    internal static List<AnimalSpawnRule> FindAllRules()
    {
        List<AnimalSpawnRule> found = new List<AnimalSpawnRule>();

        foreach (string guid in AssetDatabase.FindAssets("t:AnimalSpawnRule"))
        {
            AnimalSpawnRule rule = AssetDatabase.LoadAssetAtPath<AnimalSpawnRule>(AssetDatabase.GUIDToAssetPath(guid));
            if (rule != null) found.Add(rule);
        }

        return found;
    }


    // "Snowy Owls" becomes "snowy_owls"
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

        return builder.Length > 0 ? builder.ToString() : "animal";
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
