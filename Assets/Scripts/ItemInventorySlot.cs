using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class ItemInventorySlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {

    [SerializeField] private string id;
    public string ID => id;

    [SerializeField] private string displayName;
    [SerializeField] private GameObject tooltipGO;

    [SerializeField] private TextMeshProUGUI countText;
    private int count = 0;
    public int Count => count;


    private void Awake() {
        tooltipGO.SetActive(false);
        tooltipGO.GetComponent<TextMeshProUGUI>().text = displayName;

        UpdateDisplay();
    }


    public void OnPointerEnter(PointerEventData eventData) {
        tooltipGO.SetActive(true);
    }
    
    public void OnPointerExit(PointerEventData eventData) {
        tooltipGO.SetActive(false);
    }


    public void AddQuantity(int quantity) {
        count += quantity;
        if (count < 0) {
            Debug.LogError($"QUANTITY OF ${displayName} IS NEGATIVE!! {count}");
        }

        UpdateDisplay();
    }

    public void SetQuantity(int quantity) {
        count = quantity;
        if (count < 0) {
            Debug.LogError($"QUANTITY OF ${displayName} IS NEGATIVE!! {count}");
        }

        UpdateDisplay();
    }

    private void UpdateDisplay() {
        gameObject.SetActive(count > 0);
        countText.text = count.ToString();
    }


}
