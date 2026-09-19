using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingScreenManager : MonoBehaviour {

    public static LoadingScreenManager instance { get; private set; }


    [SerializeField] private GameObject loadingScreenCanvasGO;
    [SerializeField] private GameObject loadingScreenGO;

    public const float ANIMATION_DURATION = 0.55f; //in/out times
    private const float ANIMATION_HOLD_DURATION = 0.05f; //time loading screen is forced to stay on screen between in/out
    float elapsedTime;

    private Vector2 screenCenter = new Vector2(0, 0);
    Vector2 transitionFromPosition = Vector2.zero;


    private void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one LoadingScreenManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }



}
