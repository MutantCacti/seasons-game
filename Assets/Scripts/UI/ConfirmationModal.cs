using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


// A reusable modal that asks one question and routes a confirm or a cancel. Listeners are rebound
// each time it is shown, so one instance serves any number of callers without listeners piling up.
public class ConfirmationModal : MonoBehaviour
{
    private static ConfirmationModal instance;
    public static ConfirmationModal Instance { get { return instance; } }


    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TextMeshProUGUI confirmButtonText;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TextMeshProUGUI cancelButtonText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.15f;

    private Action onConfirm;
    private Action onCancel;
    private Tween fadeTween;
    private bool isOpen;


    public bool IsOpen { get { return isOpen; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("ConfirmationModal: found more than one ConfirmationModal - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (questionText == null) Debug.LogError("ConfirmationModal: no question text, so the question is never shown", this);
        if (confirmButton == null) Debug.LogError("ConfirmationModal: no confirm Button, so the confirm action can never run", this);
        if (confirmButtonText == null) Debug.LogError("ConfirmationModal: no confirm label, so the confirm button has no text", this);
        if (cancelButton == null) Debug.LogError("ConfirmationModal: no cancel Button, so the modal can never be dismissed", this);
        if (cancelButtonText == null) Debug.LogError("ConfirmationModal: no cancel label, so the cancel button has no text", this);
        if (canvasGroup == null) Debug.LogError("ConfirmationModal: no CanvasGroup, so the modal cannot fade or block input", this);

        // the prefab ships active but invisible, so the singleton is set before the first Show. This
        // also re-asserts the hidden state in case a prefab override changed the serialized defaults.
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0.0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;

        KillFade();
    }


    // MARK: show / hide


    public void Show(string question, string confirmText, string cancelText, Action confirm, Action cancel = null)
    {
        if (questionText == null || confirmButton == null || confirmButtonText == null || cancelButton == null || cancelButtonText == null || canvasGroup == null)
        {
            Debug.LogError("ConfirmationModal: cannot show, a required reference is missing", this);
            return;
        }

        onConfirm = confirm;
        onCancel = cancel;

        questionText.text = question ?? string.Empty;
        confirmButtonText.text = confirmText ?? string.Empty;
        cancelButtonText.text = cancelText ?? string.Empty;

        confirmButton.onClick.RemoveAllListeners();
        confirmButton.onClick.AddListener(Confirm);

        cancelButton.onClick.RemoveAllListeners();
        cancelButton.onClick.AddListener(Cancel);

        isOpen = true;

        gameObject.SetActive(true);

        // interactable and blocksRaycasts come on before the fade, so there is never a frame where
        // the modal is fading in but a click can slip through to the book or the world
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        KillFade();
        fadeTween = canvasGroup.DOFade(1.0f, fadeDuration).SetUpdate(true);
    }


    public void Hide()
    {
        isOpen = false;

        // raycasts drop at the start of the hide, so whatever is behind is clickable again at once
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        KillFade();

        if (canvasGroup != null)
        {
            fadeTween = canvasGroup.DOFade(0.0f, fadeDuration).SetUpdate(true).OnComplete(() => gameObject.SetActive(false));
        }
    }


    // MARK: actions


    // The shared dismiss path for the cancel button and for Escape (routed through
    // InGameOptionsHelper). It never runs the confirm action.
    public void Cancel()
    {
        if (!isOpen) return;

        Action cancel = onCancel;
        Hide();
        if (cancel != null) cancel();
    }


    private void Confirm()
    {
        if (!isOpen) return;

        Action confirm = onConfirm;
        Hide();
        if (confirm != null) confirm();
    }


    private void KillFade()
    {
        if (fadeTween != null && fadeTween.IsActive()) fadeTween.Kill();

        fadeTween = null;
    }
}
