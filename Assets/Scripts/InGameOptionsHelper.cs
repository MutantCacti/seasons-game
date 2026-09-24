using UnityEngine;

public class InGameOptionsHelper : MonoBehaviour {

    [SerializeField] private GameObject optionsMenu;


    private void Awake() {
        optionsMenu.SetActive(false);
        Time.timeScale = 1f;
    }

    private void OnEnable() {
        InputManager.Instance.onPause += OnPause;
    }

    private void OnDisable() {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.onPause -= OnPause;
        }
    }


    public void OnPause() {
        // Escape is the pause key, and it is also the modal's cancel. The modal owns Escape while
        // it is open, so a pause can never open the options menu behind it or strand it frozen.
        if (ConfirmationModal.Instance != null && ConfirmationModal.Instance.IsOpen) {
            ConfirmationModal.Instance.Cancel();
            return;
        }

        OnOptions(); //temporary functionality

        if (optionsMenu.activeSelf) {
            Time.timeScale = 0f;
        } else {
            Time.timeScale = 1f;
        }
    }


    private void OnOptions() {
        optionsMenu.SetActive(!optionsMenu.activeSelf);
    }

    public void OnTitleScreen() {
        AudioManager.Instance.StopAllAudio();
        LoadingScreenManager.Instance.LoadScene("Title Screen");
    }

}
