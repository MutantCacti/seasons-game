using UnityEngine;



public class CompendiumManager : MonoBehaviour
{
    private static CompendiumManager instance;
    public static CompendiumManager Instance { get { return instance; } }


    [SerializeField] private GameItemRegistry registry;


    public void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one CompendiumManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (registry == null) Debug.LogError("CompendiumManager: no GameItemRegistry, so the book has nothing to show", this);
    }


    public int Count { get { return registry != null ? registry.Count : 0; } }


    // False for a null registry, an out of range index or a hole in the list
    public bool TryGetItem(int index, out GameItem item)
    {
        item = null;

        if (registry == null) return false;
        if (index < 0 || index >= registry.Count) return false;

        item = registry.Items[index];

        return item != null;
    }


    public GameItem GetItem(int index)
    {
        TryGetItem(index, out GameItem item);

        return item;
    }


    public GameItem GetEntityInformation(int index)
    {
        return GetItem(index);
    }
}
