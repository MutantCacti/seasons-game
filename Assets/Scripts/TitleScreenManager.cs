using UnityEngine;
using UnityEngine.UI;

public class TitleScreenManager : MonoBehaviour {

    public static TitleScreenManager instance { get; private set; }

    public Button[] buttons { get; private set; }


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one TitleScreenManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Start() {
        buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
    }


    public void OnPlay()
    {
        LoadingScreenManager.instance.LoadScene("Game");
    }


    public void OnQuit()
    {
        Debug.Log("Quitting");
        Application.Quit();
    }

}
