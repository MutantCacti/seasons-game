using System.IO;
using UnityEngine;
using UnityEngine.Audio;


// Owns the global options: loads them at startup, applies them to the AudioMixer, and saves them
// to their own file (never the game save). Lives in the Persistent Scene under the bootstrapper
// root, so it outlives every scene load and the volume sliders can always reach it.
public class GlobalOptionsManager : MonoBehaviour
{
    private static GlobalOptionsManager instance;
    public static GlobalOptionsManager Instance { get { return instance; } }


    // the volume sliders never go below 0.0001, so clamp a corrupt save's 0 to the same floor
    // rather than feeding log10(0) into the mixer
    private const float MIN_VOLUME = 0.0001f;


    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private GlobalOptionsDatabase database;


    private GlobalOptionsData data;
    private bool dirty;


    private string OptionsPath { get { return SavePaths.OptionsFullPath; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one GlobalOptionsManager in the scene - destroying newest !!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        Load();
    }


    private void Start()
    {
        ApplyAll();
    }


    // MARK: load / apply / save


    private void Load()
    {
        if (database == null)
        {
            Debug.LogError("GlobalOptionsManager: no GlobalOptionsDatabase assigned, so no volume is applied or saved");
            data = new GlobalOptionsData();
            return;
        }

        try
        {
            if (!File.Exists(OptionsPath))
            {
                Debug.Log($"GlobalOptionsManager: no options file at '{OptionsPath}', using defaults");
                data = new GlobalOptionsData();
                data.InitialiseDefaultValues(database);
                return;
            }

            data = JsonUtility.FromJson<GlobalOptionsData>(File.ReadAllText(OptionsPath));

            if (data == null || data.globalOptionValues == null || data.globalOptionValues.Count == 0)
            {
                Debug.LogWarning($"GlobalOptionsManager: '{OptionsPath}' was empty or corrupt, using defaults");
                data = new GlobalOptionsData();
                data.InitialiseDefaultValues(database);
                return;
            }

            // backfill anything the database knows that the file does not, so a new option added
            // later still gets a sensible value in an old file
            foreach (GlobalOption option in database.AllGlobalOptions)
            {
                if (option == null || string.IsNullOrEmpty(option.OptionID)) continue;

                if (!data.globalOptionValues.ContainsKey(option.OptionID))
                {
                    data.globalOptionValues[option.OptionID] = option.DefaultValue;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"GlobalOptionsManager: failed to read '{OptionsPath}', using defaults ... {e.Message}");
            data = new GlobalOptionsData();
            data.InitialiseDefaultValues(database);
        }
    }


    private void ApplyAll()
    {
        if (audioMixer == null)
        {
            Debug.LogError("GlobalOptionsManager: no AudioMixer assigned, so no volume is applied");
            return;
        }

        if (database == null || data == null) return;

        // iterate the database, not the map, so ids that only exist in a stale file are ignored
        foreach (GlobalOption option in database.AllGlobalOptions)
        {
            if (option == null || string.IsNullOrEmpty(option.OptionID)) continue;

            ApplyOne(option.OptionID, GetValue(option.OptionID));
        }
    }


    private void ApplyOne(string optionID, float value)
    {
        if (audioMixer == null) return;

        audioMixer.SetFloat(optionID, VolumeToDb(value));
    }


    private float VolumeToDb(float value)
    {
        return Mathf.Log10(Mathf.Max(value, MIN_VOLUME)) * 20f - 4f;
    }


    // MARK: public api


    public float GetValue(string optionID)
    {
        if (data != null && data.globalOptionValues != null &&
            data.globalOptionValues.TryGetValue(optionID, out float value))
        {
            return value;
        }

        if (database != null)
        {
            foreach (GlobalOption option in database.AllGlobalOptions)
            {
                if (option != null && option.OptionID == optionID) return option.DefaultValue;
            }
        }

        Debug.LogError($"GlobalOptionsManager: no option with id '{optionID}', returning 2");
        return 2f;
    }


    public void SetValue(string optionID, float value)
    {
        if (data == null || data.globalOptionValues == null)
        {
            Debug.LogError("GlobalOptionsManager: no options data, so the value was ignored");
            return;
        }

        data.globalOptionValues[optionID] = value;
        dirty = true;

        ApplyOne(optionID, value);
    }


    public void Save()
    {
        if (!dirty) return;

        if (data == null)
        {
            Debug.LogError("GlobalOptionsManager: tried to save before there was any options data");
            return;
        }

        try
        {
            File.WriteAllText(OptionsPath, JsonUtility.ToJson(data, false));
            dirty = false;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"GlobalOptionsManager: failed to write '{OptionsPath}' ... {e.Message}");
        }
    }
}
