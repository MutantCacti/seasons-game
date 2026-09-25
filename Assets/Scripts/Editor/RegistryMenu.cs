using UnityEditor;
using UnityEngine;


// One place to run every registry's own gather / assign / validate, so a new registry does not need a
// new pass over the project. Each action delegates to the editor that already owns it, and the
// Inspector buttons on those editors keep working exactly as before.
public static class RegistryMenu
{
    // MARK: gather


    [MenuItem("Tools/Registries/Gather Terrain Tile Registry")]
    private static void GatherTerrainTileRegistry()
    {
        TerrainTileRegistry registry = FindExactlyOne<TerrainTileRegistry>();
        if (registry == null) return;

        TerrainTileRegistryEditor.GatherAllTiles(registry);
    }


    [MenuItem("Tools/Registries/Gather Puzzle Registry")]
    private static void GatherPuzzleRegistry()
    {
        PuzzleRegistry registry = FindExactlyOne<PuzzleRegistry>();
        if (registry == null) return;

        PuzzleRegistryEditor.GatherAllPuzzles(registry);
    }


    [MenuItem("Tools/Registries/Gather Game Item Registry")]
    private static void GatherGameItemRegistry()
    {
        GameItemRegistry registry = FindExactlyOne<GameItemRegistry>();
        if (registry == null) return;

        GameItemRegistryEditor.GatherAllItems(registry);
    }


    [MenuItem("Tools/Registries/Gather Animal Registry")]
    private static void GatherAnimalRegistry()
    {
        AnimalRegistry registry = FindExactlyOne<AnimalRegistry>();
        if (registry == null) return;

        AnimalRegistryEditor.GatherAllAnimals(registry);
    }


    [MenuItem("Tools/Registries/Gather Animal Spawn Rule Registry")]
    private static void GatherAnimalSpawnRuleRegistry()
    {
        AnimalSpawnRuleRegistry registry = FindExactlyOne<AnimalSpawnRuleRegistry>();
        if (registry == null) return;

        AnimalSpawnRuleRegistryEditor.GatherAllRules(registry);
    }


    [MenuItem("Tools/Registries/Gather Global Options Registry")]
    private static void GatherGlobalOptionsRegistry()
    {
        GlobalOptionsRegistry registry = FindExactlyOne<GlobalOptionsRegistry>();
        if (registry == null) return;

        GlobalOptionsRegistryEditor.GatherAllOptions(registry);
    }


    // MARK: all


    // Order matters, and it is a dependency order rather than the order of the menu items above:
    //   TerrainTile and Puzzle stand alone (Puzzle's gather touches only its own startingPuzzles list).
    //   GameItem orders itself by walking the puzzle tree and reading the PuzzleRegistry's
    //     startingPuzzles, so Puzzle gathered first keeps the two passes reading the same data.
    //   Animal walks the puzzle tree too, and its ordering needs each spawn rule's Animal.
    //   AnimalSpawnRule comes after Animal so the rules that point at animals are gathered once the
    //     animals they point at are in the animal registry.
    //   GlobalOptions is independent; it goes last so the log reads in the same order every time.
    private const string GATHER_ALL_ORDER = "TerrainTile, Puzzle, GameItem, Animal, AnimalSpawnRule, GlobalOptions";


    [MenuItem("Tools/Registries/Gather All Registries")]
    private static void GatherAllRegistries()
    {
        // A registry asset that is missing or duplicated is reported and skipped, and the rest of the
        // run carries on: one un-created registry should not stop the other five being gathered.
        TerrainTileRegistry tiles = FindExactlyOne<TerrainTileRegistry>();
        PuzzleRegistry puzzles = FindExactlyOne<PuzzleRegistry>();
        GameItemRegistry items = FindExactlyOne<GameItemRegistry>();
        AnimalRegistry animals = FindExactlyOne<AnimalRegistry>();
        AnimalSpawnRuleRegistry rules = FindExactlyOne<AnimalSpawnRuleRegistry>();
        GlobalOptionsRegistry options = FindExactlyOne<GlobalOptionsRegistry>();

        if (tiles != null) TerrainTileRegistryEditor.GatherAllTiles(tiles);
        if (puzzles != null) PuzzleRegistryEditor.GatherAllPuzzles(puzzles);
        if (items != null) GameItemRegistryEditor.GatherAllItems(items);
        if (animals != null) AnimalRegistryEditor.GatherAllAnimals(animals);
        if (rules != null) AnimalSpawnRuleRegistryEditor.GatherAllRules(rules);
        if (options != null) GlobalOptionsRegistryEditor.GatherAllOptions(options);

        // gathers first, then assigns ids: assigning first would hand ids to whatever happened to be in
        // each registry before the gather reshuffled it
        if (tiles != null) TerrainTileRegistryEditor.AssignMissingIds(tiles);
        PuzzleRegistryEditor.AssignMissingIds();
        if (items != null) GameItemRegistryEditor.AssignMissingIds(items);
        if (animals != null) AnimalRegistryEditor.AssignMissingIds(animals);
        if (rules != null) AnimalSpawnRuleRegistryEditor.AssignMissingIds(rules);

        // GlobalOptions has no Assign Missing Ids: an option id has to be the name of an exposed mixer
        // parameter, so only a human can write it
        Debug.Log($"RegistryMenu: gathered every registry it could find, in the order {GATHER_ALL_ORDER}, " +
            $"then assigned missing ids; GlobalOptions ids are left alone because they must name a " +
            $"mixer parameter");
    }


    [MenuItem("Tools/Registries/Validate All Registries")]
    private static void ValidateAllRegistries()
    {
        // read-only: every Validate below only reads assets and logs, so this never dirties the project.
        // A missing registry is reported by FindExactlyOne and contributes nothing to the count.
        int problems = 0;
        int validated = 0;

        TerrainTileRegistry tiles = FindExactlyOne<TerrainTileRegistry>();
        if (tiles != null) { problems += TerrainTileRegistryEditor.Validate(tiles); validated++; }

        PuzzleRegistry puzzles = FindExactlyOne<PuzzleRegistry>();
        if (puzzles != null) { problems += PuzzleRegistryEditor.Validate(puzzles); validated++; }

        GameItemRegistry items = FindExactlyOne<GameItemRegistry>();
        if (items != null) { problems += GameItemRegistryEditor.Validate(items); validated++; }

        AnimalRegistry animals = FindExactlyOne<AnimalRegistry>();
        if (animals != null) { problems += AnimalRegistryEditor.Validate(animals); validated++; }

        AnimalSpawnRuleRegistry rules = FindExactlyOne<AnimalSpawnRuleRegistry>();
        if (rules != null) { problems += AnimalSpawnRuleRegistryEditor.Validate(rules); validated++; }

        GlobalOptionsRegistry options = FindExactlyOne<GlobalOptionsRegistry>();
        if (options != null) { problems += GlobalOptionsRegistryEditor.Validate(options); validated++; }

        if (problems == 0)
        {
            Debug.Log($"RegistryMenu: {validated}/6 registries validated, no problems found");
        }
        else
        {
            Debug.LogError($"RegistryMenu: {validated}/6 registries validated, {problems} problem(s) found");
        }
    }


    // MARK: helpers


    // The editors take the registry because they dirty it through SerializedObject and Undo, so the menu
    // has to hand them the real asset. None, or several, is a project setup problem worth an error
    // rather than a silent skip.
    private static T FindExactlyOne<T>() where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");

        if (guids.Length == 1)
        {
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        Debug.LogError($"RegistryMenu: found {guids.Length} {typeof(T).Name} assets instead of one, so it " +
            $"was skipped");
        return null;
    }
}
