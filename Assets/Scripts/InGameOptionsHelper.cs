using UnityEngine;

public class InGameOptionsHelper : MonoBehaviour {

    [SerializeField] private GameObject optionsMenu;


    private void Awake() {
        optionsMenu.SetActive(false);
    }

    private void OnEnable() {
        InputManager.Instance.onPause += OnPause;
    }

    private void OnDisable() {
        InputManager.Instance.onPause -= OnPause;
    }


    private void OnPause() {
        OnOptions(); //temporary functionality

        if (optionsMenu.activeSelf) {
            Time.timeScale = 0f;
        } else {
            Time.timeScale = 1f;
        }
    }


    public void OnOptions() {
        optionsMenu.SetActive(!optionsMenu.activeSelf);
    }

}
