using UnityEngine;
using UnityEngine.UI;

public class TitleScreenManager : MonoBehaviour {

    private static TitleScreenManager instance;
    public static TitleScreenManager Instance { get { return instance; } }

    public Button[] Buttons { get; private set; }


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one TitleScreenManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Start() {
        Buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
    }


    public void OnPlay()
    {
        LoadingScreenManager.Instance.LoadScene("Game");
    }
    
    public void OnOptions() {
        Debug.Log("this would show options");
    }


    public void OnQuit()
    {
        Debug.Log("Quitting");
        Application.Quit();
    }

}
