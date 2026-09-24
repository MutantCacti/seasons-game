using System;
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
    [SerializeField] private float pulseBrighten = 0.5f;

    // only used when the entry has no UnseenHighlight to take the speed from
    [SerializeField] private float fallbackPulseSpeed = 8.0f;

    private Action onClick;
    private Color baseTint = Color.white;
    private UnseenHighlight highlight;
    private bool highlightLookedUp;
    private StatusButtonFeedback feedback;

    // The wanted state, not the visuals: the book binds its pages while its canvas is still
    // inactive and flips faces off and on while turning, so the pulse is evaluated in Update from
    // this flag and Awake must not reset any of it
    private bool pulsing;


    private void Awake()
    {
        if (statusButton == null) Debug.LogError("PuzzleStatusRow: no status Button, so the puzzle cannot be acted on from the book", this);
        if (statusIcon == null) Debug.LogError("PuzzleStatusRow: no status icon Image, so the puzzle's state has no icon", this);
        if (statusText == null) Debug.LogError("PuzzleStatusRow: no status text, so the puzzle's state is not described", this);

        if (statusButton != null) statusButton.onClick.AddListener(OnClicked);
    }


    // Evaluated every frame rather than tweened, from the same clock and formula as the outline
    // shader, so the button and the outline around the entry swell together. Scaled time, so a
    // pause freezes both.
    private void Update()
    {
        if (!pulsing) return;

        ApplyVisuals();
    }


    private void OnDisable()
    {
        if (feedback != null) feedback.ResetState();

        ApplyRest();
    }


    private void OnDestroy()
    {
        if (statusButton != null) statusButton.onClick.RemoveListener(OnClicked);
        if (feedback != null) feedback.Changed -= OnFeedbackChanged;
    }


    // MARK: setters


    // Settles the visuals straight away, so a recycled entry never keeps the last puzzle's colour or scale
    public void SetStatus(Sprite sprite, Color tint)
    {
        baseTint = tint;

        StatusButtonFeedback buttonFeedback = Feedback;
        if (buttonFeedback != null) buttonFeedback.ResetState();

        if (statusIcon != null)
        {
            statusIcon.sprite = sprite;

            // a null sprite would otherwise draw a solid white rectangle
            statusIcon.enabled = sprite != null;
        }

        ApplyVisuals();
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

        // a button that stops being clickable under the pointer lets go of its hover
        StatusButtonFeedback buttonFeedback = Feedback;
        if (!interactable && buttonFeedback != null) buttonFeedback.ResetState();
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

        ApplyVisuals();
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


    // The pulse and the hover/press feedback compose multiplicatively: scales multiply, and each
    // brighten closes its share of what is left of the gap to white
    private void ApplyVisuals()
    {
        float pulse = pulsing ? PulseAmount() : 0.0f;

        StatusButtonFeedback buttonFeedback = Feedback;
        float interactionScale = buttonFeedback != null ? buttonFeedback.Scale : 1.0f;
        float interactionBrighten = buttonFeedback != null ? buttonFeedback.Brighten : 0.0f;

        if (statusButton != null)
        {
            statusButton.transform.localScale = Vector3.one * (Mathf.Lerp(1.0f, pulseScale, pulse) * interactionScale);
        }

        if (statusIcon != null)
        {
            Color bright = Color.Lerp(baseTint, Color.white, pulseBrighten);
            Color colour = Color.Lerp(baseTint, bright, pulse);
            colour = Color.Lerp(colour, Color.white, interactionBrighten);
            colour.a = baseTint.a;

            statusIcon.color = colour;
        }
    }


    private void ApplyRest()
    {
        if (statusButton != null) statusButton.transform.localScale = Vector3.one;
        if (statusIcon != null) statusIcon.color = baseTint;
    }


    // Only while live: the feedback also resets from its own OnDisable, and that must not undo the
    // rest state this row has just restored
    private void OnFeedbackChanged()
    {
        if (isActiveAndEnabled) ApplyVisuals();
    }


    // Found or added on first use, so the prefab does not have to carry it; add it there to tune it.
    // Lazy rather than in Awake, because binds can arrive before Awake.
    private StatusButtonFeedback Feedback
    {
        get
        {
            if (feedback != null || statusButton == null) return feedback;

            feedback = statusButton.GetComponent<StatusButtonFeedback>();
            if (feedback == null) feedback = statusButton.gameObject.AddComponent<StatusButtonFeedback>();

            feedback.Changed += OnFeedbackChanged;

            return feedback;
        }
    }


    // 0..1, peaking exactly when the outline shader's width does. Time.time because that is what URP
    // feeds _Time.y; timeSinceLevelLoad drifts out of phase after every checkpoint reload
    private float PulseAmount()
    {
        return 0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed);
    }


    private float PulseSpeed
    {
        get
        {
            // looked up once, so an entry without one does not search every frame
            if (!highlightLookedUp)
            {
                highlight = GetComponent<UnseenHighlight>();
                highlightLookedUp = true;
            }

            return highlight != null ? highlight.PulseSpeed : fallbackPulseSpeed;
        }
    }
}
