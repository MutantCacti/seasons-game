using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class ItemInventorySlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Image iconImage;

    [SerializeField] private GameObject tooltipGO;
    [SerializeField] private TextMeshProUGUI countText;

    // runtime only, owned by InventoryManager
    private GameItem item;
    public GameItem Item => item;

    private TextMeshProUGUI tooltipText;
    private bool validated;


    // PlacementController subscribes to these
    public event Action<ItemInventorySlot, PointerEventData> DragBegan;
    public event Action<ItemInventorySlot, PointerEventData> Dragged;
    public event Action<ItemInventorySlot, PointerEventData> DragEnded;


    // idempotent
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


    public void OnBeginDrag(PointerEventData eventData) {
        if (item == null) return;

        if (tooltipGO != null) tooltipGO.SetActive(false);

        DragBegan?.Invoke(this, eventData);
    }


    public void OnDrag(PointerEventData eventData) {
        Dragged?.Invoke(this, eventData);
    }


    public void OnEndDrag(PointerEventData eventData) {
        DragEnded?.Invoke(this, eventData);
    }


    private void Validate() {
        if (validated) return;
        validated = true;

        if (countText == null) Debug.LogError($"ItemInventorySlot '{name}' has no countText assigned", this);

        if (tooltipGO == null) {
            Debug.LogWarning($"ItemInventorySlot '{name}' has no tooltipGO assigned", this);
            return;
        }

        // the tooltip is the panel that sizes itself around the text, so the text is its child
        tooltipText = tooltipGO.GetComponentInChildren<TextMeshProUGUI>(true);

        if (tooltipText == null) {
            Debug.LogError($"ItemInventorySlot '{name}': '{tooltipGO.name}' has no TextMeshProUGUI in its children", this);
        }
    }
}
