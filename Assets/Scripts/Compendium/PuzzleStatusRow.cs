using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


// The status line under a puzzle entry: an icon button and a line of text. Sits on the entry root
// next to CompendiumEntry and UnseenHighlight, and is driven entirely by PuzzleTabManager.
public class PuzzleStatusRow : MonoBehaviour
{
    [SerializeField] private Button statusButton;
    [SerializeField] private Image statusIcon;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Fail pulse")]
    [SerializeField] private float pulseScale = 1.15f;
    [SerializeField] private float pulseDuration = 0.5f;
    [SerializeField] private float pulseBrighten = 0.5f;

    private Action onClick;
    private Color baseTint = Color.white;
    private Tween scaleTween;
    private Tween colourTween;

    // The wanted state, kept apart from the tweens: the book binds its pages while its canvas is
    // still inactive and flips faces off and on while turning, so the pulse is (re)started from
    // OnEnable and Awake must not reset any of this
    private bool pulsing;


    private void Awake()
    {
        if (statusButton == null) Debug.LogError("PuzzleStatusRow: no status Button, so the puzzle cannot be acted on from the book", this);
        if (statusIcon == null) Debug.LogError("PuzzleStatusRow: no status icon Image, so the puzzle's state has no icon", this);
        if (statusText == null) Debug.LogError("PuzzleStatusRow: no status text, so the puzzle's state is not described", this);

        if (statusButton != null) statusButton.onClick.AddListener(OnClicked);
    }


    private void OnEnable()
    {
        if (pulsing) StartPulse();
    }


    private void OnDisable()
    {
        StopPulse();
    }


    private void OnDestroy()
    {
        StopPulse();

        if (statusButton != null) statusButton.onClick.RemoveListener(OnClicked);
    }


    // MARK: setters


    // Stops any pulse first, so a recycled entry never keeps the last puzzle's colour or scale
    public void SetStatus(Sprite sprite, Color tint)
    {
        StopPulse();

        baseTint = tint;

        if (statusIcon == null) return;

        statusIcon.sprite = sprite;
        statusIcon.color = tint;

        // a null sprite would otherwise draw a solid white rectangle
        statusIcon.enabled = sprite != null;
    }


    public void SetText(string text)
    {
        if (statusText == null) return;

        // left active even when empty, so the row keeps its height and the layout does not jump
        statusText.text = text ?? string.Empty;
    }


    public void SetInteractable(bool interactable)
    {
        if (statusButton != null) statusButton.interactable = interactable;
    }


    // Replaces any previously stored callback. Entries are recycled across pages, so a replaced
    // callback is safer than listeners that would pile up.
    public void SetOnClick(Action callback)
    {
        onClick = callback;
    }


    public void SetPulsing(bool pulsing)
    {
        this.pulsing = pulsing;

        StopPulse();

        if (pulsing && isActiveAndEnabled) StartPulse();
    }


    public void Clear()
    {
        SetPulsing(false);
        SetStatus(null, Color.white);
        SetText(string.Empty);
        SetInteractable(false);
        SetOnClick(null);
    }


    private void OnClicked()
    {
        if (onClick != null) onClick();
    }


    // MARK: pulse


    // Scaled time on purpose: this lives in the book, and a pause freezes the book too
    private void StartPulse()
    {
        StopPulse();

        if (statusButton != null)
        {
            scaleTween = statusButton.transform
                .DOScale(pulseScale, pulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        if (statusIcon != null)
        {
            Color bright = Color.Lerp(baseTint, Color.white, pulseBrighten);
            bright.a = baseTint.a;

            colourTween = statusIcon
                .DOColor(bright, pulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }
    }


    private void StopPulse()
    {
        KillTween(ref scaleTween);
        KillTween(ref colourTween);

        if (statusButton != null) statusButton.transform.localScale = Vector3.one;
        if (statusIcon != null) statusIcon.color = baseTint;
    }


    private static void KillTween(ref Tween tween)
    {
        if (tween != null && tween.IsActive()) tween.Kill();

        tween = null;
    }
}
