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


    internal static void GatherAllItems(GameItemRegistry registry)
    {
        if (registry == null)
        {
            Debug.LogError("GameItemRegistryEditor: no registry to gather into, so nothing was gathered");
            return;
        }

        List<GameItem> found = FindAllItems();

        int compendiumCount;
        int unreachedCount;
        int reachedCount;

        List<GameItem> ordered = OrderItems(registry, found, out compendiumCount, out unreachedCount, out reachedCount);

        Undo.RecordObject(registry, "Gather Game Items");

        SerializedObject serialized = new SerializedObject(registry);
        SerializedProperty list = serialized.FindProperty("items");

        list.arraySize = ordered.Count;
        for (int i = 0; i < ordered.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
        }

        serialized.ApplyModifiedProperties();

        registry.Rebuild();

        Debug.Log($"GameItemRegistry: gathered {ordered.Count} items ({compendiumCount} compendium, " +
            $"{unreachedCount} unreached, {reachedCount} from puzzle tree)", registry);
    }


    internal static void AssignMissingIds(GameItemRegistry registry)
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


    internal static int Validate(GameItemRegistry registry)
    {
        if (registry == null)
        {
            Debug.LogError("GameItemRegistryEditor: no registry to validate, so nothing was validated");
            return 1;
        }

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

        foreach (GameItem item in seen.Values)
        {
            problems += ValidateGrowth(item, registered);
            problems += ValidateReactions(item, registered);
        }

        if (problems == 0)
        {
            Debug.Log($"GameItemRegistry: {seen.Count} items, all ids present and unique", registry);
        }
        else
        {
            Debug.LogError($"GameItemRegistry: {problems} problem(s) found", registry);
        }

        return problems;
    }


    // MARK: growth


    // Growth replaces one placed entity with another, so both ends need a prefab and the grown one can't
    // be a tile. Returns the number of problems. Tags the grown item needs but the source doesn't are
    // only a warning: the growth waits until the ground suits it, which may be never
    private static int ValidateGrowth(GameItem item, HashSet<GameItem> registered)
    {
        GameItem into = item.GrowsInto;
        if (into == null) return 0;

        int problems = 0;

        if (item.Prefab == null)
        {
            Debug.LogError($"GameItemRegistry: '{item.name}' grows into '{into.name}' but has no prefab, so it " +
                $"is never an entity that could grow", item);
            problems++;
        }

        if (into.Prefab == null || into.Tile != null)
        {
            Debug.LogError($"GameItemRegistry: '{item.name}' grows into '{into.name}', which needs a prefab and " +
                $"no tile to replace an entity", item);
            problems++;
        }

        if (!registered.Contains(into))
        {
            Debug.LogError($"GameItemRegistry: '{item.name}' grows into '{into.name}', which is not in this " +
                $"registry, so the grown entity could never be saved", item);
            problems++;
        }

        // a chain that comes back round would grow forever
        HashSet<GameItem> chain = new HashSet<GameItem> { item };

        for (GameItem step = into; step != null; step = step.GrowsInto)
        {
            if (chain.Add(step)) continue;

            Debug.LogError($"GameItemRegistry: '{item.name}' grows round in a loop back to '{step.name}'", item);
            problems++;
            break;
        }

        WarnAboutTags(item, into, "placedOnTags", "needs", "doesn't");
        WarnAboutTags(item, into, "antiPlacedOnTags", "is kept off", "isn't");

        return problems;
    }


    // warns for each tag in into's list that item's own list lacks
    private static void WarnAboutTags(GameItem item, GameItem into, string field, string verb, string negation)
    {
        HashSet<string> own = new HashSet<string>(ReadTags(item, field));

        foreach (string tag in ReadTags(into, field))
        {
            if (own.Contains(tag)) continue;

            Debug.LogWarning($"GameItemRegistry: '{into.name}' {verb} tag '{tag}' but '{item.name}', which " +
                $"grows into it, {negation}, so the growth may wait for ground that never comes", item);
        }
    }


    private static List<string> ReadTags(GameItem item, string field)
    {
        List<string> tags = new List<string>();

        SerializedProperty list = new SerializedObject(item).FindProperty(field);
        if (list == null) return tags;

        for (int i = 0; i < list.arraySize; i++)
        {
            string tag = list.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(tag)) tags.Add(tag);
        }

        return tags;
    }


    // MARK: reactions


    // A reaction replaces one placed entity with another when a reagent is dropped on it, so both ends
    // are asked for the same things a growth is, and the applied item needs no prefab of its own: the
    // whole point of a reagent is that it has nothing to place. Returns the number of problems
    private static int ValidateReactions(GameItem item, HashSet<GameItem> registered)
    {
        IReadOnlyList<ItemReaction> reactions = item.Reactions;

        if (reactions == null || reactions.Count == 0) return 0;

        int problems = 0;

        // an item that does both is legal - a sprout could react to something as well as grow - but it
        // is far more often a seed that kept its old growsInto, which would grow on its own AND react
        if (item.GrowsInto != null)
        {
            Debug.LogWarning($"GameItemRegistry: '{item.name}' both grows into '{item.GrowsInto.name}' and " +
                $"has reactions, so it will grow on its own as well as react - clear growsInto if the " +
                $"reaction is meant to be the whole change", item);
        }

        // nothing placed, so nothing can be dropped on: a reaction here can never run
        if (item.Prefab == null)
        {
            Debug.LogError($"GameItemRegistry: '{item.name}' has reactions but no prefab, so it is never a " +
                $"placed entity that a reagent could be dropped on", item);
            problems++;
        }

        HashSet<GameItem> applied = new HashSet<GameItem>();

        for (int i = 0; i < reactions.Count; i++)
        {
            ItemReaction reaction = reactions[i];

            if (reaction == null)
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' has an empty reaction at {i}", item);
                problems++;
                continue;
            }

            GameItem from = reaction.Catalyst;
            GameItem into = reaction.Becomes;

            if (from == null || into == null)
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' has a reaction with no " +
                    $"{(from == null ? "catalyst item" : "result")}, so it can never do anything", item);
                problems++;
                continue;
            }

            // the same shape a growth's result needs: it replaces an entity, so it has to be one
            if (into.Prefab == null || into.Tile != null)
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' reacts to '{from.name}' by becoming " +
                    $"'{into.name}', which needs a prefab and no tile to replace an entity", item);
                problems++;
            }

            // a reagent is not saved and is never placed, but a reaction to an unregistered item could
            // still be authored, and nobody would ever see the reagent in the world
            if (!registered.Contains(from))
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' reacts to '{from.name}', which is not in " +
                    $"this registry", item);
                problems++;
            }

            if (!registered.Contains(into))
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' reacts to '{from.name}' by becoming " +
                    $"'{into.name}', which is not in this registry, so the new entity could never be saved", item);
                problems++;
            }

            if (!applied.Add(from))
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' has more than one reaction to '{from.name}', " +
                    $"so only the first is ever used", item);
                problems++;
            }
        }

        return problems;
    }


    // MARK: ordering


    // Orders the gathered items into the order the player meets them: the compendium first, then items
    // no puzzle reaches (A->Z), then the puzzle tree's ingredients in meet order, each item followed by
    // whatever it grows into or reacts into. Falls back to a plain A->Z sort when the puzzle registry
    // is missing.
    private static List<GameItem> OrderItems(GameItemRegistry registry, List<GameItem> found,
        out int compendiumCount, out int unreachedCount, out int reachedCount)
    {
        compendiumCount = 0;
        unreachedCount = 0;
        reachedCount = 0;

        PuzzleRegistry puzzleRegistry = registry.PuzzleRegistry;

        if (puzzleRegistry == null)
        {
            Debug.LogWarning("GameItemRegistry: puzzleRegistry is null, so items are gathered in plain A->Z order", registry);

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        PuzzleTreeWalker.Result walk = PuzzleTreeWalker.Walk(puzzleRegistry.StartingPuzzles);

        // step 3 first: collect every item the puzzle tree reaches, in meet order. An item that
        // several depths reach keeps the place of its first appearance.
        List<GameItem> reached = new List<GameItem>();
        HashSet<GameItem> reachedSet = new HashSet<GameItem>();

        // what each reached item can become: growsInto, and every reaction's becomes. Kept for the
        // dependency order below, which needs the edges rather than just the set
        Dictionary<GameItem, GameItem[]> becomeEdges = new Dictionary<GameItem, GameItem[]>();

        for (int depth = 0; depth < walk.Levels.Count; depth++)
        {
            List<Puzzle> level = walk.Levels[depth];

            List<GameItem> ingredients = new List<GameItem>();

            for (int i = 0; i < level.Count; i++)
            {
                IReadOnlyList<Ingredient> puzzleIngredients = level[i].Ingredients;
                if (puzzleIngredients == null) continue;

                for (int j = 0; j < puzzleIngredients.Count; j++)
                {
                    GameItem item = puzzleIngredients[j].item;
                    if (item == null) continue;

                    ingredients.Add(item);
                }
            }

            ingredients.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            for (int i = 0; i < ingredients.Count; i++)
            {
                if (reachedSet.Add(ingredients[i])) reached.Add(ingredients[i]);
            }
        }

        // step 4: what a reached item grows into or reacts into is reached too, so it is not swept up
        // with the items no puzzle reaches and hoisted to the front of the book: a seed's sprout and
        // flower follow the seed, which is where the player meets them
        for (int i = 0; reached.Count > i; i++)
        {
            CollectBecomes(reached[i], reachedSet, reached, becomeEdges);
        }

        // then order by dependency, so a thing always follows the thing it is made from: the seed
        // before its sprout, the sprout before its flower, and each chain kept together rather than
        // interleaved with the other. Ties keep the meet order above, so the puzzle tree still decides
        // between items that don't depend on each other
        SortByDependencies(reached, becomeEdges);

        // step 1: the compendium leads, matched by id rather than name
        GameItem compendium = null;

        for (int i = 0; i < found.Count; i++)
        {
            if (found[i].Id == "compendium")
            {
                compendium = found[i];
                break;
            }
        }

        if (compendium == null)
        {
            Debug.LogWarning("GameItemRegistry: no item with id 'compendium', so nothing is forced first", registry);
        }

        // step 2: items no puzzle reaches, A->Z
        List<GameItem> unreached = new List<GameItem>();

        for (int i = 0; i < found.Count; i++)
        {
            GameItem item = found[i];
            if (item == compendium) continue;
            if (reachedSet.Contains(item)) continue;

            unreached.Add(item);
        }

        unreached.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        // assemble: compendium, then unreached, then reached in the order the player meets them
        List<GameItem> ordered = new List<GameItem>(found.Count);
        HashSet<GameItem> placed = new HashSet<GameItem>();
        HashSet<GameItem> registered = new HashSet<GameItem>(found);

        if (compendium != null && placed.Add(compendium)) ordered.Add(compendium);

        for (int i = 0; i < unreached.Count; i++)
        {
            if (placed.Add(unreached[i])) ordered.Add(unreached[i]);
        }

        for (int i = 0; i < reached.Count; i++)
        {
            GameItem item = reached[i];
            if (!registered.Contains(item)) continue;   // reached from the tree but not gathered here
            if (!placed.Add(item)) continue;            // already placed, e.g. the compendium itself

            ordered.Add(item);
        }

        compendiumCount = compendium != null ? 1 : 0;
        unreachedCount = unreached.Count;
        reachedCount = ordered.Count - compendiumCount - unreachedCount;

        return ordered;
    }


    // Adds everything item can become - what it grows into, and what each of its reactions turns it
    // into - to reached. A hidden item is collected too: the seeds are hidden from the book, and the
    // flowers they lead to still have to be discovered through them. Records item's own edges so the
    // dependency order below can see them
    private static void CollectBecomes(GameItem item, HashSet<GameItem> reachedSet, List<GameItem> reached,
        Dictionary<GameItem, GameItem[]> becomeEdges)
    {
        if (item == null) return;
        if (becomeEdges.ContainsKey(item)) return;

        GameItem[] edges = OutputEdges(item);
        becomeEdges.Add(item, edges);

        for (int i = 0; i < edges.Length; i++)
        {
            GameItem into = edges[i];
            if (into == null) continue;
            if (!reachedSet.Add(into)) continue;

            reached.Add(into);
        }
    }


    // The items this one becomes: its growsInto and every complete reaction's becomes. A catalyst is
    // deliberately not an edge here - the player has to be holding it - so it never orders the book
    private static GameItem[] OutputEdges(GameItem item)
    {
        List<GameItem> edges = new List<GameItem>();

        if (item.GrowsInto != null) edges.Add(item.GrowsInto);

        IReadOnlyList<ItemReaction> reactions = item.Reactions;

        if (reactions != null)
        {
            for (int i = 0; i < reactions.Count; i++)
            {
                ItemReaction reaction = reactions[i];
                if (reaction == null || reaction.Becomes == null) continue;

                edges.Add(reaction.Becomes);
            }
        }

        return edges.ToArray();
    }


    // Reorders items only as much as the becomes edges require, keeping the meet order wherever the
    // edges allow it. A depth-first walk of each chain, entered from the reached list in meet order:
    // a seed, then the sprout it becomes, then the flower that sprout becomes, with the next chain
    // only after the whole of this one
    private static void SortByDependencies(List<GameItem> items, Dictionary<GameItem, GameItem[]> becomeEdges)
    {
        Dictionary<GameItem, int> finishOrder = new Dictionary<GameItem, int>();

        int counter = 0;

        // one walk per root, each with its own path set: every item is visited once in total, and a
        // chain is always entered where the player meets it rather than from its middle. The path set
        // is what terminates a become cycle, which validation reports separately
        for (int i = 0; i < items.Count; i++)
        {
            VisitDependencies(items[i], becomeEdges, finishOrder, new HashSet<GameItem>(), ref counter);
        }

        // a stable sort over the finish order, so two items with nothing between them hold their places
        items.Sort((a, b) =>
        {
            finishOrder.TryGetValue(a, out int left);
            finishOrder.TryGetValue(b, out int right);

            return left.CompareTo(right);
        });
    }


    // Depth-first with the chain in front of item first, then item, then what item becomes, so nothing
    // is placed before the thing it is made from
    private static void VisitDependencies(GameItem item, Dictionary<GameItem, GameItem[]> becomeEdges,
        Dictionary<GameItem, int> finishOrder, HashSet<GameItem> onPath, ref int counter)
    {
        if (item == null) return;
        if (finishOrder.ContainsKey(item)) return;
        if (!onPath.Add(item)) return;

        foreach (KeyValuePair<GameItem, GameItem[]> entry in becomeEdges)
        {
            if (!HasOutput(entry.Value, item)) continue;

            VisitDependencies(entry.Key, becomeEdges, finishOrder, onPath, ref counter);
        }

        finishOrder[item] = counter;
        counter++;

        if (!becomeEdges.TryGetValue(item, out GameItem[] edges) || edges == null) return;

        for (int i = 0; i < edges.Length; i++)
        {
            VisitDependencies(edges[i], becomeEdges, finishOrder, onPath, ref counter);
        }
    }


    private static bool HasOutput(GameItem[] outputs, GameItem item)
    {
        if (outputs == null) return false;

        for (int i = 0; i < outputs.Length; i++)
        {
            if (outputs[i] == item) return true;
        }

        return false;
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
