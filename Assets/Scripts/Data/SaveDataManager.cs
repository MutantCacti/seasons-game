using System.Collections.Generic;
using System.IO;
using UnityEngine;


// Exposes a live SaveData that it loads from disk and writes back to disk
public class SaveDataManager : MonoBehaviour
{
    private static SaveDataManager instance;
    public static SaveDataManager Instance { get { return instance; } }


    private SaveData saveData;
    public SaveData SaveData { get { return saveData; } }

    // True while this session has created a world that is not finished populating
    public bool IsConstructingNewWorld { get; private set; }


    // SavePaths owns the name, so SaveHelper's Clear Save can never point at a different file
    private string FullPath { get { return SavePaths.FullPath; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one SaveDataManager in the scene - destroying newest !!");
            Destroy(gameObject);
            return;
        }

        instance = this;
        saveData = ReadFromDisk();
    }


    public void Save()
    {
        if (saveData == null)
        {
            Debug.LogError("SaveDataManager: tried to save before there was any SaveData");
            return;
        }

        saveData.lastSaveTime = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        saveData.PrepareForWrite();

        WriteToDisk(saveData);
    }


    // NB: currently only the EntityManager needs to track this
    // if another system needs to populate a new world later,
    // this function will need to track each one and only clear
    // IsConstructingNewWorld when they all have acknowledged
    public void AcknowledgeNewWorld()
    {
        IsConstructingNewWorld = false;
    }


    // used by the new game path
    public void ResetToNewGame()
    {
        saveData = CreateNewSave();
        WriteToDisk(saveData);
    }


    // the only place to make a new save
    private SaveData CreateNewSave()
    {
        IsConstructingNewWorld = true;

        return new SaveData();
    }


    private SaveData ReadFromDisk()
    {
        try
        {
            if (!File.Exists(FullPath))
            {
                Debug.Log($"SaveDataManager: no save at '{FullPath}', starting a new one");
                return CreateNewSave();
            }

            SaveData loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(FullPath));

            if (loaded == null)
            {
                Debug.LogError($"SaveDataManager: '{FullPath}' did not deserialise, starting a new one");
                return CreateNewSave();
            }

            // any other version is not this one: there is no migration, so it is a new game
            if (loaded.saveVersion != SaveData.CURRENT_VERSION)
            {
                Debug.Log($"SaveDataManager: save is version {loaded.saveVersion} but we are in version " +
                    $"{SaveData.CURRENT_VERSION} - starting a new game");
                return CreateNewSave();
            }

            if (loaded.world == null)
            {
                Debug.LogWarning($"SaveDataManager: version {SaveData.CURRENT_VERSION} save at '{FullPath}' " +
                    $"has no world - starting it empty");
                loaded.world = new WorldState();
            }

            loaded.world.EnsureSections();

            if (loaded.checkpoints == null) loaded.checkpoints = new List<Checkpoint>();

            Debug.Log($"SaveDataManager: Loaded save from {FullPath}");

            return loaded;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveDataManager: failed to read '{FullPath}' ... {e.Message}");
            return CreateNewSave();
        }
    }


    private bool WriteToDisk(SaveData data)
    {
        try
        {
            File.WriteAllText(FullPath, JsonUtility.ToJson(data, false));
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveDataManager: failed to write '{FullPath}' ... {e.Message}");
            return false;
        }
    }
}
