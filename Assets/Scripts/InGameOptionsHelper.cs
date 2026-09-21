using UnityEngine;

public class InGameOptionsHelper : MonoBehaviour {

    [SerializeField] private GameObject optionsMenu;


    private void Awake() {
        optionsMenu.SetActive(false);
    }


    public void OnOptions() {
        optionsMenu.SetActive(!optionsMenu.activeSelf);
    }

}
