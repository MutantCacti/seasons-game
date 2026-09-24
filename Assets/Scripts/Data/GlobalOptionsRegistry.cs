using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;


[CreateAssetMenu(
    fileName="GlobalOptionsRegistry",
    menuName="Global Options/Global Options Registry"
)]
public class GlobalOptionsRegistry : ScriptableObject, Registry<GlobalOption>
{
    [FormerlySerializedAs("allGlobalOptions")] [SerializeField] private List<GlobalOption> options = new List<GlobalOption>();


    [System.NonSerialized] private Dictionary<string, GlobalOption> optionById;


    public IReadOnlyList<GlobalOption> Entries { get { return options; } }
    public int Count { get { return options != null ? options.Count : 0; } }


    // MARK: lookup


    public bool TryGetById(string id, out GlobalOption option)
    {
        option = null;
        if (string.IsNullOrEmpty(id)) return false;

        EnsureLookup();

        return optionById.TryGetValue(id, out option) && option != null;
    }


    public bool TryGetIdByEntry(GlobalOption option, out string id)
    {
        id = null;
        if (option == null) return false;

        EnsureLookup();

        if (!string.IsNullOrEmpty(option.Id) &&
            optionById.TryGetValue(option.Id, out GlobalOption registered) &&
            registered == option)
        {
            id = option.Id;
            return true;
        }

        Debug.LogError($"GlobalOptionsRegistry: '{option.name}' is not registered or its id '{option.Id}' is empty or duplicated, so it cannot be written to a save", this);

        return false;
    }


    // MARK: construction


    // the lookup goes stale whenever the editor changes the options list
    private void OnValidate()
    {
        optionById = null;
    }


    public void Rebuild()
    {
        optionById = new Dictionary<string, GlobalOption>();
        if (options == null) return;

        for (int i = 0; i < options.Count; i++)
        {
            GlobalOption option = options[i];

            if (option == null)
            {
                Debug.LogError($"GlobalOptionsRegistry: entry {i} is empty", this);
                continue;
            }

            if (string.IsNullOrEmpty(option.Id))
            {
                Debug.LogError($"GlobalOptionsRegistry: '{option.name}' has no Id, so it can never be saved or loaded", option);
                continue;
            }

            // first entry wins on duplicate
            if (optionById.TryGetValue(option.Id, out GlobalOption existing))
            {
                Debug.LogError($"GlobalOptionsRegistry: duplicate Id '{option.Id}' on '{option.name}', keeping '{existing.name}' and ignoring this one", option);
                continue;
            }

            optionById.Add(option.Id, option);
        }
    }


    private void EnsureLookup()
    {
        if (optionById != null) return;

        Rebuild();
    }
}
