using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


// The focused animal's status panel: its name (renamable), its star, how happy it is and what is
// bothering it, hovering beside it on the screen space canvas. Opens and closes with AnimalSelection's
// focus. Happiness and tips are read from the animal a few times a second while open; the tip lines
// are pooled, only rebuilt when the tips change, and the panel's layout shrinks to fit them.
//
// Screen space rather than a world canvas: the text field stays crisp and takes keyboard focus the
// usual way, the panel is the same size at every zoom, and terrain or the book can never cover it.
// It trails the animal a fixed gap beside the edge of its sprite as drawn on screen, so a bigger
// animal pushes it out by just its extra size, with the same snappiness as the camera so the two
// move together. Clamped to the screen and kept off the inventory bar.
//
// Feel: it pops out of the side nearest the animal and shrinks back into it, a switch between
// animals is a quick bounce rather than a close and open, the star punches and wobbles on and
// squashes off, and a name being typed is faded like pencil until it is saved, when it stamps into
// full ink and the animal hops as if it heard.
//
// Lives on an always-active, full-screen, non-raycasting rect; only the panel child is shown and
// hidden, so this keeps running and nothing is bound while its canvas is inactive.
public class AnimalStatusPanel : MonoBehaviour
{
    private const float REFERENCE_FRAMERATE = 60.0f;    // as CameraController: snappiness is tuned against it


    [Header("Parts")]
    [SerializeField] private RectTransform panel;           // the box itself; a child of this rect
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private Button starButton;
    [SerializeField] private Image starImage;
    [SerializeField] private Button closeButton;
    [SerializeField] private HappinessMeter happinessMeter;
    [SerializeField] private RectTransform tipsContainer;
    [SerializeField] private TMP_Text tipTemplate;          // an inactive line in the container, cloned per tip


    [Header("Happiness")]
    [SerializeField] private float pollInterval = 0.25f;    // seconds between reads of the animal, unscaled


    [Header("Star")]
    [SerializeField] private Sprite starOn;                 // optional: without them the star is tinted instead
    [SerializeField] private Sprite starOff;
    [SerializeField] private Color starOnColour = new Color(232.0f / 255.0f, 184.0f / 255.0f, 58.0f / 255.0f, 1.0f);   // #E8B83A
    [SerializeField] private Color starOffColour = new Color(138.0f / 255.0f, 129.0f / 255.0f, 116.0f / 255.0f, 1.0f); // #8A8174, the compendium's faded ink


    [Header("Placement")]
    [SerializeField] private float gap = 40.0f;                             // canvas units between the sprite's edge and the panel
    [SerializeField] private float margin = 12.0f;                          // canvas units kept clear at the screen edge
    [SerializeField] private RectTransform avoid;                           // the inventory bar, which the panel never covers
    [SerializeField] [Range(0.0f, 1.0f)] private float snappiness = 0.2f;  // per-frame follow at 60fps, as the camera's


    [Header("Feel")]
    [SerializeField] private float openOvershoot = 1.08f;
    [SerializeField] private float openDuration = 0.22f;
    [SerializeField] private float openSlide = 6.0f;            // canvas units it slides out from the animal as it opens
    [SerializeField] private float closeSwell = 1.04f;
    [SerializeField] private float closeDuration = 0.14f;
    [SerializeField] private float switchSquash = 0.94f;
    [SerializeField] private float switchDuration = 0.18f;
    [SerializeField] private float switchPitch = 1.12f;
    [SerializeField] private float starOnPunch = 1.35f;
    [SerializeField] private float starOnWobble = 12.0f;        // degrees
    [SerializeField] private float starOnDuration = 0.3f;
    [SerializeField] private float starOffSquash = 0.8f;
    [SerializeField] private float starOffDuration = 0.2f;


    [Header("Ink")]
    // applied over whatever the built parts were given, so the text reads clearly on the paper
    [SerializeField] private Color nameColour = new Color(30.0f / 255.0f, 27.0f / 255.0f, 23.0f / 255.0f, 1.0f);      // #1E1B17
    [SerializeField] private Color nameFieldColour = new Color(230.0f / 255.0f, 222.0f / 255.0f, 202.0f / 255.0f, 1.0f); // #E6DECA
    [SerializeField] private Color tipColour = new Color(79.0f / 255.0f, 72.0f / 255.0f, 63.0f / 255.0f, 1.0f);       // #4F483F


