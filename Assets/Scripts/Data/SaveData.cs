using System;
using UnityEngine;


[System.Serializable]
public class SaveData
{
    public const int CURRENT_VERSION = 1;

    public int saveVersion;
    public long lastSaveTime;       // milliseconds since unix epoch
    public long playTimeElapsed;    // milliseconds


    public TerrainSaveData terrain;


    public SerializableDictionary<string, int> inventory;


    // default constructor
    public SaveData()
    {
        saveVersion = CURRENT_VERSION;
        lastSaveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        playTimeElapsed = 0L;
        terrain = new TerrainSaveData();

        inventory = new SerializableDictionary<string, int>();
    }


    public void PrepareForWrite()
    {
        if (terrain != null) terrain.Sort();
    }
}
