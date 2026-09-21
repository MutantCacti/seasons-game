using UnityEngine;
using UnityEngine.UI;

public class TitleScreenManager : MonoBehaviour {

    private static TitleScreenManager instance;
    public static TitleScreenManager Instance { get { return instance; } }

    public Button[] Buttons { get; private set; }


    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject optionsMenu;


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one TitleScreenManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        mainMenu.SetActive(true);
        optionsMenu.SetActive(false);
    }

    private void Start() {
        Buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
    }


    public void OnPlay()
    {
        LoadingScreenManager.Instance.LoadScene("Game");
    }

    public void OnOptions() {
        SwitchActiveMenu();
    }

    public void OnQuit()
    {
        Debug.Log("Quitting");
        Application.Quit();
    }
    
    private void SwitchActiveMenu() {
        mainMenu.SetActive(!mainMenu.activeSelf);
        optionsMenu.SetActive(!optionsMenu.activeSelf);
    }

}
