using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleScreenManager : MonoBehaviour {

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
