using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


// Hover, press and click feedback for a puzzle status button, so it reads as something to click.
// It never touches the transform or the colour itself: it only animates two factors, and
// PuzzleStatusRow multiplies them into the fail pulse, so hovering a pulsing button adds to the
// pulse instead of fighting it.
[RequireComponent(typeof(Button))]
public class StatusButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [SerializeField] private float hoverScale = 1.1f;
    [SerializeField] private float pressScale = 0.94f;
    [SerializeField] private float hoverBrighten = 0.3f;
    [SerializeField] private float transitionDuration = 0.08f;

    [Header("Click punch")]
    [SerializeField] private float punchScale = 1.12f;
    [SerializeField] private float punchDuration = 0.06f;

    private Button button;
    private bool hovered;
    private bool pressed;

    private float scale = 1.0f;
    private float punch = 1.0f;
    private float brighten;

    private Tween scaleTween;
    private Tween punchTween;
    private Tween brightenTween;


    // Raised whenever a factor moves, so the row can reapply its visuals
    public event Action Changed;


    // multiplied into the pulse scale
    public float Scale { get { return scale * punch; } }


    // 0..1, how far toward white the (already pulsed) icon colour is pushed
    public float Brighten { get { return brighten; } }


    private void Awake()
    {
        button = GetComponent<Button>();
    }


    private void OnDisable()
    {
        ResetState();
    }


    private void OnDestroy()
    {
        KillTweens();
    }


    // Back to rest with no animation. Called on rebind and on disable, because rows are recycled
    // and a new puzzle must never inherit the last one's hover.
    public void ResetState()
    {
        KillTweens();

        hovered = false;
        pressed = false;
        scale = 1.0f;
        punch = 1.0f;
        brighten = 0.0f;

        RaiseChanged();
    }


    // MARK: pointer


    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!Reacts()) return;

        hovered = true;
        TweenToTargets();
    }


    public void OnPointerExit(PointerEventData eventData)
    {
        if (!Reacts()) return;

        hovered = false;
        TweenToTargets();
    }


    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Reacts() || eventData.button != PointerEventData.InputButton.Left) return;

        pressed = true;
        TweenToTargets();
    }


    public void OnPointerUp(PointerEventData eventData)
    {
        if (!Reacts() || eventData.button != PointerEventData.InputButton.Left) return;

        pressed = false;
        TweenToTargets();
    }


    public void OnPointerClick(PointerEventData eventData)
    {
        if (!Reacts() || eventData.button != PointerEventData.InputButton.Left) return;

        KillTween(ref punchTween);

        punchTween = DOTween.To(() => punch, value => { punch = value; RaiseChanged(); }, punchScale, punchDuration)
            .SetEase(Ease.OutQuad)
            .SetLoops(2, LoopType.Yoyo)
            .OnComplete(() => { punch = 1.0f; RaiseChanged(); });
    }


    // A button that cannot be clicked does not pretend it can
    private bool Reacts()
    {
        if (button != null && button.IsInteractable()) return true;

        // it may have turned non-interactable under the pointer, so let go of anything half done
        if (hovered || pressed || scale != 1.0f || brighten != 0.0f) ResetState();

        return false;
    }


    // MARK: tweens


    private void TweenToTargets()
    {
        float targetScale = pressed ? pressScale : (hovered ? hoverScale : 1.0f);
        float targetBrighten = hovered || pressed ? hoverBrighten : 0.0f;

        KillTween(ref scaleTween);
        KillTween(ref brightenTween);

        scaleTween = DOTween.To(() => scale, value => { scale = value; RaiseChanged(); }, targetScale, transitionDuration)
            .SetEase(Ease.OutQuad);

        brightenTween = DOTween.To(() => brighten, value => { brighten = value; RaiseChanged(); }, targetBrighten, transitionDuration)
            .SetEase(Ease.OutQuad);
    }


    private void KillTweens()
    {
        KillTween(ref scaleTween);
        KillTween(ref punchTween);
        KillTween(ref brightenTween);
    }


    private void RaiseChanged()
    {
        if (Changed != null) Changed();
    }


    private static void KillTween(ref Tween tween)
    {
        if (tween != null && tween.IsActive()) tween.Kill();

        tween = null;
    }
}
