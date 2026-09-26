using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(
    fileName="GameItemRegistry",
    menuName="Items/Game Item Registry"
)]
public class GameItemRegistry : ScriptableObject, Registry<GameItem>
{
    [SerializeField] private List<GameItem> items = new List<GameItem>();

    // the editor orders the items by walking this registry's puzzle tree
    [SerializeField] private PuzzleRegistry puzzleRegistry;


    [System.NonSerialized] private Dictionary<string, GameItem> itemById;


    public IReadOnlyList<GameItem> Entries { get { return items; } }
    public int Count { get { return items != null ? items.Count : 0; } }
    public PuzzleRegistry PuzzleRegistry { get { return puzzleRegistry; } }


    // MARK: lookup


    public bool TryGetById(string id, out GameItem item)
    {
        item = null;
        if (string.IsNullOrEmpty(id)) return false;

        EnsureLookup();

        return itemById.TryGetValue(id, out item) && item != null;
    }


    public bool TryGetIdByEntry(GameItem item, out string id)
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


    // whether item is one of this registry's entries, quietly. TryGetIdByEntry is the loud version,
    // for the one caller that is about to write item into a save
    public bool IsRegistered(GameItem item)
    {
        if (item == null) return false;

        EnsureLookup();

        return !string.IsNullOrEmpty(item.Id) &&
            itemById.TryGetValue(item.Id, out GameItem registered) &&
            registered == item;
    }


    // A reagent has no tile and no prefab of its own, so the only thing it can do is react: some
    // registered item names it as a reaction's applied. An item of that shape that nothing names is
    // not a reagent, and PlacementController refuses to drag it
    public bool IsReagent(GameItem item)
    {
        if (item == null || !item.IsReagentShape) return false;
        if (items == null) return false;

        for (int i = 0; i < items.Count; i++)
        {
            GameItem candidate = items[i];
            if (candidate == null) continue;

            IReadOnlyList<ItemReaction> reactions = candidate.Reactions;
            if (reactions == null) continue;

            for (int j = 0; j < reactions.Count; j++)
            {
                ItemReaction reaction = reactions[j];
                if (reaction == null || reaction.Catalyst != item) continue;
                if (reaction.Becomes == null) continue;

                return true;
            }
        }

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
