using DG.Tweening;
using UnityEngine;

public class CompendiumUIManager : MonoBehaviour {

    private static CompendiumUIManager instance;
    public static CompendiumUIManager Instance{ get { return instance; }}

    [SerializeField] private GameObject compendium;
    private CanvasGroup compendiumCanvasGroup;

    [SerializeField] private float fadeDuration = 0.5f;

    [SerializeField] private CompendiumEntry[] compendiumEntries;


    public void Awake() {
        if (instance != null && instance != this) {
            Debug.LogWarning("Found more than one CompendiumUIManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Start() {
        compendiumCanvasGroup = compendium.GetComponent<CanvasGroup>();
        compendiumCanvasGroup.alpha = 0f;
        compendium.SetActive(false);
    }


    public void OnOpenCloseCompendium(bool isReading) {
        if (isReading) {
            OnOpenCompendium();
        } else {
            OnCloseCompendium();
        }
    }


    public void OnOpenCompendium() {
        compendiumCanvasGroup.alpha = 0f;
        compendium.SetActive(true);
        compendiumCanvasGroup.DOFade(1f, fadeDuration).SetEase(Ease.InQuad).WaitForCompletion(); //may need .SetUpdate(true) if we want to pause time with time scale whilst it's open
        FillCompendium();
    }


    private void FillCompendium() {
        Debug.Log("the compendium will now display all details");
        // foreach (CompendiumEntry compendiumEntry in compendiumEntries) { //?? or maybe we loop through CompendiumManager
        //     // TODO: THIS
        //     // compendiumEntry.SetImage();
        //     // compendiumEntry.SetNameText();
        //     // compendiumEntry.SetDescriptionText();

        //     CompendiumManager.Instance.Thing(0);
        // }


        for (int i = 0; i < compendiumEntries.Length; i++) {
            GameItem gameItem = CompendiumManager.Instance.GetEntityInformation(i);
            if (gameItem == null) {
                compendiumEntries[i].gameObject.SetActive(false);
                continue;
            }

            compendiumEntries[i].SetImage(gameItem.Icon);
            compendiumEntries[i].SetNameText(gameItem.name);
            compendiumEntries[i].SetDescriptionText(gameItem.Description);
            compendiumEntries[i].gameObject.SetActive(true);
        }

    }
    

    public void OnCloseCompendium() {
        compendiumCanvasGroup.alpha = 1f;
        compendium.SetActive(true);
        compendiumCanvasGroup.DOFade(0f, fadeDuration).WaitForCompletion(); //may need .SetUpdate(true) if we want to pause time with time scale whilst it's open
        compendium.SetActive(false);
    }


}
