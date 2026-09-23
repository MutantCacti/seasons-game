using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;


// how to overcomplicate menus, a file
public class CompendiumUIManager : MonoBehaviour
{
    private static CompendiumUIManager instance;
    public static CompendiumUIManager Instance { get { return instance; } }


    public const int ENTRIES_PER_PAGE = 3;

    private const float FLAT_THRESHOLD = 0.995f;


    [Serializable]
    public class Page
    {
        public RectTransform root;
        public CompendiumEntry[] entries;

        [NonSerialized] public int pageIndex = -1;
    }


    [Serializable]
    public class Leaf
    {
        public RectTransform pivot;
        public Page front;
        public Page back;

        [NonSerialized] public float angle;
        [NonSerialized] public Tween turn;
    }


    [Header("Book")]
    [SerializeField] private BookAnimator book;
    [SerializeField] private GameObject uiRoot;
    [SerializeField] private Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Leaves (exactly three)")]
    [SerializeField] private Leaf[] leaves;

    [Header("Buttons")]
    [SerializeField] private Button turnPrevious;
    [SerializeField] private Button turnNext;
    [SerializeField] private Button close;

    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.2f;
    [SerializeField] private float turnDuration = 0.55f;
    [SerializeField] private Ease turnEase = Ease.InOutQuad;

    [Header("Layout")]
    [SerializeField] private float hiddenDepth = 2.0f;


    private Leaf left;
    private Leaf right;
    private Leaf parked;

    private int spread;
    private int spreadCount;
    private bool built;
    private bool turning;
    private Coroutine openRoutine;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one CompendiumUIManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (book == null) book = GetComponent<BookAnimator>();

        if (book == null) Debug.LogError("CompendiumUIManager: no BookAnimator, so nothing can open the book", this);
        if (uiRoot == null) Debug.LogError("CompendiumUIManager: no uiRoot, so the pages have nowhere to live", this);
        if (canvasGroup == null && uiRoot != null) canvasGroup = uiRoot.GetComponent<CanvasGroup>();

        if (leaves == null || leaves.Length < 3)
        {
            Debug.LogError($"CompendiumUIManager: three leaves are needed, found {(leaves == null ? 0 : leaves.Length)}", this);
        }

        if (turnPrevious != null) turnPrevious.onClick.AddListener(TurnBack);
        if (turnNext != null) turnNext.onClick.AddListener(TurnForward);
        if (close != null) close.onClick.AddListener(RequestClose);

