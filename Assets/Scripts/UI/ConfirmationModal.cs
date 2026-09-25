using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


// A reusable modal that asks one question and routes a confirm or a cancel. The question and the
// button labels are authored in the prefab; only a token in the question is filled in per call.
// Listeners are rebound each Show, so one instance serves any number of callers without piling up.
public class ConfirmationModal : MonoBehaviour
{
    private static ConfirmationModal instance;
    public static ConfirmationModal Instance { get { return instance; } }


    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.15f;

    private string questionTemplate;
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
        if (cancelButton == null) Debug.LogError("ConfirmationModal: no cancel Button, so the modal can never be dismissed", this);
        if (canvasGroup == null) Debug.LogError("ConfirmationModal: no CanvasGroup, so the modal cannot fade or block input", this);

        // captured now because Show substitutes into it, so the authored template must survive
        // repeated Shows rather than being overwritten by the first substitution
        questionTemplate = questionText != null ? questionText.text : string.Empty;

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


    // token, when given, is replaced by value in the authored question; a null token shows it verbatim
    public void Show(Action confirm, Action cancel = null, string token = null, string value = null)
    {
        if (questionText == null || confirmButton == null || cancelButton == null || canvasGroup == null)
        {
            Debug.LogError("ConfirmationModal: cannot show, a required reference is missing", this);
            return;
        }

        onConfirm = confirm;
        onCancel = cancel;

        questionText.text = token == null ? questionTemplate : questionTemplate.Replace(token, value ?? string.Empty);

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
