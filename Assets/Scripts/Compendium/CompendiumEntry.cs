using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CompendiumEntry : MonoBehaviour {

    [SerializeField] private Image entryImage;
    [SerializeField] private TextMeshProUGUI entryNameText;
    [SerializeField] private TextMeshProUGUI entryDescriptionText;


    public void SetImage(Sprite sprite) {
        entryImage.sprite = sprite;
    }

    public void SetNameText(string name) {
        entryNameText.text = name;
    }
    
    public void SetDescriptionText(string description) {
        entryNameText.text = description;
    }

}
