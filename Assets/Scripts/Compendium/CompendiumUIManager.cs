using DG.Tweening;
using UnityEngine;

public class CompendiumUIManager : MonoBehaviour {

    [SerializeField] private GameObject compendium;
    private CanvasGroup compendiumCanvasGroup;

    [SerializeField] private float fadeDuration = 0.5f;

    [SerializeField] private CompendiumEntry[] compendiumEntries;


    private void Start() {
        compendiumCanvasGroup = compendium.GetComponent<CanvasGroup>();
        compendiumCanvasGroup.alpha = 0f;
        compendium.SetActive(false);
    }


    public void OnOpenCompendium() {
        compendiumCanvasGroup.alpha = 0f;
        compendium.SetActive(true);
        compendiumCanvasGroup.DOFade(1f, fadeDuration).SetEase(Ease.InSine).WaitForCompletion(); //may need .SetUpdate(true) if we want to pause time with time scale whilst it's open
        FillCompendium();
    }


    private void FillCompendium() {
        Debug.Log("the compendium will now display all details");
        foreach (CompendiumEntry compendiumEntry in compendiumEntries) { //?? or maybe we loop through CompendiumManager
            // TODO: THIS
            // compendiumEntry.SetImage();
            // compendiumEntry.SetNameText();
            // compendiumEntry.SetDescriptionText();
        }
    }
    

    public void OnCloseCompendium() {
        compendiumCanvasGroup.alpha = 1f;
        compendium.SetActive(true);
        compendiumCanvasGroup.DOFade(0f, fadeDuration).WaitForCompletion(); //may need .SetUpdate(true) if we want to pause time with time scale whilst it's open
        compendium.SetActive(false);
    }


}
