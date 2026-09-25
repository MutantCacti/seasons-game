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
        TerrainTileRegistryEditor.GatherAllTiles(null);
    }


    [MenuItem("Tools/Registries/Gather Puzzle Registry")]
    private static void GatherPuzzleRegistry()
    {
        PuzzleRegistryEditor.GatherAllPuzzles(null);
    }


    [MenuItem("Tools/Registries/Gather Game Item Registry")]
    private static void GatherGameItemRegistry()
    {
        GameItemRegistryEditor.GatherAllItems(null);
    }


    [MenuItem("Tools/Registries/Gather Animal Registry")]
    private static void GatherAnimalRegistry()
    {
        AnimalRegistryEditor.GatherAllAnimals(null);
    }


    [MenuItem("Tools/Registries/Gather Animal Spawn Rule Registry")]
    private static void GatherAnimalSpawnRuleRegistry()
    {
        AnimalSpawnRuleRegistryEditor.GatherAllRules(null);
    }


    [MenuItem("Tools/Registries/Gather Global Options Registry")]
    private static void GatherGlobalOptionsRegistry()
    {
        GlobalOptionsRegistryEditor.GatherAllOptions(null);
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
        // gathers first, then assigns ids: assigning first would hand ids to whatever happened to be in
        // each registry before the gather reshuffled it
        TerrainTileRegistryEditor.GatherAllTiles(null);
        PuzzleRegistryEditor.GatherAllPuzzles(null);
        GameItemRegistryEditor.GatherAllItems(null);
        AnimalRegistryEditor.GatherAllAnimals(null);
        AnimalSpawnRuleRegistryEditor.GatherAllRules(null);
        GlobalOptionsRegistryEditor.GatherAllOptions(null);

        TerrainTileRegistryEditor.AssignMissingIds(null);
        PuzzleRegistryEditor.AssignMissingIds();
        GameItemRegistryEditor.AssignMissingIds(null);
        AnimalRegistryEditor.AssignMissingIds(null);
        AnimalSpawnRuleRegistryEditor.AssignMissingIds(null);

        // GlobalOptions has no Assign Missing Ids: an option id has to be the name of an exposed mixer
        // parameter, so only a human can write it
        Debug.Log($"RegistryMenu: gathered every registry ({GATHER_ALL_ORDER}) and assigned missing ids; " +
            $"GlobalOptions ids are left alone because they must name a mixer parameter");
    }


    [MenuItem("Tools/Registries/Validate All Registries")]
    private static void ValidateAllRegistries()
    {
        // read-only: every Validate below only reads assets and logs, so this never dirties the project
        int problems = 0;

        problems += TerrainTileRegistryEditor.Validate(null);
        problems += PuzzleRegistryEditor.Validate(null);
        problems += GameItemRegistryEditor.Validate(null);
        problems += AnimalRegistryEditor.Validate(null);
        problems += AnimalSpawnRuleRegistryEditor.Validate(null);
        problems += GlobalOptionsRegistryEditor.Validate(null);

        if (problems == 0)
        {
            Debug.Log("RegistryMenu: all 6 registries validated, no problems found");
        }
        else
        {
            Debug.LogError($"RegistryMenu: all 6 registries validated, {problems} problem(s) found");
        }
    }
}
