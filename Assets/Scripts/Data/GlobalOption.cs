using UnityEngine;


// One option the game remembers across sessions. The id is what the save file and manager key on;
// for volume it is the AudioMixer's exposed parameter name, so applying needs no extra mapping.
[CreateAssetMenu(menuName = "Global Options/Global Option")]
public class GlobalOption : ScriptableObject
{
    [SerializeField] private string optionID;
    [SerializeField] private float defaultValue = 2f;


    public string OptionID { get { return optionID; } }


    public float DefaultValue { get { return defaultValue; } }
}
