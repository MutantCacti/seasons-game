using UnityEngine;


// The part of the options that gets written to its own json file. A missing id falls back to the
// registry default, and a missing file is rebuilt from the registry, so old files keep loading.
[System.Serializable]
public class GlobalOptionsData
{
    public SerializableDictionary<string, float> globalOptionValues = new SerializableDictionary<string, float>();


    public void InitialiseDefaultValues(GlobalOptionsRegistry registry)
    {
        globalOptionValues.Clear();

        if (registry == null) return;

        foreach (GlobalOption option in registry.Entries)
        {
            if (option == null || string.IsNullOrEmpty(option.Id)) continue;

            globalOptionValues[option.Id] = option.DefaultValue;
        }
    }
}
