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
        InputManager.Instance.onPause -= OnPause;
    }


    public void OnPause() {
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
        LoadingScreenManager.Instance.LoadScene("Title Screen");
    }

}
