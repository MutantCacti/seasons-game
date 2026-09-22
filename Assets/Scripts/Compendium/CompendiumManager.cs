using UnityEngine;

public class CompendiumManager : MonoBehaviour {

    private static CompendiumManager instance;
    public static CompendiumManager Instance { get { return instance; } }

    [SerializeField] private GameItemRegistry registry;
    

    public void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one CompendiumManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }
    


    public GameItem GetEntityInformation(int index) {

        try {
            return registry.Items[index];
        } catch {
            return null;
        }
        
    }


}
