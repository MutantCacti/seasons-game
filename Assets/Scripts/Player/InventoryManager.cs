using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour {
    private static InventoryManager instance;
    public static InventoryManager Instance { get { return instance; } }

    [SerializeField] private ItemInventorySlot[] itemInventorySlots;

    // Resolves the ids in a save back to assets
    [SerializeField] private GameItemRegistry registry;

    // inventory
    private readonly Dictionary<GameItem, int> counts = new();
    private readonly Dictionary<GameItem, ItemInventorySlot> slotsByItem = new();
    private readonly Dictionary<ItemInventorySlot, GameItem> itemBySlot = new();

    public event Action SlotsChanged;

    public event Action<GameItem, int> ItemConsumed;

    public int SlotCount { get { return itemInventorySlots != null ? itemInventorySlots.Length : 0; } }

    public IReadOnlyList<ItemInventorySlot> Slots { get { return itemInventorySlots; } }

    // One slot per distinct item
    public int ActiveSlotCount { get { return counts.Count; } }

    public bool IsFull { get { return ActiveSlotCount >= SlotCount; } }


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one InventoryManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        HideAllSlots();
        LoadFromSave();
    }


    // MARK: queries


    public int GetCount(GameItem item) {
        if (item == null) return 0;

        return counts.TryGetValue(item, out int count) ? count : 0;
    }


    public bool Has(GameItem item, int quantity = 1) {
        return quantity > 0 && GetCount(item) >= quantity;
    }


    // MARK: mutations


    public bool Add(GameItem item, int quantity) {
        if (item == null) {
            Debug.LogError("InventoryManager: tried to add a null item", this);
            return false;
        }

        if (quantity <= 0) {
            Debug.LogError($"InventoryManager: Add expects a positive quantity, got {quantity} for '{item.name}'", this);
            return false;
        }

        if (!SetCount(item, GetCount(item) + quantity)) return false;

        Save();
        return true;
    }


    public bool TryRemove(GameItem item, int quantity) {
        if (item == null || quantity <= 0) return false;
        if (!Has(item, quantity)) return false;

        if (!SetCount(item, GetCount(item) - quantity)) return false;

        ItemConsumed?.Invoke(item, quantity);

        Save();
        return true;
    }


    public void Save() {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
    }


    // MARK: internals


    private bool SetCount(GameItem item, int quantity) {
        if (item == null) {
            Debug.LogError("InventoryManager: tried to set the count of a null item", this);
            return false;
        }

        if (quantity < 0) {
            Debug.LogError($"InventoryManager: tried to set the count of '{item.name}' to {quantity}", this);
            return false;
        }

        bool isBound = slotsByItem.TryGetValue(item, out ItemInventorySlot slot);
        bool slotsChanged = false;

        if (quantity == 0) {
            counts.Remove(item);

            // free up the slot so the next new item can take it
            if (isBound) {
                ReleaseSlot(item, slot);
                slotsChanged = true;
            }
        }
        else {
            if (!isBound) {
                // cannot give a slot to an item that does not exist
                if (!TryTakeSlot(item, out slot)) return false;

                slotsChanged = true;
            }

            counts[item] = quantity;

            if (slot != null) slot.Display(item, quantity);
        }

        WriteThrough(item, quantity);

        if (slotsChanged) SlotsChanged?.Invoke();

        return true;
    }


    // First free slot in hierarchy order (ascending index)
    private bool TryTakeSlot(GameItem item, out ItemInventorySlot slot) {
        slot = null;

        if (itemInventorySlots != null) {
            for (int i = 0; i < itemInventorySlots.Length; i++) {
                ItemInventorySlot candidate = itemInventorySlots[i];

                if (candidate == null) continue;
                if (itemBySlot.ContainsKey(candidate)) continue;

                slot = candidate;
                break;
            }
        }

        if (slot == null) {
            Debug.LogError($"InventoryManager: '{item.name}' needs a slot but all {SlotCount} are in use, so the inventory cannot hold {ActiveSlotCount + 1} different items. The container needs more slots.", this);
            return false;
        }

        slotsByItem.Add(item, slot);
        itemBySlot.Add(slot, item);

        return true;
    }


    private void ReleaseSlot(GameItem item, ItemInventorySlot slot) {
        slotsByItem.Remove(item);

        if (slot == null) return;

        itemBySlot.Remove(slot);
        slot.Hide();
    }


    private void HideAllSlots() {
        if (itemInventorySlots == null) return;

        for (int i = 0; i < itemInventorySlots.Length; i++) {
            if (itemInventorySlots[i] != null) itemInventorySlots[i].Hide();
        }
    }


    // Write through the SaveDataManager's live loaded save rather than directly to disk
    private void WriteThrough(GameItem item, int quantity) {
        SaveDataManager manager = SaveDataManager.Instance;
        if (manager == null || manager.SaveData == null || manager.SaveData.inventory == null) return;

        // log and give up if the item is not in the registry
        if (registry == null || !registry.TryGetIdByItem(item, out string id)) return;

        if (quantity == 0) manager.SaveData.inventory.Remove(id);
        else manager.SaveData.inventory[id] = quantity;
    }


    private void LoadFromSave() {
        counts.Clear();

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null) {
            Debug.LogError("InventoryManager: no SaveDataManager in the scene, so the inventory starts empty", this);
            return;
        }

        SerializableDictionary<string, int> saved = manager.SaveData.inventory;
        if (saved == null || saved.Count == 0) return;

        if (registry == null) {
            Debug.LogError("InventoryManager: no GameItemRegistry assigned, so none of the saved items can be resolved", this);
            return;
        }
        List<GameItem> resolved = new List<GameItem>();

        foreach (KeyValuePair<string, int> pair in saved) {
            if (pair.Value <= 0) continue;

            if (!registry.TryGetItemById(pair.Key, out GameItem item)) {
                Debug.LogError($"InventoryManager: the save holds '{pair.Key}', but no GameItem in '{registry.name}' has that id - dropping it", this);
                continue;
            }

            resolved.Add(item);
        }

        resolved.Sort(CompareByRegistryOrder);

        foreach (GameItem item in resolved) {
            if (SetCount(item, saved[item.Id])) continue;

            // a save with more items than there are slots for is obviously corrupt
            // so since we can only display so many items, we remove the extra ones
            saved.Remove(item.Id);

            Debug.LogError($"InventoryManager: dropped '{item.name}' from the save because the inventory has no free slot for it", this);
        }
    }


    private int CompareByRegistryOrder(GameItem a, GameItem b) {
        int indexA = RegistryIndex(a);
        int indexB = RegistryIndex(b);

        if (indexA != indexB) return indexA.CompareTo(indexB);

        return string.CompareOrdinal(a.Id, b.Id);
    }


    private int RegistryIndex(GameItem item) {
        IReadOnlyList<GameItem> items = registry != null ? registry.Items : null;
        if (items == null) return int.MaxValue;

        for (int i = 0; i < items.Count; i++) {
            if (items[i] == item) return i;
        }

        return int.MaxValue;
    }
}
