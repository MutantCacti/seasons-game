using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Presentation only. InventoryManager decides what this slot holds, and a slot with nothing in it
// is inactive, which is also what keeps it out of the layout group's arrangement.
public class ItemInventorySlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {

    // optional; shows the item's icon over a child image so the slot's own frame sprite survives
    [SerializeField] private Image iconImage;

    [SerializeField] private GameObject tooltipGO;
    [SerializeField] private TextMeshProUGUI countText;

    // runtime only, owned by InventoryManager
    private GameItem item;
    public GameItem Item => item;

    private TextMeshProUGUI tooltipText;
    private bool validated;


    // Safe to call repeatedly with the same item: the icon and tooltip are only rewritten when the
    // item actually changes, so a count going up does not redo that work.
    public void Display(GameItem newItem, int count) {
        if (newItem == null) {
            Hide();
            return;
        }

        Validate();

        if (item != newItem) {
            item = newItem;

            if (tooltipText != null) {
                tooltipText.text = item.Name;
                tooltipGO.SetActive(false);
            }

            if (iconImage != null) iconImage.sprite = item.Icon;
        }

        gameObject.SetActive(true);

        if (countText != null) countText.text = count.ToString();
    }


    // The slot keeps its place in the hierarchy; the layout group skips inactive children, so an
    // emptied slot collapses instead of leaving a gap.
    public void Hide() {
        item = null;

        if (tooltipGO != null) tooltipGO.SetActive(false);

        gameObject.SetActive(false);
    }


    public void OnPointerEnter(PointerEventData eventData) {
        if (item == null || tooltipGO == null) return;

        tooltipGO.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (tooltipGO != null) tooltipGO.SetActive(false);
    }


    private void Validate() {
        if (validated) return;
        validated = true;

        if (tooltipGO != null) tooltipText = tooltipGO.GetComponent<TextMeshProUGUI>();

        if (countText == null) Debug.LogError($"ItemInventorySlot '{name}' has no countText assigned", this);
        if (tooltipGO == null) Debug.LogWarning($"ItemInventorySlot '{name}' has no tooltipGO assigned", this);
    }
}
