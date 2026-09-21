using System;
using TMPro;
using UnityEngine;

public class ItemInventorySlot : MonoBehaviour {

    [SerializeField] private string id;
    public string ID => id;
    [SerializeField] private string displayName;
    [SerializeField] private TextMeshProUGUI countText;
    private int count = 0;
    public int Count => count;


    private void Awake() {
        UpdateDisplay();
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
