using UnityEngine;

public class InGameOptionsHelper : MonoBehaviour {

    [SerializeField] private GameObject optionsMenu;


    private void Awake() {
        optionsMenu.SetActive(false);
        Time.timeScale = 1f;
    }

    private void OnEnable() {
        InputManager.Instance.onPause += OnPauseKey;
    }

    private void OnDisable() {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.onPause -= OnPauseKey;
        }
    }


    // Escape. It backs out of the top-most layer: modal, then menu, then book, then a focused animal,
    // and only then pauses
    private void OnPauseKey() {
        // Escape is the pause key, and it is also the modal's cancel. The modal owns Escape while
        // it is open, so a pause can never open the options menu behind it or strand it frozen.
        if (CancelModal()) return;

        // an open menu closes before the book behind it, so the book's close never runs frozen
        // under timeScale 0
        if (optionsMenu.activeSelf) {
            SetMenuOpen(false);
            return;
        }

        // open is the read target, not IsReading, so Escape during the close animation opens the menu
        CompendiumUIManager compendium = CompendiumUIManager.Instance;
        if (compendium != null && compendium.IsOpen) {
            compendium.RequestClose();
            return;
        }

        AnimalSelection selection = AnimalSelection.Instance;
        if (selection != null && selection.HasFocus) {
            selection.Clear();
            return;
        }

        SetMenuOpen(true);
    }


    // the pause button (wired in the Game scene) keeps its plain toggle, so it still pauses over an open book
    public void OnPause() {
        if (CancelModal()) return;

        SetMenuOpen(!optionsMenu.activeSelf);
    }


    private bool CancelModal() {
        if (ConfirmationModal.Instance == null || !ConfirmationModal.Instance.IsOpen) return false;

        ConfirmationModal.Instance.Cancel();
        return true;
    }


    private void SetMenuOpen(bool open) {
        optionsMenu.SetActive(open);
        Time.timeScale = open ? 0f : 1f;
    }


    public void OnTitleScreen() {
        AudioManager.Instance.StopAllAudio();
        LoadingScreenManager.Instance.LoadScene("Title Screen");
    }

}
