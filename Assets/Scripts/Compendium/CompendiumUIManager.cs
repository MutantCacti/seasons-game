using DG.Tweening;
using UnityEngine;

public class CompendiumUIManager : MonoBehaviour {

    [SerializeField] private GameObject compendium;
    private CanvasGroup compendiumCanvasGroup;

    [SerializeField] private float fadeDuration = 0.5f;

    private void Start() {
        compendiumCanvasGroup = compendium.GetComponent<CanvasGroup>();
        compendiumCanvasGroup.alpha = 0f;
        compendium.SetActive(false);
    }


    public void OnOpenCompendium() {
        compendiumCanvasGroup.alpha = 0f;
        compendium.SetActive(true);
        compendiumCanvasGroup.DOFade(1f, fadeDuration).SetEase(Ease.InSine); //may need .SetUpdate(true) if we want to pause time with time scale whilst it's open
    }
    

}
