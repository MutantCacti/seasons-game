using System;
using TMPro;
using UnityEngine;

public class ItemInventorySlot : MonoBehaviour {

    [SerializeField] private string id;
    public string ID => id;
    [SerializeField] private string displayName;
    [SerializeField] private TextMeshProUGUI countText;
    private int count = 0;


    public void AddQuantity(int quantity) {
        count += quantity;

        if (count > 0) {
            gameObject.SetActive(true);
        } else {
            gameObject.SetActive(false);

            if (count < 0) {
                Debug.LogError($"QUANTITY OF ${displayName} IS NEGATIVE!! {count}");
            }
        }

        countText.text = count.ToString();
    }


}
