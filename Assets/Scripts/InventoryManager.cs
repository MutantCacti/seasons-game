using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour {

    public static InventoryManager instance { get; private set; }

    [SerializeField] private ItemInventorySlot[] itemInventorySlots;
    private Dictionary<string, ItemInventorySlot> keyValuePairs = new();



    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one InventoryManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;


        foreach (ItemInventorySlot itemInventorySlot in itemInventorySlots) {
            keyValuePairs.Add(itemInventorySlot.ID, itemInventorySlot);
        }
    }


    public void UpdateItemCount(string itemID, int quantity) {
        keyValuePairs[itemID].AddQuantity(quantity);
    }
    

}
