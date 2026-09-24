using UnityEngine;


// The part of the options that gets written to its own json file. A missing id falls back to the
// database default, and a missing file is rebuilt from the database, so old files keep loading.
[System.Serializable]
public class GlobalOptionsData
{
    public SerializableDictionary<string, float> globalOptionValues = new SerializableDictionary<string, float>();


    public void InitialiseDefaultValues(GlobalOptionsDatabase database)
    {
        globalOptionValues.Clear();

        if (database == null) return;

        foreach (GlobalOption option in database.AllGlobalOptions)
        {
            if (option == null || string.IsNullOrEmpty(option.OptionID)) continue;

            globalOptionValues[option.OptionID] = option.DefaultValue;
        }
    }
}
