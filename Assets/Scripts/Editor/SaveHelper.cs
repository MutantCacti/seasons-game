using System.IO;
using UnityEditor;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-Flash


// Deletes the save file that SaveDataManager writes to, so a fresh world can be started without waiting
// for the game to overwrite it.
public static class SaveHelper
{
    internal const string AlwaysNewSaveMenuPath = "Tools/SaveHelper/Always New Save";


    // EditorPrefs is per machine and shared by every project, and every worktree is its own Unity
    // project, so the key carries the project folder name. That is the same rule SavePaths uses to
    // name the save file, so two worktrees can never fight over one toggle.
    private static string AlwaysNewSaveKey
    {
        get { return "SaveHelper.AlwaysNewSave." + SavePaths.ProjectName; }
    }


    // the hook that clears on Play has to read the same state the menu writes
    public static bool AlwaysNewSaveEnabled
    {
        get { return EditorPrefs.GetBool(AlwaysNewSaveKey, false); }
    }


    // MARK: always new save


    // A checkmark is not a normal MenuItem state: it is cleared whenever the Editor restarts, so the
    // validate function re-applies it from EditorPrefs on every menu rebuild. Menu.SetChecked and
    // Menu.GetChecked exist on UnityEditor.Menu (UnityEditor.CoreModule.dll in 6000.3.24f1), and the
    // 6000.3 docs for Menu.SetChecked say the check status "is cleared when the Editor restarts",
    // which is exactly why the state is read back from EditorPrefs here rather than kept in a field.
    [MenuItem(AlwaysNewSaveMenuPath, true)]
    private static bool ValidateAlwaysNewSave()
    {
        Menu.SetChecked(AlwaysNewSaveMenuPath, EditorPrefs.GetBool(AlwaysNewSaveKey, false));
        return true;
    }


    [MenuItem(AlwaysNewSaveMenuPath)]
    private static void ToggleAlwaysNewSave()
    {
        bool wanted = !EditorPrefs.GetBool(AlwaysNewSaveKey, false);

        EditorPrefs.SetBool(AlwaysNewSaveKey, wanted);
        Menu.SetChecked(AlwaysNewSaveMenuPath, wanted);

        Debug.Log(wanted
            ? "SaveHelper: Always New Save is ON - the save is deleted every time Play is pressed"
            : "SaveHelper: Always New Save is OFF - the save is kept");
    }


    // MARK: clear


    [MenuItem("Tools/SaveHelper/Clear Save")]
    public static void ClearSave()
    {
        // SavePaths owns the name, so this works whether or not a SaveDataManager is loaded. That matters
        // because the manager lives in the Persistent Scene and that is usually not open while another
        // scene is being edited.
        string path = SavePaths.FullPath;

        if (!File.Exists(path))
        {
            Debug.Log($"SaveHelper: nothing to clear, no save at '{path}'");
            return;
        }

        // Batch mode has nobody to answer a dialog, and a prompt there would hang the run
        if (!Application.isBatchMode &&
            !EditorUtility.DisplayDialog(
                "Clear Save",
                $"Delete the save file?\n\n{path}",
                "Delete",
                "Cancel"))
        {
            return;
        }

        try
        {
            File.Delete(path);
            Debug.Log($"SaveHelper: deleted save at '{path}'");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveHelper: failed to delete '{path}' ... {e.Message}");
        }
    }


    [MenuItem("Tools/SaveHelper/Clear Options")]
    public static void ClearOptions()
    {
        // separate file from the save, so clearing one never touches the other
        string path = SavePaths.OptionsFullPath;

        if (!File.Exists(path))
        {
            Debug.Log($"SaveHelper: nothing to clear, no options file at '{path}'");
            return;
        }

        // Batch mode has nobody to answer a dialog, and a prompt there would hang the run
        if (!Application.isBatchMode &&
            !EditorUtility.DisplayDialog(
                "Clear Options",
                $"Delete the options file?\n\n{path}",
                "Delete",
                "Cancel"))
        {
            return;
        }

        try
        {
            File.Delete(path);
            Debug.Log($"SaveHelper: deleted options file at '{path}'");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveHelper: failed to delete '{path}' ... {e.Message}");
        }
    }
}


// Clears the save on the way into Play mode so testing a fresh world never needs Clear Save by hand.
// Its own class because [InitializeOnLoad] needs a static constructor, and SaveHelper is a bag of menu
// items rather than something Unity should run a constructor for.
[InitializeOnLoad]
public static class AlwaysNewSaveHook
{
    static AlwaysNewSaveHook()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }


    // ExitingEditMode is raised before play mode loads its scenes, so the file is gone before any
    // scene's Awake runs and SaveDataManager therefore loads nothing and starts a new save.
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;

        if (!SaveHelper.AlwaysNewSaveEnabled) return;

        string path = SavePaths.FullPath;

        if (!File.Exists(path))
        {
            Debug.LogWarning($"SaveHelper: Always New Save is ON, but there was no save at '{path}' " +
                $"to delete before entering Play mode");
            return;
        }

        try
        {
            File.Delete(path);

            // LogWarning rather than Log so it survives console filtering and shows its icon
            Debug.LogWarning($"SaveHelper: Always New Save is ON, so the save was deleted before " +
                $"entering Play mode ('{path}')");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveHelper: Always New Save is ON but '{path}' could not be deleted ... " +
                $"{e.Message}");
        }
    }
}
