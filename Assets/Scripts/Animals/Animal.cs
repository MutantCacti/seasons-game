using UnityEngine;


[CreateAssetMenu(
    fileName = "Animal",
    menuName = "Animals/Animal"
)]
public class Animal : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] [TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] private GameObject prefab;

    // how many of this animal the world holds at most, shared by every rule that spawns it
    [SerializeField] [Min(1)] private int mobCap = 1;

    public string Id => id;
    public string Name => name; // Object.name, the name in the editor
    public string Description => description;
    public Sprite Icon => icon;
    public GameObject Prefab => prefab;
    public int MobCap => Mathf.Max(1, mobCap);
}