    [Header("Name")]
    [SerializeField] private Color editingTextColour = new Color(138.0f / 255.0f, 129.0f / 255.0f, 116.0f / 255.0f, 1.0f);   // #8A8174, pencil
    [SerializeField] private Color editingFieldColour = new Color(233.0f / 255.0f, 226.0f / 255.0f, 207.0f / 255.0f, 1.0f);  // #E9E2CF, a shade lighter
    [SerializeField] private Color savedFlashColour = new Color(240.0f / 255.0f, 205.0f / 255.0f, 110.0f / 255.0f, 1.0f);    // #F0CD6E, warm gold
    [SerializeField] private float savedPop = 1.12f;
    [SerializeField] private float savedDuration = 0.35f;
    [SerializeField] private float revertShake = 6.0f;         // canvas units
    [SerializeField] private float revertDuration = 0.25f;
    [SerializeField] private float inkDuration = 0.12f;


    [Header("Sound")]
    [SerializeField] private SoundObject openSound;
    [SerializeField] private SoundObject closeSound;
    [SerializeField] private SoundObject starOnSound;
    [SerializeField] private SoundObject starOffSound;
    [SerializeField] private SoundObject renameSound;
    [SerializeField] private SoundObject revertSound;


    private RectTransform root;
    private Canvas canvas;
    private AnimalController bound;
    private SpriteRenderer boundSprite;
    private AnimalSelection selection;

    private PopFeel starFeel;

    private readonly Vector3[] corners = new Vector3[4];

    // open and close
    private bool shown;
    private float presence;
    private Tween presenceTween;
    private float slide;            // canvas units, towards the animal; eased to 0 as it opens

    // trailing, kept as the panel's bottom left so a side change doesn't move it
    private Vector2 corner;
    private bool snapNext;
    private bool onLeft;

    // the name
    private Color inkTextColour;
    private Color restFieldColour;
    private Tween nameTween;
    private Tween nameScaleTween;
    private Tween shakeTween;
    private Vector2 viewportRest;
    private bool pencil;            // the name is showing as being typed; an end edit and a submit can both arrive

    private Transform listener;

    // happiness
    private float pollTimer;
    private readonly List<TMP_Text> tipLines = new List<TMP_Text>();
    private string[] shownTips = new string[0];
    private readonly string[] happyTip = new string[1];


    private void Awake()
    {
        root = transform as RectTransform;
        canvas = GetComponentInParent<Canvas>();

        if (panel == null) Debug.LogError("AnimalStatusPanel: no panel, so the status panel can never show", this);
        if (nameField == null) Debug.LogError("AnimalStatusPanel: no nameField, so animals cannot be renamed", this);
        if (starButton == null) Debug.LogError("AnimalStatusPanel: no starButton, so animals cannot be highlighted", this);
        if (avoid == null) Debug.LogWarning("AnimalStatusPanel: no avoid rect, so the panel may sit on top of the inventory bar", this);

        if (nameField != null)
        {
            nameField.characterLimit = AnimalController.MAX_NAME_LENGTH;
            nameField.lineType = TMP_InputField.LineType.SingleLine;
            nameField.onSelect.AddListener(OnNameSelected);
            nameField.onSubmit.AddListener(OnNameSubmitted);
            nameField.onEndEdit.AddListener(OnNameEdited);

            inkTextColour = nameColour;
            restFieldColour = nameFieldColour;

            if (nameField.textComponent != null) nameField.textComponent.color = nameColour;
            if (nameField.image != null) nameField.image.color = nameFieldColour;
            viewportRest = nameField.textViewport != null ? nameField.textViewport.anchoredPosition : Vector2.zero;
        }

        if (starButton != null)
        {
            starButton.onClick.AddListener(OnStarClicked);
            starFeel = GetOrAddFeel(starButton.gameObject);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseClicked);
            GetOrAddFeel(closeButton.gameObject);
        }

