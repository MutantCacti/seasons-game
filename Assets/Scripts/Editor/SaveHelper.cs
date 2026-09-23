using System.IO;
using UnityEditor;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-Flash


// Deletes the save file that SaveDataManager writes to, so a fresh world can be started without waiting
// for the game to overwrite it.
public static class SaveHelper
{
    // Must match SaveDataManager's own default. Only used when no SaveDataManager is loaded, because
    // the manager lives in the Persistent Scene and that is usually not open while another scene is
    // being edited.
    private const string DEFAULT_FILE_NAME = "Save.json";

    private const string FILE_NAME_FIELD = "fileName";


    [MenuItem("Tools/SaveHelper/Clear Save")]
    public static void ClearSave()
    {
        string path = ResolveSavePath();

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


    // The save lives at Application.persistentDataPath + SaveDataManager.fileName
    private static string ResolveSavePath()
    {
        return Path.Combine(Application.persistentDataPath, ReadConfiguredFileName());
    }


    // fileName is private, so it is read through the serialized field rather than guessed at
    private static string ReadConfiguredFileName()
    {
        // Include inactive: the manager may sit on a disabled object while a scene is being set up
        SaveDataManager manager =
            Object.FindFirstObjectByType<SaveDataManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            return DEFAULT_FILE_NAME;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty(FILE_NAME_FIELD);

        if (property == null || string.IsNullOrEmpty(property.stringValue))
        {
            Debug.LogWarning($"SaveHelper: '{manager.name}' has an empty file name, so " +
                $"'{DEFAULT_FILE_NAME}' is assumed instead.");

            return DEFAULT_FILE_NAME;
        }

        return property.stringValue;
    }
}
