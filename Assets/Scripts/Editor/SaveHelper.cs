using System.IO;
using UnityEditor;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-Flash


// Deletes the save file that SaveDataManager writes to, so a fresh world can be started without waiting
// for the game to overwrite it.
public static class SaveHelper
{
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
}
