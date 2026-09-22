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

    // True while a world has been created but never written to disk. The world initialisation seeds the
    // authored fixtures while this is up, so they are only ever created once.
    public bool IsNewWorld { get; private set; }


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

        // the world is on disk now, so it is no longer waiting for its fixtures to be seeded
        if (WriteToDisk(saveData)) IsNewWorld = false;
    }


    // used by the new game path
    public void ResetToNewGame()
    {
        saveData = CreateNewSave();
        WriteToDisk(saveData);
    }


    // The one place a world is created: no save on disk, a save that will not deserialise, a save from a
    // newer build, or an explicit reset. IsNewWorld stays up until the world is first written back, so
    // the fixtures are seeded exactly once however the world came to be.
    private SaveData CreateNewSave()
    {
        IsNewWorld = true;

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

            if (loaded.saveVersion > SaveData.CURRENT_VERSION)
            {
                Debug.LogError($"SaveDataManager: save is version {loaded.saveVersion} but we are in version {SaveData.CURRENT_VERSION} - treating it as a new game");
                return CreateNewSave();
            }

            Debug.Log($"SaveDataManager: Loaded save from {FullPath}");

            if (loaded.terrain == null) loaded.terrain = new TerrainSaveData();
            if (loaded.entities == null) loaded.entities = new EntitySaveData();
            if (loaded.inventory == null) loaded.inventory = new SerializableDictionary<string, int>();

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
