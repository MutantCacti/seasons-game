using UnityEngine;
using UnityEngine.UI;


[RequireComponent(typeof(RectTransform))]
public class InventoryContainer : MonoBehaviour
{
    [SerializeField] private InventoryManager inventory;

    [SerializeField] private HorizontalOrVerticalLayoutGroup layoutGroup;

    private RectTransform rectTransform;
    private float slotWidth;
    private bool measured;


    private void Awake()
    {
        rectTransform = transform as RectTransform;

        if (inventory == null) inventory = GetComponent<InventoryManager>();
        if (layoutGroup == null) layoutGroup = GetComponent<HorizontalOrVerticalLayoutGroup>();
    }


    private void OnEnable()
    {
        if (inventory != null) inventory.SlotsChanged += Resize;

        Resize();
    }


    private void OnDisable()
    {
        if (inventory != null) inventory.SlotsChanged -= Resize;
    }


    private void Start()
    {
        Resize();
    }


    // MARK: resize


    // runs whenever an item takes or frees a slot
    public void Resize()
    {
        if (!Validate()) return;

        int slotCount = inventory.ActiveSlotCount;
        float gaps = slotCount > 1 ? (slotCount - 1) * layoutGroup.spacing : 0f;

        // n slots have n - 1 gaps
        float width = layoutGroup.padding.left + layoutGroup.padding.right + slotCount * slotWidth + gaps;
        // extra padding for the far ends
        width += layoutGroup.spacing;

        Vector2 size = rectTransform.sizeDelta;
        if (Mathf.Approximately(size.x, width)) return;

        rectTransform.sizeDelta = new Vector2(width, size.y);
    }


    // MARK: internals


    private bool Validate()
    {
        if (rectTransform == null)
        {
            Debug.LogError($"InventoryContainer '{name}' has no RectTransform, so it cannot be resized", this);
            return false;
        }

        if (inventory == null)
        {
            Debug.LogError($"InventoryContainer '{name}' has no InventoryManager, so there is no slot count to resize to", this);
            return false;
        }

        if (layoutGroup == null)
        {
            Debug.LogError($"InventoryContainer '{name}' has no HorizontalOrVerticalLayoutGroup, so the spacing between slots is unknown", this);
            return false;
        }

        if (!measured) MeasureSlotWidth();

        if (!measured)
        {
            Debug.LogError($"InventoryContainer '{name}' has no ItemInventorySlot child to measure a slot width from", this);
            return false;
        }

        return true;
    }


    private void MeasureSlotWidth()
    {
        ItemInventorySlot[] slots = GetComponentsInChildren<ItemInventorySlot>(true);

        if (slots == null || slots.Length == 0) return;

        if (slots[0].transform is RectTransform slotRect)
        {
            slotWidth = slotRect.sizeDelta.x;
            measured = slotWidth > 0f;
        }
    }
}
