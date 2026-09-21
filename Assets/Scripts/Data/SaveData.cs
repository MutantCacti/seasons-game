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
    public void PrepareForWrite()
    {
        terrain.Sort();

        foreach (ItemInventorySlot itemInventorySlot in InventoryManager.Instance.ItemInventorySlots) {
            if (inventory.ContainsKey(itemInventorySlot.ID)) {
                inventory[itemInventorySlot.ID] = itemInventorySlot.Count;
            } else {
                inventory.Add(itemInventorySlot.ID, itemInventorySlot.Count);
            }
        }
    }
}
