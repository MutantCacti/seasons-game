using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class CompendiumEntry : MonoBehaviour
{
    [SerializeField] private Image entryImage;
    [SerializeField] private TextMeshProUGUI entryNameText;
    [SerializeField] private TextMeshProUGUI entryDescriptionText;


    public void SetItem(GameItem item)
    {
        if (item == null)
        {
            Clear();
            return;
        }

        if (item.Icon != null) SetImage(item.Icon);

        SetNameText(item.Name);
        SetDescriptionText(item.Description);
    }


    public void Clear()
    {
        SetNameText(string.Empty);
        SetDescriptionText(string.Empty);
    }


    public void SetImage(Sprite sprite)
    {
        if (entryImage == null) return;

        entryImage.sprite = sprite;
    }

    public void SetNameText(string name)
    {
        if (entryNameText == null) return;

        entryNameText.text = name;
    }

    public void SetDescriptionText(string description)
    {
        if (entryDescriptionText == null) return;

        entryDescriptionText.text = description;
    }
}