        HideImmediate();
    }


    private void OnDestroy()
    {
        if (turnPrevious != null) turnPrevious.onClick.RemoveListener(TurnBack);
        if (turnNext != null) turnNext.onClick.RemoveListener(TurnForward);
        if (close != null) close.onClick.RemoveListener(RequestClose);

        KillTurns();
    }


    // MARK: open and close


    public void OnOpenCloseCompendium(bool isReading)
    {
        if (isReading) Open();
        else Close();
    }


    public void Open()
    {
        if (uiRoot == null) return;

        EnsureCamera();
        ResetLeaves();

        uiRoot.SetActive(true);

        KillFade();
        StopOpenRoutine();

        if (canvasGroup == null) return;

        canvasGroup.alpha = 0.0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        openRoutine = StartCoroutine(RevealWhenFlat());
    }


    // The menu waits for the pages to be opened fully to 90 degrees
    private IEnumerator RevealWhenFlat()
    {
        while (book != null && book.PagesFlat < FLAT_THRESHOLD) yield return null;

        openRoutine = null;

        KillFade();

        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        canvasGroup.DOFade(1.0f, fadeDuration).SetEase(Ease.OutQuad);
    }


    public void Close()
    {
        if (uiRoot == null) return;

        StopOpenRoutine();
        KillFade();

        if (canvasGroup == null)
        {
            HideImmediate();
            return;
        }

        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        canvasGroup.DOFade(0.0f, fadeDuration)
            .SetEase(Ease.InQuad)
            .OnComplete(HideImmediate);
    }


    public void RequestClose()
    {
        if (book != null) book.SetRead(false);

        Close();
    }


    private void HideImmediate()
    {
        StopOpenRoutine();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0.0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (uiRoot != null) uiRoot.SetActive(false);
    }


    private void StopOpenRoutine()
    {
        if (openRoutine == null) return;

        StopCoroutine(openRoutine);
        openRoutine = null;
    }


    private void EnsureCamera()
    {
        if (canvas == null) canvas = uiRoot != null ? uiRoot.GetComponent<Canvas>() : null;
        if (canvas == null) return;

        if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
    }


    // MARK: pages


    private int ItemCount
    {
        get { return CompendiumManager.Instance != null ? CompendiumManager.Instance.Count : 0; }
    }


    private int PageCount
    {
        get { return (ItemCount + ENTRIES_PER_PAGE - 1) / ENTRIES_PER_PAGE; }
    }


    private int SpreadCount
    {
        get
        {
            int pages = PageCount;
            return (pages + 1) / 2;
        }
    }


    private void ResetLeaves()
    {
        if (leaves == null || leaves.Length < 3) return;

        spread = 0;
        turning = false;
        built = true;

        KillTurns();

        left = leaves[0];
        right = leaves[1];
        parked = leaves[2];

        SetAngle(left, 180.0f);
        AssignPage(left.front, -1);
        AssignPage(left.back, 0);

        SetAngle(right, 0.0f);
        AssignPage(right.front, 1);
        AssignPage(right.back, 2);

        SetAngle(parked, 0.0f);
        AssignPage(parked.front, 3);
        AssignPage(parked.back, 4);

        ShowLeaf(left, false);
        ShowLeaf(right, true);
        HideLeaf(parked);

        Promote(left);
        Promote(right);
        Park(parked);

        spreadCount = SpreadCount;
        UpdateTurnButtons();
    }


    public void TurnForward()
    {
        if (!built || turning) return;
        if (spread + 1 >= spreadCount) return;

        turning = true;

        int nextSpread = spread + 1;
        int revealed = nextSpread * 2 + 1;

        UpdateTurnButtons(nextSpread);

        Leaf oldLeft = left;
        Leaf oldRight = right;
        Leaf oldParked = parked;

        AssignPage(oldParked.front, revealed);
        AssignPage(oldParked.back, revealed + 1);
        SetAngle(oldParked, 0.0f);
        Park(oldParked);
        ShowLeaf(oldParked, true);

        BringToFront(oldRight);
        ShowLeaf(oldRight, true);

        AnimateTurn(oldRight, oldRight.angle, oldRight.angle + 180.0f, false, () =>
        {
            left = oldRight;
            right = oldParked;
            parked = oldLeft;

            spread = nextSpread;
            turning = false;

            Promote(left);
            Promote(right);
            Retire(parked);
        });
    }


    public void TurnBack()
    {
        if (!built || turning) return;
        if (spread - 1 < 0) return;

        turning = true;

        int previousSpread = spread - 1;
        int revealed = previousSpread * 2;

        UpdateTurnButtons(previousSpread);

        Leaf oldLeft = left;
        Leaf oldRight = right;
        Leaf oldParked = parked;

        AssignPage(oldParked.back, revealed);
        AssignPage(oldParked.front, revealed - 1);
        SetAngle(oldParked, 180.0f);
        Park(oldParked);
        ShowLeaf(oldParked, false);

        BringToFront(oldLeft);
        ShowLeaf(oldLeft, false);

        AnimateTurn(oldLeft, oldLeft.angle, oldLeft.angle - 180.0f, true, () =>
        {
            left = oldParked;
            right = oldLeft;
            parked = oldRight;

            spread = previousSpread;
            turning = false;

            Promote(left);
            Promote(right);
            Retire(parked);
        });
    }


    private void AnimateTurn(Leaf leaf, float from, float to, bool frontVisibleAfter, TweenCallback onComplete)
    {
        if (leaf == null || leaf.pivot == null)
        {
            onComplete();
            return;
        }

        KillTween(ref leaf.turn);
        leaf.angle = from;

        float angle = from;
        bool switched = false;

        leaf.turn = DOTween.To(() => angle, value =>
        {
            angle = value;
            leaf.angle = value;
            leaf.pivot.localRotation = Quaternion.Euler(0.0f, value, 0.0f);

            if (switched) return;

            bool passedEdge = to > from ? value >= 90.0f : value <= 90.0f;
            if (!passedEdge) return;

            switched = true;
            SetFace(leaf, frontVisibleAfter);
        }, to, turnDuration)
            .SetEase(turnEase)
            .OnComplete(() =>
            {
                SetFace(leaf, frontVisibleAfter);
                onComplete();
            });
    }


    private void ShowLeaf(Leaf leaf, bool frontVisible)
    {
        if (leaf == null || leaf.pivot == null) return;

        leaf.pivot.gameObject.SetActive(true);
        SetFace(leaf, frontVisible);
    }


    private void HideLeaf(Leaf leaf)
    {
        if (leaf == null || leaf.pivot == null) return;

        leaf.pivot.gameObject.SetActive(false);
    }


    private void SetFace(Leaf leaf, bool frontVisible)
    {
        if (leaf == null) return;

        if (leaf.front != null && leaf.front.root != null) leaf.front.root.gameObject.SetActive(frontVisible);
        if (leaf.back != null && leaf.back.root != null) leaf.back.root.gameObject.SetActive(!frontVisible);
    }


    private void BringToFront(Leaf leaf)
    {
        if (leaf == null || leaf.pivot == null || leaves == null) return;

        leaf.pivot.SetSiblingIndex(leaves.Length - 1);
    }


    private void Promote(Leaf leaf)
    {
        if (leaf == null || leaf.pivot == null) return;

        leaf.pivot.localPosition = new Vector3(leaf.pivot.localPosition.x, leaf.pivot.localPosition.y, 0.0f);
    }


    private void Retire(Leaf leaf)
    {
        if (leaf == null || leaf.pivot == null) return;

        HideLeaf(leaf);
        SetAngle(leaf, 0.0f);
        Park(leaf);
    }


    private void Park(Leaf leaf)
    {
        if (leaf == null || leaf.pivot == null) return;

        leaf.pivot.localPosition = new Vector3(leaf.pivot.localPosition.x, leaf.pivot.localPosition.y, hiddenDepth);
    }


    private void SetAngle(Leaf leaf, float angle)
    {
        if (leaf == null || leaf.pivot == null) return;

        leaf.angle = angle;
        leaf.pivot.localRotation = Quaternion.Euler(0.0f, angle, 0.0f);
    }


    private void AssignPage(Page page, int pageIndex)
    {
        if (page == null || page.root == null) return;

        page.pageIndex = pageIndex;

        CompendiumManager manager = CompendiumManager.Instance;
        int count = manager != null ? manager.Count : 0;
        int first = pageIndex * ENTRIES_PER_PAGE;

        bool hasContent = pageIndex >= 0 && first < count;

        if (page.entries == null) return;

        for (int i = 0; i < page.entries.Length; i++)
        {
            CompendiumEntry entry = page.entries[i];
            if (entry == null) continue;

            GameItem item = null;
            if (hasContent && manager != null) manager.TryGetItem(first + i, out item);

            entry.SetItem(item);
            entry.gameObject.SetActive(item != null);
        }
    }


    private void UpdateTurnButtons()
    {
        UpdateTurnButtons(spread);
    }


    private void UpdateTurnButtons(int atSpread)
    {
        if (turnPrevious != null) turnPrevious.gameObject.SetActive(atSpread - 1 >= 0);
        if (turnNext != null) turnNext.gameObject.SetActive(atSpread + 1 < spreadCount);
    }


    // MARK: tween helpers


    private void KillFade()
    {
        if (canvasGroup != null) canvasGroup.DOKill();
    }


    private void KillTurns()
    {
        if (leaves == null) return;

        for (int i = 0; i < leaves.Length; i++)
        {
            if (leaves[i] == null) continue;

            KillTween(ref leaves[i].turn);
        }
    }


    private static void KillTween(ref Tween tween)
    {
        if (tween != null && tween.IsActive()) tween.Kill();

        tween = null;
    }
}
