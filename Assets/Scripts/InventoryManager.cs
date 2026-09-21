using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour {

    public static InventoryManager instance { get; private set; }

    [SerializeField] private ItemInventorySlot[] itemInventorySlots;
    public ItemInventorySlot[] ItemInventorySlots => itemInventorySlots;
    private Dictionary<string, ItemInventorySlot> identifiedInventorySlots = new();



    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one InventoryManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;


        foreach (ItemInventorySlot itemInventorySlot in itemInventorySlots) {
            identifiedInventorySlots.Add(itemInventorySlot.ID, itemInventorySlot);
        }

        foreach (KeyValuePair<string, int> keyValuePair in SaveDataManager.Instance.SaveData.inventory) {
            UpdateItemCount(keyValuePair.Key, keyValuePair.Value);
        }
    }


    public void UpdateItemCount(string itemID, int quantity) {
        identifiedInventorySlots[itemID].AddQuantity(quantity);
        SaveDataManager.Instance.Save();
    }

    private void SetItemCount(string itemID, int quantity) {
        identifiedInventorySlots[itemID].SetQuantity(quantity);
    }
    

}
