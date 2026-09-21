using System;
using UnityEngine;


[System.Serializable]
public class SaveData
{
    public const int CURRENT_VERSION = 1;

    public int saveVersion;
    public long lastSaveTime;       // milliseconds since unix epoch
    public long playTimeElapsed;    // milliseconds

    // Nested rather than flattened onto this class, so each system owns a section and this stays
    // readable as inventory, quests and crops arrive
    public TerrainSaveData terrain;

    // item id -> count. Owned by InventoryManager, which writes through to it on every change
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


    // Called by SaveDataManager just before serialising. Anything that needs tidying for a clean
    // file does it here, so the manager never has to know what sections exist.
    //
    // Inventory is deliberately absent: InventoryManager writes its own counts through to
    // `inventory` as they change, so nothing has to reach back into the UI at save time.
    public void PrepareForWrite()
    {
        if (terrain != null) terrain.Sort();
    }
}
