using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadingScreenManager : MonoBehaviour {

    public static LoadingScreenManager instance { get; private set; }


    [SerializeField] private GameObject loadingScreenCanvasGO;
    [SerializeField] private GameObject loadingScreenGO;

    public const float ANIMATION_DURATION = 0.55f; //in/out times
    private const float ANIMATION_HOLD_DURATION = 0.05f; //time loading screen is forced to stay on screen between in/out
    float elapsedTime;

    private Vector2 screenCenter = new Vector2(0, 0);
    Vector2 transitionFromPosition = Vector2.zero;
    Vector2 transitionToPosition = Vector2.zero;

    private bool isLoading = false;


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one LoadingScreenManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
        loadingScreenCanvasGO.SetActive(false);
    }


    public void LoadScene(string sceneName) {
        if (isLoading) {
            Debug.LogWarning($"Already loading a scene, ignoring request to load {sceneName}");
            return;
        }

        StartCoroutine(LoadSceneRoutine(sceneName));
    }


    private IEnumerator LoadSceneRoutine(string sceneName) {
        isLoading = true;

        
        InputManager.Instance.DisableInput();

        RectTransform loadingScreenRT = loadingScreenGO.GetComponent<RectTransform>();
        transitionFromPosition = new Vector2(-Screen.width, 0); // slide in from the left
        transitionToPosition = new Vector2(Screen.width, 0);    // slide out to the right

        // in
        loadingScreenRT.anchoredPosition = transitionFromPosition;
        loadingScreenCanvasGO.SetActive(true);
        yield return loadingScreenRT.DOAnchorPos(screenCenter, ANIMATION_DURATION).SetEase(Ease.InOutQuad).WaitForCompletion();

        // load behind the loading screen
        elapsedTime = 0f;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);

        while (!load.isDone || elapsedTime < ANIMATION_HOLD_DURATION) {
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // out
        yield return loadingScreenRT.DOAnchorPos(transitionToPosition, ANIMATION_DURATION).SetEase(Ease.InOutQuad).WaitForCompletion();
        loadingScreenCanvasGO.SetActive(false);

        InputManager.Instance.EnableInput();
        if (sceneName.Equals("Title Screen")) {
            foreach (Button button in TitleScreenManager.instance.buttons) {
                button.enabled = true;
            }
        }

        isLoading = false;
    }

}