        if (happinessMeter == null) Debug.LogWarning("AnimalStatusPanel: no happinessMeter, so happiness is not shown", this);
        if (tipsContainer == null || tipTemplate == null) Debug.LogWarning("AnimalStatusPanel: no tipsContainer or tipTemplate, so happiness tips are not shown", this);

        if (tipTemplate != null)
        {
            tipTemplate.color = tipColour;
            tipTemplate.gameObject.SetActive(false);
        }

        // matches shownTips being empty, so a content animal never shows an empty tips block
        if (tipsContainer != null) tipsContainer.gameObject.SetActive(false);

        if (panel != null) panel.gameObject.SetActive(false);
    }


    private void Start()
    {
        selection = AnimalSelection.Instance;

        if (selection == null)
        {
            Debug.LogError("AnimalStatusPanel: no AnimalSelection, so the panel never opens", this);
            return;
        }

        selection.FocusChanged += OnFocusChanged;
        OnFocusChanged(selection.Focused);
    }


    private void OnDestroy()
    {
        if (selection != null) selection.FocusChanged -= OnFocusChanged;

        if (nameField != null)
        {
            nameField.onSelect.RemoveListener(OnNameSelected);
            nameField.onSubmit.RemoveListener(OnNameSubmitted);
            nameField.onEndEdit.RemoveListener(OnNameEdited);
        }

        if (starButton != null) starButton.onClick.RemoveListener(OnStarClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);

        Unbind(false);

        if (presenceTween != null) presenceTween.Kill();
        if (nameTween != null) nameTween.Kill();
        if (nameScaleTween != null) nameScaleTween.Kill();
        if (shakeTween != null) shakeTween.Kill();
    }


    private void LateUpdate()
    {
        if (panel == null || !panel.gameObject.activeSelf) return;

        if (bound != null)
        {
            pollTimer -= Time.unscaledDeltaTime;

            if (pollTimer <= 0.0f)
            {
                pollTimer = pollInterval;
                ReadHappiness(false);
            }

            Place();
        }

        panel.localScale = new Vector3(presence, presence, 1.0f);
    }


    // MARK: binding


    private void OnFocusChanged(AnimalController animal)
    {
        if (animal == bound) return;

        bool switching = bound != null && animal != null;

        Unbind(true);

        if (animal == null || panel == null)
        {
            Close();
            return;
        }

        bound = animal;
        bound.StatusChanged += Refresh;
        boundSprite = animal.GetComponentInChildren<SpriteRenderer>(true);

        Refresh();

        // the bar fills up from empty as it opens, and the tips are in place before it lays out
        ReadHappiness(true);
        pollTimer = pollInterval;

        if (switching) Bounce();
        else Open();
    }


    // A name still being typed is saved, not lost: disabling the field only deactivates it and sends
    // no end edit (TMP_InputField.OnDisable), so it is committed here first
    private void Unbind(bool commit)
    {
        if (bound == null) return;

        if (commit) CommitName();

        bound.StatusChanged -= Refresh;
        bound = null;
        boundSprite = null;
    }


    private void Refresh()
    {
        if (bound == null) return;

        if (nameField != null && !nameField.isFocused) nameField.SetTextWithoutNotify(bound.DisplayName);

        bool on = bound.Highlighted;

        if (starImage != null)
        {
            Sprite sprite = on ? starOn : starOff;
            if (sprite != null) starImage.sprite = sprite;

            starImage.color = on ? starOnColour : starOffColour;
        }
    }


    // MARK: happiness


    private void ReadHappiness(bool fromEmpty)
    {
        if (bound == null) return;

        string[] tips = bound.GetHappinessTips();

        if (happinessMeter != null)
        {
            // nothing bothering it means content, whatever the sum says
            float happiness = tips == null || tips.Length == 0 ? 1.0f : bound.Happiness;

            if (fromEmpty) happinessMeter.SetValue(happiness, true, 0.0f);
            else if (!Mathf.Approximately(happiness, happinessMeter.Value)) happinessMeter.SetValue(happiness, true);
        }

        // nothing bothering it: its happy line goes where the tips would be
        if ((tips == null || tips.Length == 0) && !string.IsNullOrEmpty(bound.GetHappyLine()))
        {
            happyTip[0] = bound.GetHappyLine();
            tips = happyTip;
        }

        ShowTips(tips);
    }


    // Only touches the lines when the tips differ, so the layout isn't rebuilt four times a second.
    // With none, the container hides and the panel shrinks to the header and bar
    private void ShowTips(string[] tips)
    {
        if (tipsContainer == null || tipTemplate == null) return;
        if (tips == null) tips = new string[0];

        if (SameTips(tips)) return;

        // a copy: the happy line's array is reused, and comparing it with itself would never see a change
        shownTips = (string[])tips.Clone();

        while (tipLines.Count < tips.Length)
        {
            TMP_Text line = Instantiate(tipTemplate, tipsContainer);
            line.name = "Tip";
            tipLines.Add(line);
        }

        for (int i = 0; i < tipLines.Count; i++)
        {
            bool used = i < tips.Length;

            tipLines[i].gameObject.SetActive(used);
            if (used) tipLines[i].text = tips[i];
        }

        tipsContainer.gameObject.SetActive(tips.Length > 0);

        if (panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }


    private bool SameTips(string[] tips)
    {
        if (tips.Length != shownTips.Length) return false;

        for (int i = 0; i < tips.Length; i++)
        {
            if (tips[i] != shownTips[i]) return false;
        }

        return true;
    }


    // MARK: open and close


    // out of the side nearest the animal, overshooting a little, sliding the last few units away
    private void Open()
    {
        // reopened mid close, it grows back from where it is rather than from nothing
        bool wasActive = panel.gameObject.activeSelf;

        shown = true;
        panel.gameObject.SetActive(true);

        // pitfall 2: the layout sizes the panel, and it has not run yet this frame
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        snapNext = true;
        Place();

        if (presenceTween != null) presenceTween.Kill();
        if (!wasActive) presence = 0.0f;

        slide = openSlide;

        Sequence open = DOTween.Sequence();
        open.Append(DOTween.To(() => presence, value => presence = value, openOvershoot, openDuration * 0.6f).SetEase(Ease.OutQuad));
        open.Append(DOTween.To(() => presence, value => presence = value, 1.0f, openDuration * 0.4f).SetEase(Ease.InOutQuad));
        open.Insert(0.0f, DOTween.To(() => slide, value => slide = value, 0.0f, openDuration).SetEase(Ease.OutCubic));
        open.SetUpdate(true).SetLink(gameObject);

        presenceTween = open;

        PlayUISound(openSound, 1.0f);
    }


    // a swell and then back into the animal
    private void Close()
    {
        if (panel == null || !shown) return;

        shown = false;

        if (presenceTween != null) presenceTween.Kill();

        Sequence close = DOTween.Sequence();
        close.Append(DOTween.To(() => presence, value => presence = value, closeSwell, closeDuration * 0.3f).SetEase(Ease.OutQuad));
        close.Append(DOTween.To(() => presence, value => presence = value, 0.0f, closeDuration * 0.7f).SetEase(Ease.InBack));
        close.OnComplete(() => panel.gameObject.SetActive(false));
        close.SetUpdate(true).SetLink(gameObject);

        presenceTween = close;

        PlayUISound(closeSound, 1.0f);
    }


    // another animal while open: the panel trails over to it with a quick squash, not a close and open
    private void Bounce()
    {
        if (!shown || !panel.gameObject.activeSelf)
        {
            Open();
            return;
        }

        if (presenceTween != null) presenceTween.Kill();

        Sequence bounce = DOTween.Sequence();
        bounce.Append(DOTween.To(() => presence, value => presence = value, switchSquash, switchDuration * 0.4f).SetEase(Ease.OutQuad));
        bounce.Append(DOTween.To(() => presence, value => presence = value, 1.0f, switchDuration * 0.6f).SetEase(Ease.OutBack));
        bounce.SetUpdate(true).SetLink(gameObject);

        presenceTween = bounce;

        PlayUISound(openSound, switchPitch);
    }


    // MARK: star


    private void OnStarClicked()
    {
        if (bound == null) return;

        bool on = !bound.Highlighted;
        bound.SetHighlighted(on);

        if (on)
        {
            if (starFeel != null)
            {
                starFeel.Punch(starOnPunch, starOnDuration);
                starFeel.Wobble(starOnWobble, starOnDuration);
            }

            PlayUISound(starOnSound, 1.0f);
        }
        else
        {
            if (starFeel != null) starFeel.Squash(starOffSquash, starOffDuration);

            PlayUISound(starOffSound, 1.0f);
        }
    }


    private void OnCloseClicked()
    {
        if (selection != null) selection.Clear();
    }


    // MARK: name


    // typing is pencil: faded ink on a lighter field, until it is saved
    private void OnNameSelected(string text)
    {
        pencil = true;
        TweenName(editingTextColour, editingFieldColour, inkDuration);
    }


    private void OnNameSubmitted(string text)
    {
        CommitName();
    }


    // Escape puts the old text back and then ends the edit, with wasCanceled set until the next edit
    private void OnNameEdited(string text)
    {
        if (nameField != null && nameField.wasCanceled)
        {
            Revert();
            return;
        }

        CommitName();
    }


    // The animal trims, caps and falls back to the species name; the field then shows what it kept.
    // A real change stamps in with a sound and the animal hopping; anything else just goes back to ink
    private void CommitName()
    {
        if (bound == null || nameField == null) return;

        bool changed = bound.SetCustomName(nameField.text);
        nameField.SetTextWithoutNotify(bound.DisplayName);

        if (!changed)
        {
            // only a name that was being typed goes back to ink; a second end edit after a save must
            // not cut the save's flash short
            if (pencil) TweenName(inkTextColour, restFieldColour, inkDuration);
            pencil = false;
            return;
        }

        pencil = false;
        Stamp();
        PlayUISound(renameSound, 1.0f);

        bound.OnRenamed();
    }


    // straight to full ink, a pop, and a warm flash fading back to paper
    private void Stamp()
    {
        if (nameTween != null) nameTween.Kill();
        if (nameScaleTween != null) nameScaleTween.Kill();

        if (nameField.textComponent != null) nameField.textComponent.color = inkTextColour;

        if (nameField.image != null)
        {
            nameField.image.color = savedFlashColour;
            nameTween = nameField.image.DOColor(restFieldColour, savedDuration).SetEase(Ease.InQuad).SetUpdate(true).SetLink(gameObject);
        }

        Transform field = nameField.transform;
        field.localScale = Vector3.one;

        Sequence pop = DOTween.Sequence();
        pop.Append(field.DOScale(savedPop, savedDuration * 0.35f).SetEase(Ease.OutQuad));
        pop.Append(field.DOScale(1.0f, savedDuration * 0.65f).SetEase(Ease.OutBack));
        pop.SetUpdate(true).SetLink(gameObject);

        nameScaleTween = pop;
    }


    // "never mind": the old name is back already, so ink it and give the text a little head shake
    private void Revert()
    {
        pencil = false;
        TweenName(inkTextColour, restFieldColour, inkDuration);
        PlayUISound(revertSound, 1.0f);

        RectTransform viewport = nameField != null ? nameField.textViewport : null;
        if (viewport == null) return;

        if (shakeTween != null) shakeTween.Kill();
        viewport.anchoredPosition = viewportRest;

        shakeTween = viewport.DOShakeAnchorPos(revertDuration, new Vector2(revertShake, 0.0f), 20, 0.0f, false, true)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnKill(() => viewport.anchoredPosition = viewportRest);
    }


    private void TweenName(Color text, Color field, float duration)
    {
        if (nameField == null) return;

        if (nameTween != null) nameTween.Kill();

        Sequence tween = DOTween.Sequence();
        if (nameField.textComponent != null) tween.Join(nameField.textComponent.DOColor(text, duration));
        if (nameField.image != null) tween.Join(nameField.image.DOColor(field, duration));
        tween.SetUpdate(true).SetLink(gameObject);

        nameTween = tween;
    }


    // MARK: placement


    // Beside the sprite, gap from its right edge unless that runs off the screen (then gap from its
    // left), vertically centred on it, clamped inside the screen and lifted clear of the inventory
    // bar. It grows out of the side facing the animal, so the pivot follows the side
    private void Place()
    {
        Camera viewCamera = Camera.main;
        if (viewCamera == null || root == null) return;

        if (!TryGetSpriteRect(viewCamera, out Rect sprite)) return;

        Rect area = root.rect;
        Vector2 size = panel.rect.size;

        float left = sprite.xMax + gap;
        bool leftSide = left + size.x > area.xMax - margin;
        if (leftSide) left = sprite.xMin - gap - size.x;

        left = Mathf.Clamp(left, area.xMin + margin, Mathf.Max(area.xMin + margin, area.xMax - margin - size.x));

        float bottom = sprite.center.y - size.y * 0.5f;
        bottom = Mathf.Clamp(bottom, area.yMin + margin, Mathf.Max(area.yMin + margin, area.yMax - margin - size.y));

        bottom = KeepOffAvoid(left, bottom, size);

        Vector2 target = new Vector2(left, bottom);

        if (snapNext)
        {
            corner = target;
            snapNext = false;
        }
        else
        {
            // framerate independent, the same decay as the camera's lerp
            float t = 1.0f - Mathf.Pow(1.0f - snappiness, Time.unscaledDeltaTime * REFERENCE_FRAMERATE);
            corner = Vector2.Lerp(corner, target, t);
        }

        onLeft = leftSide;
        panel.pivot = new Vector2(onLeft ? 1.0f : 0.0f, 0.5f);

        // the opening slide starts nearer the animal
        float towardAnimal = onLeft ? slide : -slide;

        panel.localPosition = new Vector3(corner.x + panel.pivot.x * size.x + towardAnimal, corner.y + panel.pivot.y * size.y, 0.0f);
    }


    // The animal's sprite as drawn, in this rect's local space. Read from the sprite rather than the
    // renderer's bounds, so a hidden animal (a burrowed worm) still has a size and the panel doesn't
    // jump when it goes under. An animal with no sprite is a point at its transform
    private bool TryGetSpriteRect(Camera viewCamera, out Rect rect)
    {
        rect = default;

        // an overlay canvas converts with no camera
        Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        Sprite sprite = boundSprite != null ? boundSprite.sprite : null;
        int count = sprite != null ? 4 : 1;

        for (int i = 0; i < count; i++)
        {
            Vector3 world;

            if (sprite != null)
            {
                Bounds local = sprite.bounds;
                Vector3 point = new Vector3((i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y, 0.0f);

                // flipX mirrors the drawing through the pivot
                if (boundSprite.flipX) point.x = -point.x;

                world = boundSprite.transform.TransformPoint(point);
            }
            else
            {
                world = bound.transform.position;
            }

            Vector3 screen = viewCamera.WorldToScreenPoint(world);
            if (screen.z <= 0.0f) return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, canvasCamera, out Vector2 local2)) return false;

            min = Vector2.Min(min, local2);
            max = Vector2.Max(max, local2);
        }

        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }


    // lifts the panel to just above the avoid rect if the two overlap
    private float KeepOffAvoid(float left, float bottom, Vector2 size)
    {
        if (avoid == null || !avoid.gameObject.activeInHierarchy) return bottom;

        avoid.GetWorldCorners(corners);

        Vector2 min = root.InverseTransformPoint(corners[0]);
        Vector2 max = min;

        for (int i = 1; i < 4; i++)
        {
            Vector2 local = root.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        bool overlaps = left < max.x && left + size.x > min.x && bottom < max.y && bottom + size.y > min.y;

        return overlaps ? max.y + margin : bottom;
    }


    // MARK: internals


    private static PopFeel GetOrAddFeel(GameObject target)
    {
        PopFeel feel = target.GetComponent<PopFeel>();
        return feel != null ? feel : target.AddComponent<PopFeel>();
    }


    // AudioManager.PlayUI is not written yet, so UI sounds play at the listener the way the pings'
    // do; give their SoundObjects a spatial blend of 0
    private void PlayUISound(SoundObject sound, float pitch)
    {
        if (sound == null || AudioManager.Instance == null) return;

        if (listener == null)
        {
            AudioListener found = FindAnyObjectByType<AudioListener>();
            if (found != null) listener = found.transform;
        }

        Vector3 position = listener != null ? listener.position : Vector3.zero;

        AudioManager.Instance.PlaySFX(sound, position, pitch);
    }
}
