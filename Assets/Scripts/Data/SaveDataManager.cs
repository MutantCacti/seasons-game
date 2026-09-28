using System;
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

    // a SaveSoon waiting for this frame's LateUpdate
    private bool saveSoon;


    // Raised once a newly created world has finished populating, so systems that held work back until
    // construction ended can flush it
    public event Action NewWorldConstructed;


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


    // Saves at the end of this frame instead of now, so several changes in one frame (a worm greening
    // two cells, two animals at once) cost one write. What changed is already in SaveData, so a quit
    // before the flush is still caught by the quit and pause saves below
    public void SaveSoon()
    {
        saveSoon = true;
    }


    private void LateUpdate()
    {
        if (!saveSoon) return;

        saveSoon = false;

        if (CanFlush) Save();
    }


    // Catches what only lives in memory, like animal positions. Unity sends OnApplicationQuit to every
    // active object before any OnDisable/OnDestroy (manual, "When quitting"), and in the editor when
    // play mode exits, so the scene still holds its latest state here. Handler order between objects is
    // undefined, which is fine: everything is already written into saveData by the last LateUpdate.
    private void OnApplicationQuit()
    {
        if (CanFlush) Save();
    }


    // mobile and alt-tab never promise a quit, so going into the background saves too
    private void OnApplicationPause(bool paused)
    {
        if (paused && CanFlush) Save();
    }


    // a world still being built would reach disk without its fixtures, and a loaded one would then
    // never seed them
    private bool CanFlush { get { return saveData != null && !IsConstructingNewWorld; } }


    // NB: currently only the EntityManager needs to track this
    // if another system needs to populate a new world later,
    // this function will need to track each one and only clear
    // IsConstructingNewWorld when they all have acknowledged
    public void AcknowledgeNewWorld()
    {
        IsConstructingNewWorld = false;

        NewWorldConstructed?.Invoke();
    }


    // A save worth continuing: one was read from disk rather than started fresh (none, unreadable, or
    // from another version all start a new world)
    public bool HasSave { get { return saveData != null && !IsConstructingNewWorld && File.Exists(FullPath); } }


    // The title screen's New Game: the file goes, and the save in memory is replaced with a new world,
    // or the game scene would load the old one from memory and write it straight back
    public void DeleteSave()
    {
        try
        {
            if (File.Exists(FullPath)) File.Delete(FullPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveDataManager: failed to delete '{FullPath}' ... {e.Message}");
        }

        saveSoon = false;
        saveData = CreateNewSave();
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

            // additive to v4, so a save from before it simply has none yet
            if (loaded.compendium == null) loaded.compendium = new CompendiumSaveData();

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
