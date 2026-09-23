using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LoadingScreenManager : MonoBehaviour {

    private static LoadingScreenManager instance;
    public static LoadingScreenManager Instance { get { return instance; } }

    public bool IsLoading { get { return isLoading; } }

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

        if (SceneManager.GetActiveScene().name.Equals("Title Screen")) {
            foreach (Button button in TitleScreenManager.Instance.Buttons) {
                button.enabled = false;
            }
        }
        InputManager.Instance.DisableInput();

        RectTransform loadingScreenRT = loadingScreenGO.GetComponent<RectTransform>();
        transitionFromPosition = new Vector2(-Screen.width, 0); // slide in from the left
        transitionToPosition = new Vector2(Screen.width, 0);    // slide out to the right

        // in
        loadingScreenRT.anchoredPosition = transitionFromPosition;
        loadingScreenCanvasGO.SetActive(true);
        yield return loadingScreenRT.DOAnchorPos(screenCenter, ANIMATION_DURATION).SetEase(Ease.InOutQuad).SetUpdate(true).WaitForCompletion();

        // load behind the loading screen
        elapsedTime = 0f;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);

        // hold until the load is done AND the minimum hold has passed, otherwise the slide-out
        // runs during scene activation and the hitch makes it jump. Unscaled, because the
        // pause menu sets timeScale to 0 and a scaled timer would never finish
        while (!load.isDone || elapsedTime < ANIMATION_HOLD_DURATION) {
            elapsedTime += Time.unscaledDeltaTime;
            yield return null;
        }

        // out
        yield return loadingScreenRT.DOAnchorPos(transitionToPosition, ANIMATION_DURATION).SetEase(Ease.InOutQuad).SetUpdate(true).WaitForCompletion();
        loadingScreenCanvasGO.SetActive(false);

        InputManager.Instance.EnableInput();
        if (sceneName.Equals("Title Screen")) {
            foreach (Button button in TitleScreenManager.Instance.Buttons) {
                button.enabled = true;
            }
        }

        isLoading = false;
    }

}
