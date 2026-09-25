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

    // display only, never saved: units the data already holds but the slot doesn't show yet, because
    // their icons are still flying in. the slot shows count - held
    private readonly Dictionary<GameItem, int> heldDisplay = new();

    // loads show the hotbar as it is, without popping every slot
    private bool loading;

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


    // the slot an item is bound to, concealed or not
    public bool TryGetSlot(GameItem item, out ItemInventorySlot slot) {
        slot = null;

        if (item == null) return false;

        return slotsByItem.TryGetValue(item, out slot) && slot != null;
    }


    // MARK: mutations


    // deferDisplay: the data changes now, but the slot keeps showing the old count until
    // ReleaseDisplay says the flying icons have landed
    public bool Add(GameItem item, int quantity, bool deferDisplay = false) {
        if (item == null) {
            Debug.LogError("InventoryManager: tried to add a null item", this);
            return false;
        }

        if (quantity <= 0) {
            Debug.LogError($"InventoryManager: Add expects a positive quantity, got {quantity} for '{item.name}'", this);
            return false;
        }

        if (deferDisplay) heldDisplay[item] = GetHeld(item) + quantity;

        if (!SetCount(item, GetCount(item) + quantity)) {
            // nothing was added, so there is nothing to wait for
            if (deferDisplay) ClampHeld(item, GetCount(item));
            return false;
        }

        Save();
        return true;
    }


    // an icon landed: show its units
    public void ReleaseDisplay(GameItem item, int amount) {
        if (item == null || amount <= 0) return;

        int held = GetHeld(item) - amount;

        if (held > 0) heldDisplay[item] = held;
        else heldDisplay.Remove(item);

        RefreshSlot(item, true);
    }


    // the flights are gone (scene teardown, a missing flight manager), so show the real counts
    public void ReleaseAllDisplay() {
        if (heldDisplay.Count == 0) return;

        List<GameItem> items = new List<GameItem>(heldDisplay.Keys);
        heldDisplay.Clear();

        for (int i = 0; i < items.Count; i++) {
            RefreshSlot(items[i], false);
        }
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
            heldDisplay.Remove(item);

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

            // placing can take the count below what is still in flight, so held never exceeds it
            ClampHeld(item, quantity);

            ShowCount(item, slot, !loading);
        }

        WriteThrough(item, quantity);

        if (slotsChanged) SlotsChanged?.Invoke();

        return true;
    }


    private int GetHeld(GameItem item) {
        return heldDisplay.TryGetValue(item, out int held) ? held : 0;
    }


    private void ClampHeld(GameItem item, int quantity) {
        int held = GetHeld(item);

        if (held <= 0) return;

        if (quantity <= 0) heldDisplay.Remove(item);
        else if (held > quantity) heldDisplay[item] = quantity;
    }


    private void RefreshSlot(GameItem item, bool animate) {
        if (!counts.ContainsKey(item)) return;
        if (!slotsByItem.TryGetValue(item, out ItemInventorySlot slot)) return;

        ShowCount(item, slot, animate);
    }


    // a slot with nothing landed yet is concealed rather than showing 0, so it can't be dragged
    private void ShowCount(GameItem item, ItemInventorySlot slot, bool animate) {
        if (slot == null) return;

        int displayed = GetCount(item) - GetHeld(item);

        if (displayed > 0) slot.Display(item, displayed, animate);
        else slot.Conceal(item);
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
        if (manager == null || manager.SaveData == null || manager.SaveData.world == null) return;

        SerializableDictionary<string, int> saved = manager.SaveData.world.inventory;

        if (saved == null) return;

        // log and give up if the item is not in the registry
        if (registry == null || !registry.TryGetIdByEntry(item, out string id)) return;

        if (quantity == 0) saved.Remove(id);
        else saved[id] = quantity;
    }


    private void LoadFromSave() {
        loading = true;

        try {
            LoadSaved();
        }
        finally {
            loading = false;
        }
    }


    private void LoadSaved() {
        counts.Clear();
        heldDisplay.Clear();

        SaveDataManager manager = SaveDataManager.Instance;

        if (manager == null || manager.SaveData == null) {
            Debug.LogError("InventoryManager: no SaveDataManager in the scene, so the inventory starts empty", this);
            return;
        }

        if (manager.SaveData.world == null) {
            Debug.LogError("InventoryManager: the save has no world, so the inventory starts empty", this);
            return;
        }

        SerializableDictionary<string, int> saved = manager.SaveData.world.inventory;
        if (saved == null || saved.Count == 0) return;

        if (registry == null) {
            Debug.LogError("InventoryManager: no GameItemRegistry assigned, so none of the saved items can be resolved", this);
            return;
        }
        List<GameItem> resolved = new List<GameItem>();

        foreach (KeyValuePair<string, int> pair in saved) {
            if (pair.Value <= 0) continue;

            if (!registry.TryGetById(pair.Key, out GameItem item)) {
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
        IReadOnlyList<GameItem> items = registry != null ? registry.Entries : null;
        if (items == null) return int.MaxValue;

        for (int i = 0; i < items.Count; i++) {
            if (items[i] == item) return i;
        }

        return int.MaxValue;
    }
}
