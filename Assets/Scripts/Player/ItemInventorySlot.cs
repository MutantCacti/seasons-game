using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class ItemInventorySlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, ICursorHoverTarget
{
    [SerializeField] private Image iconImage;

    [SerializeField] private GameObject tooltipGO;
    [SerializeField] private TextMeshProUGUI countText;

    // MARK: feel
    [SerializeField] private float popDuration = 0.35f;
    [SerializeField] private float pulseStrength = 0.18f;
    [SerializeField] private float pulseDuration = 0.25f;

    // runtime only, owned by InventoryManager
    private GameItem item;
    public GameItem Item => item;

    private TextMeshProUGUI tooltipText;
    private bool validated;

    // concealed = bound to an item whose first icon is still flying in. scale 0 rather than
    // inactive, so the layout group keeps its space and the flight has a real rect to aim at
    private bool concealed;
    private int shownCount;
    private Tween scaleTween;

    public bool IsConcealed => concealed;


    // an empty or hidden slot is nothing to drag, so it gets no hand cursor
    public bool WantsPointer => item != null && !concealed && isActiveAndEnabled;


    // PlacementController subscribes to these
    public event Action<ItemInventorySlot, PointerEventData> DragBegan;
    public event Action<ItemInventorySlot, PointerEventData> Dragged;
    public event Action<ItemInventorySlot, PointerEventData> DragEnded;


    // idempotent. animate pops a slot that is new (or was concealed) and pulses a changed count,
    // up or down. loads pass false so a reloaded hotbar doesn't bounce
    public void Display(GameItem newItem, int count, bool animate = false) {
        if (newItem == null) {
            Hide();
            return;
        }

        bool isNew = concealed || !gameObject.activeSelf || item != newItem;
        bool countChanged = count != shownCount;

        Bind(newItem);

        gameObject.SetActive(true);
        concealed = false;
        shownCount = count;

        if (countText != null) countText.text = count.ToString();

        if (!animate) {
            if (isNew) SetScale(Vector3.one);
            return;
        }

        if (isNew) PopIn();
        else if (countChanged) Pulse();
    }


    // held at scale 0 until the first flying icon lands and Display pops it in
    public void Conceal(GameItem newItem) {
        if (newItem == null) {
            Hide();
            return;
        }

        Bind(newItem);

        gameObject.SetActive(true);
        concealed = true;
        shownCount = 0;

        if (tooltipGO != null) tooltipGO.SetActive(false);

        SetScale(Vector3.zero);
    }


    public void Hide() {
        item = null;
        concealed = false;
        shownCount = 0;

        if (tooltipGO != null) tooltipGO.SetActive(false);

        SetScale(Vector3.one);

        gameObject.SetActive(false);
    }


    // MARK: feel


    private void PopIn() {
        KillScale();

        transform.localScale = Vector3.zero;
        scaleTween = transform.DOScale(Vector3.one, popDuration)
            .SetEase(Ease.OutBack)
            .SetLink(gameObject);
    }


    // from the resting size, never from zero, so it reads as a bump rather than a respawn
    private void Pulse() {
        KillScale();

        transform.localScale = Vector3.one;
        scaleTween = transform.DOPunchScale(Vector3.one * pulseStrength, pulseDuration, 1, 0.5f)
            .SetLink(gameObject);
    }


    private void SetScale(Vector3 scale) {
        KillScale();

        transform.localScale = scale;
    }


    private void KillScale() {
        if (scaleTween != null && scaleTween.IsActive()) scaleTween.Kill();
        scaleTween = null;
    }


    private void Bind(GameItem newItem) {
        Validate();

        if (item == newItem) return;

        item = newItem;

        if (tooltipText != null) {
            tooltipText.text = item.Name;
            tooltipGO.SetActive(false);
        }

        if (iconImage != null) iconImage.sprite = item.Icon;
    }


    public void OnPointerEnter(PointerEventData eventData) {
        if (item == null || concealed || tooltipGO == null) return;

        tooltipGO.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (tooltipGO != null) tooltipGO.SetActive(false);
    }


    public void OnBeginDrag(PointerEventData eventData) {
        if (item == null || concealed) return;

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
