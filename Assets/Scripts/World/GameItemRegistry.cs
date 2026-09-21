using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(
    fileName="GameItemRegistry",
    menuName="Items/Game Item Registry"
)]
public class GameItemRegistry : ScriptableObject
{
    [SerializeField] private List<GameItem> items = new List<GameItem>();


    [System.NonSerialized] private Dictionary<string, GameItem> itemById;


    public IReadOnlyList<GameItem> Items { get { return items; } }
    public int Count { get { return items != null ? items.Count : 0; } }


    // MARK: lookup


    public bool TryGetItemById(string id, out GameItem item)
    {
        item = null;
        if (string.IsNullOrEmpty(id)) return false;

        EnsureLookup();

        return itemById.TryGetValue(id, out item) && item != null;
    }


    public bool TryGetIdByItem(GameItem item, out string id)
    {
        id = null;
        if (item == null) return false;

        EnsureLookup();

        if (!string.IsNullOrEmpty(item.Id) &&
            itemById.TryGetValue(item.Id, out GameItem registered) &&
            registered == item)
        {
            id = item.Id;
            return true;
        }

        Debug.LogError($"GameItemRegistry: '{item.name}' is not registered or its id '{item.Id}' is empty or duplicated, so it cannot be written to a save", this);

        return false;
    }


    // MARK: construction


    // the lookup goes stale whenever the editor changes the items list
    private void OnValidate()
    {
        itemById = null;
    }


    public void Rebuild()
    {
        itemById = new Dictionary<string, GameItem>();
        if (items == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            GameItem item = items[i];

            if (item == null)
            {
                Debug.LogError($"GameItemRegistry: entry {i} is empty", this);
                continue;
            }

            if (string.IsNullOrEmpty(item.Id))
            {
                Debug.LogError($"GameItemRegistry: '{item.name}' has no Id, so it can never be saved or loaded", item);
                continue;
            }

            // first entry wins on duplicate
            if (itemById.TryGetValue(item.Id, out GameItem existing))
            {
                Debug.LogError($"GameItemRegistry: duplicate Id '{item.Id}' on '{item.name}', keeping '{existing.name}' and ignoring this one", item);
                continue;
            }

            itemById.Add(item.Id, item);
        }
    }


    private void EnsureLookup()
    {
        if (itemById != null) return;

        Rebuild();
    }
}
