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

        SetImage(item.Icon);
        SetIconMaterial(null);
        SetNameText(item.Name);
        SetDescriptionText(item.Description);
    }


    public void Clear()
    {
        SetImage(null);
        SetIconMaterial(null);
        SetNameText(string.Empty);
        SetDescriptionText(string.Empty);
    }


    public void SetImage(Sprite sprite)
    {
        if (entryImage == null) return;

        entryImage.sprite = sprite;

        // a null sprite would otherwise draw a solid white rectangle
        entryImage.enabled = sprite != null;
    }


    public void SetIconMaterial(Material material)
    {
        if (entryImage == null) return;

        // null restores the default UI material
        entryImage.material = material;
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
