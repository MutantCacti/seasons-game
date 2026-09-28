using UnityEngine;
using UnityEngine.UI;

public class TitleScreenManager : MonoBehaviour {

    private static TitleScreenManager instance;
    public static TitleScreenManager Instance { get { return instance; } }

    public Button[] Buttons { get; private set; }


    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject optionsMenu;

    // shown only when there is a save to continue; inactive, the menu's layout group closes up the gap
    [SerializeField] private GameObject continueButton;


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one TitleScreenManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        mainMenu.SetActive(true);
        optionsMenu.SetActive(false);

        // in Awake, before the first layout pass, so the menu never shows a gap closing
        if (continueButton != null) continueButton.SetActive(HasSave);
        else Debug.LogError("TitleScreenManager: no continue button assigned, so a save can only be continued through New Game's confirm", this);
    }

    private void Start() {
        Buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
    }


    // Continue: loads whatever SaveDataManager already holds
    public void OnPlay()
    {
        LoadingScreenManager.Instance.LoadScene("Game");
    }


    // New Game: with a save, confirm first, since it is deleted; without one there is nothing to lose
    public void OnNewGame() {
        if (!HasSave) {
            StartNewGame();
            return;
        }

        ConfirmationModal modal = ConfirmationModal.Instance;

        // refused rather than unconfirmed: a stray click must never wipe a save
        if (modal == null) {
            Debug.LogError("TitleScreenManager: no ConfirmationModal in the title screen, so New Game can't confirm deleting the save and does nothing", this);
            return;
        }

        modal.Show(StartNewGame);
    }


    private void StartNewGame() {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.DeleteSave();
        else Debug.LogError("TitleScreenManager: no SaveDataManager, so the old save could not be deleted", this);

        OnPlay();
    }


    private static bool HasSave {
        get { return SaveDataManager.Instance != null && SaveDataManager.Instance.HasSave; }
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
