using System.IO;
using UnityEngine;


// Exposes a live SaveData that it loads from disk and writes back to disk
public class SaveDataManager : MonoBehaviour
{
    private static SaveDataManager instance;
    public static SaveDataManager Instance { get { return instance; } }


    [SerializeField] private string fileName = "Save.json";


    private SaveData saveData;
    public SaveData SaveData { get { return saveData; } }


    private string FullPath { get { return Path.Combine(Application.persistentDataPath, fileName); } }


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


    // used by the new game path
    public void ResetToNewGame()
    {
        saveData = new SaveData();
        WriteToDisk(saveData);
    }


    private SaveData ReadFromDisk()
    {
        try
        {
            if (!File.Exists(FullPath))
            {
                Debug.Log($"SaveDataManager: no save at '{FullPath}', starting a new one");
                return new SaveData();
            }

            SaveData loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(FullPath));

            if (loaded == null)
            {
                Debug.LogError($"SaveDataManager: '{FullPath}' did not deserialise, starting a new one");
                return new SaveData();
            }

            if (loaded.saveVersion > SaveData.CURRENT_VERSION)
            {
                Debug.LogError($"SaveDataManager: save is version {loaded.saveVersion} but we are in version {SaveData.CURRENT_VERSION} - treating it as a new game");
                return new SaveData();
            }

            if (loaded.terrain == null) loaded.terrain = new TerrainSaveData();
            if (loaded.inventory == null) loaded.inventory = new SerializableDictionary<string, int>();

            return loaded;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveDataManager: failed to read '{FullPath}' ... {e.Message}");
            return new SaveData();
        }
    }


    private void WriteToDisk(SaveData data)
    {
        try
        {
            File.WriteAllText(FullPath, JsonUtility.ToJson(data, false));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveDataManager: failed to write '{FullPath}' ... {e.Message}");
        }
    }
}
