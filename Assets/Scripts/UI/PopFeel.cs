using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;


// Makes a UI control feel physical: it grows under the pointer, dips while pressed, and can be punched,
// squashed or wobbled from code. The pointer part and the punch part are separate factors multiplied
// together, so a punch played while hovered settles back to the hovered size rather than fighting it.
//
// Unscaled time throughout: UI keeps answering while the game is paused.
public class PopFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private float hoverScale = 1.1f;
    [SerializeField] private float pressScale = 0.92f;
    [SerializeField] private float pointerDuration = 0.1f;


    private Vector3 restScale = Vector3.one;
    private float pointerFactor = 1.0f;
    private float punchFactor = 1.0f;
    private float wobbleDegrees;

    private bool hovered;
    private bool pressed;

    private Tween pointerTween;
    private Tween punchTween;
    private Tween wobbleTween;


    private void Awake()
    {
        restScale = transform.localScale;
    }


    private void OnDisable()
    {
        // a control hidden mid-press comes back at rest
        hovered = false;
        pressed = false;

        KillAll();

        pointerFactor = 1.0f;
        punchFactor = 1.0f;
        wobbleDegrees = 0.0f;
        Apply();
    }


    private void OnDestroy()
    {
        KillAll();
    }


    // MARK: pointer


    public void OnPointerEnter(PointerEventData eventData) { hovered = true; RefreshPointer(); }
    public void OnPointerExit(PointerEventData eventData) { hovered = false; pressed = false; RefreshPointer(); }
    public void OnPointerDown(PointerEventData eventData) { pressed = true; RefreshPointer(); }
    public void OnPointerUp(PointerEventData eventData) { pressed = false; RefreshPointer(); }


    private void RefreshPointer()
    {
        float target = pressed ? pressScale : hovered ? hoverScale : 1.0f;

        if (pointerTween != null) pointerTween.Kill();

        pointerTween = DOTween.To(() => pointerFactor, value => { pointerFactor = value; Apply(); }, target, pointerDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(gameObject);
    }


    // MARK: punches


    // out to peak and back to 1, overshooting a little on the way home
    public void Punch(float peak, float duration)
    {
        PlayPunch(peak, duration, Ease.OutQuad, Ease.OutBack);
    }


    // in to low and back, no overshoot: a soft, quieter reaction
    public void Squash(float low, float duration)
    {
        PlayPunch(low, duration, Ease.OutQuad, Ease.OutQuad);
    }


    // a rotation shake that dies away, e.g. a star being set
    public void Wobble(float degrees, float duration)
    {
        if (wobbleTween != null) wobbleTween.Kill();

        wobbleDegrees = 0.0f;

        Sequence wobble = DOTween.Sequence();
        wobble.Append(TweenWobble(degrees, duration * 0.25f));
        wobble.Append(TweenWobble(-degrees * 0.6f, duration * 0.25f));
        wobble.Append(TweenWobble(degrees * 0.3f, duration * 0.25f));
        wobble.Append(TweenWobble(0.0f, duration * 0.25f));
        wobble.SetUpdate(true).SetLink(gameObject);

        wobbleTween = wobble;
    }


    private void PlayPunch(float extreme, float duration, Ease outEase, Ease backEase)
    {
        if (punchTween != null) punchTween.Kill();

        punchFactor = 1.0f;

        Sequence punch = DOTween.Sequence();
        punch.Append(DOTween.To(() => punchFactor, value => { punchFactor = value; Apply(); }, extreme, duration * 0.35f).SetEase(outEase));
        punch.Append(DOTween.To(() => punchFactor, value => { punchFactor = value; Apply(); }, 1.0f, duration * 0.65f).SetEase(backEase));
        punch.SetUpdate(true).SetLink(gameObject);

        punchTween = punch;
    }


    private Tween TweenWobble(float to, float duration)
    {
        return DOTween.To(() => wobbleDegrees, value => { wobbleDegrees = value; Apply(); }, to, duration).SetEase(Ease.InOutSine);
    }


    // MARK: internals


    private void Apply()
    {
        transform.localScale = restScale * (pointerFactor * punchFactor);
        transform.localRotation = Quaternion.Euler(0.0f, 0.0f, wobbleDegrees);
    }


    private void KillAll()
    {
        if (pointerTween != null) pointerTween.Kill();
        if (punchTween != null) punchTween.Kill();
        if (wobbleTween != null) wobbleTween.Kill();
    }
}
