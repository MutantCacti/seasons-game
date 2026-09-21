using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour {
    private static InventoryManager instance;
    public static InventoryManager Instance { get { return instance; } }

    // The pool of slots, in hierarchy order. A slot carries no item of its own: which slot shows
    // which item is decided here, and a slot is only ever active while it is bound to something.
    [SerializeField] private ItemInventorySlot[] itemInventorySlots;

    // Resolves the ids in a save back to assets
    [SerializeField] private GameItemRegistry registry;

    // inventory
    // counts and slotsByItem are kept in step so that "has a count" and "has a slot" can never
    // disagree, and slotsByItem / itemBySlot are inverses of each other.
    private readonly Dictionary<GameItem, int> counts = new();
    private readonly Dictionary<GameItem, ItemInventorySlot> slotsByItem = new();
    private readonly Dictionary<ItemInventorySlot, GameItem> itemBySlot = new();

    // Fires when a slot is taken or freed - not when the count changes on an item that already has
    // one. That is what a layout sizing itself from the occupied slot count needs.
    public event Action SlotsChanged;


    public int SlotCount { get { return itemInventorySlots != null ? itemInventorySlots.Length : 0; } }

    // One slot per distinct item, so this is the occupied slot count too
    public int ActiveSlotCount { get { return counts.Count; } }

    public bool IsFull { get { return ActiveSlotCount >= SlotCount; } }


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one InventoryManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        // nothing is held yet, so nothing is shown
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
    // Add and TryRemove are the public ways in. Both funnel through SetCount, which is the only
    // place the counts, the slot bindings and the live save blob change together - the same shape
    // as TerrainGrid.Remap.


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

            // emptied: hand the slot back so the next new item can take it
            if (isBound) {
                ReleaseSlot(item, slot);
                slotsChanged = true;
            }
        }
        else {
            if (!isBound) {
                // refuses the whole change rather than tracking an item that no slot can show
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


    // First free slot in hierarchy order, so a freed slot gets reused rather than leaving a hole.
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


    // Keeps the live blob current, so SaveDataManager.Save() is only ever a serialise and this
    // manager's lifetime stops mattering to persistence.
    private void WriteThrough(GameItem item, int quantity) {
        SaveDataManager manager = SaveDataManager.Instance;
        if (manager == null || manager.SaveData == null || manager.SaveData.inventory == null) return;

        // logs and gives up if the item is not in the registry, rather than writing a bogus key
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

        // Resolve everything before taking any slots. A dictionary that has had entries removed does
        // not enumerate in insertion order, so allocating slots as the save is read would let the bar
        // reshuffle between sessions; sorting into registry order makes the loaded layout stable.
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

            // No slot for it. The blob has to keep mirroring what is actually held, or the stale
            // entry would silently overwrite a later pickup of the same item.
            saved.Remove(item.Id);

            Debug.LogError($"InventoryManager: dropped '{item.name}' from the save because the inventory has no free slot for it", this);
        }
    }


    private int CompareByRegistryOrder(GameItem a, GameItem b) {
        int indexA = RegistryIndex(a);
        int indexB = RegistryIndex(b);

        if (indexA != indexB) return indexA.CompareTo(indexB);

        // ties only happen for items the registry does not know about, which cannot be saved anyway
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
