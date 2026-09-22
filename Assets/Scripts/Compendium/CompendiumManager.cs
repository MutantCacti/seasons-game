using UnityEngine;

public class CompendiumManager : MonoBehaviour {

    private static CompendiumManager instance;
    public static CompendiumManager Instance { get { return instance; } }
    

    public void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one CompendiumManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }
    


    public string GetEntityInformation(int index) {

        try {
            return SaveDataManager.Instance.SaveData.entities.entities[index].itemId;
        } catch {
            return "";
        }
        // foreach (EntitySave entity in SaveDataManager.Instance.SaveData.entities.entities) {
            
        // }
    }


}
