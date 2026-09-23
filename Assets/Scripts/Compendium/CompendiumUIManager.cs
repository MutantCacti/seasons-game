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


    private const float FLAT_THRESHOLD = 0.995f;


    [Serializable]
    public class Page
    {
        public RectTransform root;

        [NonSerialized] public GameObject[] pages;
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

    [Header("Tabs")]
    [SerializeField] private CompendiumTabManager[] tabs;

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
    private int currentTabIndex;
    private bool built;
    private bool turning;
    private Coroutine openRoutine;
    private readonly List<Action> tabChangedHandlers = new List<Action>();


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

        if (tabs == null || tabs.Length == 0)
        {
            Debug.LogError("CompendiumUIManager: no tabs assigned, so the book has no pages to show", this);
        }
        else
        {
            tabChangedHandlers.Clear();

            for (int i = 0; i < tabs.Length; i++)
            {
                CompendiumTabManager tab = tabs[i];

                if (tab == null)
                {
                    tabChangedHandlers.Add(null);
                    continue;
                }

                if (tab.TabButton != null) tab.TabButton.onClick.AddListener(() => ShowTab(tab, 0));

                Action handler = () => OnTabChanged(tab);
                tab.Changed += handler;
                tabChangedHandlers.Add(handler);
            }

            ApplyTabSiblingOrder();
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

        if (tabs != null)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                CompendiumTabManager tab = tabs[i];
                if (tab == null) continue;

                if (tab.TabButton != null) tab.TabButton.onClick.RemoveAllListeners();

                if (i < tabChangedHandlers.Count && tabChangedHandlers[i] != null)
                {
                    tab.Changed -= tabChangedHandlers[i];
                }
            }
        }

        tabChangedHandlers.Clear();

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
        LayoutSpread(0);

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


    private CompendiumTabManager CurrentTab
    {
        get { return tabs != null && currentTabIndex >= 0 && currentTabIndex < tabs.Length ? tabs[currentTabIndex] : null; }
    }


    private int SpreadCount
    {
        get
        {
            CompendiumTabManager tab = CurrentTab;
            int pages = tab != null ? tab.PageCount : 0;
            return (pages + 1) / 2;
        }
    }


    private void LayoutSpread(int target)
    {
        if (leaves == null || leaves.Length < 3) return;

        KillTurns();
        turning = false;
        built = true;

        left = leaves[0];
        right = leaves[1];
        parked = leaves[2];

        SetAngle(left, 180.0f);
        SetAngle(right, 0.0f);
        SetAngle(parked, 0.0f);

        ShowLeaf(left, false);
        ShowLeaf(right, true);
        HideLeaf(parked);

        Promote(left);
        Promote(right);
        Park(parked);

        spread = target;
        spreadCount = SpreadCount;
        AssignSpreadPages(target);
        UpdateTurnButtons();
    }


    private void AssignSpreadPages(int spread)
    {
        if (left == null || right == null || parked == null) return;

        AssignPage(left.front, spread * 2 - 1);
        AssignPage(left.back, spread * 2);
        AssignPage(right.front, spread * 2 + 1);
        AssignPage(right.back, spread * 2 + 2);
        AssignPage(parked.front, spread * 2 + 3);
        AssignPage(parked.back, spread * 2 + 4);
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

        // The leaf pivots share their parent with the tab, turn and close buttons, so "front"
        // can't be a fixed index like leaves.Length - 1. Move to the highest sibling index any
        // leaf currently holds instead, so the turn always draws over its neighbours.
        int front = -1;
        for (int i = 0; i < leaves.Length; i++)
        {
            if (leaves[i] == null || leaves[i].pivot == null) continue;

            int sibling = leaves[i].pivot.GetSiblingIndex();
            if (sibling > front) front = sibling;
        }

        leaf.pivot.SetSiblingIndex(front);
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

        CompendiumTabManager tab = CurrentTab;
        if (tab == null) return;

        EnsurePagesBuilt(page);

        for (int i = 0; i < page.pages.Length; i++)
        {
            if (page.pages[i] != null) page.pages[i].SetActive(i == currentTabIndex);
        }

        GameObject active = currentTabIndex >= 0 && currentTabIndex < page.pages.Length ? page.pages[currentTabIndex] : null;
        if (active != null) tab.BindPage(active, pageIndex);
    }


    private void EnsurePagesBuilt(Page page)
    {
        int tabCount = tabs != null ? tabs.Length : 0;

        if (page.pages != null && page.pages.Length == tabCount) return;

        page.pages = new GameObject[tabCount];

        for (int i = 0; i < page.pages.Length; i++)
        {
            CompendiumTabManager tab = tabs[i];

            if (tab == null || tab.PagePrefab == null)
            {
                page.pages[i] = null;
                continue;
            }

            GameObject instance = Instantiate(tab.PagePrefab, page.root, false);
            StretchToFill(instance);
            page.pages[i] = instance;
        }
    }


    private void StretchToFill(GameObject go)
    {
        RectTransform rect = go != null ? go.transform as RectTransform : null;

        if (rect == null)
        {
            Debug.LogError("CompendiumUIManager: a tab's PagePrefab root is not a RectTransform, so it cannot fill a face", this);
            return;
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        Vector3 position = rect.localPosition;
        rect.localPosition = new Vector3(position.x, position.y, 0.0f);
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


    // MARK: tabs


    private int IndexOfTab(CompendiumTabManager tab)
    {
        if (tabs == null || tab == null) return -1;

        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == tab) return i;
        }

        return -1;
    }


    private void OnTabChanged(CompendiumTabManager tab)
    {
        if (tab != CurrentTab) return;

        AssignSpreadPages(spread);
    }


    public void ShowTab(CompendiumTabManager tab, int pageIndex)
    {
        int index = IndexOfTab(tab);

        if (index < 0)
        {
            Debug.LogWarning("CompendiumUIManager: ShowTab was passed a tab that is not in tabs", this);
            return;
        }

        if (index != currentTabIndex)
        {
            if (CurrentTab != null) CurrentTab.OnHidden();

            currentTabIndex = index;

            if (CurrentTab != null) CurrentTab.OnShown();
        }

        ApplyTabSiblingOrder();
        JumpToSpread(pageIndex);
    }


    private void JumpToSpread(int pageIndex)
    {
        KillTurns();
        turning = false;

        int count = SpreadCount;
        int target = count > 0 ? Mathf.Clamp(pageIndex / 2, 0, count - 1) : 0;

        LayoutSpread(target);
    }


    private void ApplyTabSiblingOrder()
    {
        if (tabs == null) return;

        // unselected buttons are ordered in reverse array order, so the top of the array
        // draws in front; the selected button is then moved to the very front of everything
        int sibling = 0;

        for (int i = tabs.Length - 1; i >= 0; i--)
        {
            CompendiumTabManager tab = tabs[i];
            if (tab == null || tab.TabButton == null) continue;
            if (i == currentTabIndex) continue;

            tab.TabButton.transform.SetSiblingIndex(sibling);
            sibling++;
        }

        CompendiumTabManager selected = CurrentTab;
        if (selected != null && selected.TabButton != null)
        {
            selected.TabButton.transform.SetAsLastSibling();
        }
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
