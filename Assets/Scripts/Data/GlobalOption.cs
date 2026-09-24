using UnityEngine;
using UnityEngine.Serialization;


// One option the game remembers across sessions. The id is the AudioMixer's exposed parameter
// name, so the registry resolves it without any extra mapping.
[CreateAssetMenu(
    fileName = "GlobalOption",
    menuName = "Global Options/Global Option"
)]
public class GlobalOption : ScriptableObject
{
    [FormerlySerializedAs("optionID")] [SerializeField] private string id;
    [SerializeField] private float defaultValue = 2f;


    public string Id => id;
    public string Name => name; // Object.name, the name in the editor
    public float DefaultValue => defaultValue;
}
