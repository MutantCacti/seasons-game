using System.IO;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-V4-Pro


// The one place the save file's name and path are decided. Every worktree is a separate Unity project
// but they all share one persistentDataPath, so in the Editor the file is named after the project
// folder and the checkpoints, notifications, compendium and registry branches can run side by side.
//
// Builds keep the plain name: there is only ever one build installed.
public static class SavePaths
{
    private const string BUILD_FILE_NAME = "Save.json";
    private const string BUILD_OPTIONS_FILE_NAME = "Options.json";


    // Full path of the live save file. Both SaveDataManager and Tools > SaveHelper > Clear Save use this,
    // so they can never disagree about which file is the save.
    public static string FullPath { get { return Path.Combine(Application.persistentDataPath, FileName); } }


    // Full path of the options file. Kept separate from the save so Clear Save and Clear Options
    // can never touch each other's data.
    public static string OptionsFullPath { get { return Path.Combine(Application.persistentDataPath, OptionsFileName); } }


    public static string FileName
    {
        get
        {
#if UNITY_EDITOR
            // Application.dataPath is <project root>/Assets
            return $"Save-{ProjectName}.json";
#else
            return BUILD_FILE_NAME;
#endif
        }
    }


    // Same naming rule as the save: per-worktree in the Editor (all worktrees share one
    // persistentDataPath), plain name in a build.
    public static string OptionsFileName
    {
        get
        {
#if UNITY_EDITOR
            return $"Options-{ProjectName}.json";
#else
            return BUILD_OPTIONS_FILE_NAME;
#endif
        }
    }


#if UNITY_EDITOR
    // The folder that holds Assets/, which is the name this worktree is known by
    private static string ProjectName
    {
        get
        {
            string dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath)) return "UnknownProject";

            string trimmed = dataPath.TrimEnd('/', '\\');
            int separator = trimmed.LastIndexOfAny(new[] { '/', '\\' });

            // dataPath should end in Assets, so the project is its parent. If it somehow does not,
            // using the last folder is still better than refusing to save.
            string last = separator >= 0 ? trimmed.Substring(separator + 1) : trimmed;
            string parent = separator > 0 ? trimmed.Substring(0, separator) : "";

            if (!string.Equals(last, "Assets", System.StringComparison.Ordinal) || parent.Length == 0)
            {
                return last;
            }

            int parentSeparator = parent.LastIndexOfAny(new[] { '/', '\\' });
            return parentSeparator >= 0 ? parent.Substring(parentSeparator + 1) : parent;
        }
    }
#endif
}
