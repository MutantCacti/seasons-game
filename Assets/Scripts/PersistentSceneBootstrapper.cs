using UnityEngine;
using UnityEngine.SceneManagement;

public class PersistentSceneBootstrapper : MonoBehaviour {

    [SerializeField] private string firstSceneToLoad;

    private static bool isInitialised = false;

    private void Awake() {
        if (isInitialised) {
            Debug.LogWarning("Found more than one PersistentSceneBootstrapper in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        isInitialised = true;

        QualitySettings.vSyncCount = 1;

        SceneManager.LoadScene(firstSceneToLoad);
    }

}