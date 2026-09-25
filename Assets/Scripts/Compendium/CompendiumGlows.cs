using UnityEngine;
using UnityEngine.UI;


// Points the way to unseen entries inside the open book. Each tab pulses in the colour of the loudest
// thing it has unseen, except the tab being read, whose own pages already show their outlines. Next and
// Previous pulse for whatever is unseen on the pages beyond them in the current tab. The glows are the
// SpriteGlow children of the tab and turn buttons, found rather than wired. Like BookGlow, this only
// recomputes on a dirty flag, never every frame, and never clears anything on hover.
public class CompendiumGlows : MonoBehaviour
{
    [SerializeField] private CompendiumUIManager compendium;

    [Header("Colours")]
    [SerializeField] private Color discoveryColour = new Color(237.0f / 255.0f, 196.0f / 255.0f, 59.0f / 255.0f, 1.0f);   // #EDC43B, as BookGlow
    [SerializeField] private Color completionColour = new Color(141.0f / 255.0f, 191.0f / 255.0f, 90.0f / 255.0f, 1.0f);  // #8DBF5A, as BookGlow
    [SerializeField] private Color softLockColour = new Color(210.0f / 255.0f, 87.0f / 255.0f, 63.0f / 255.0f, 1.0f);     // #D2573F, as BookGlow


    private SpriteGlow[] tabGlows;
    private SpriteGlow previousGlow;
    private SpriteGlow nextGlow;

    // starts dirty, so the first LateUpdate settles whatever the tabs bound before this woke up
    private bool dirty = true;


    // MARK: lifecycle


    private void Awake()
    {
        if (compendium == null) compendium = GetComponent<CompendiumUIManager>();
        if (compendium == null) Debug.LogError("CompendiumGlows: no CompendiumUIManager, so no tab or turn button can glow", this);
    }


    // After every Awake, so the book's tabs and buttons are all there to be searched
    private void Start()
    {
        FindGlows();

        CompendiumTabManager.UnseenChanged += MarkDirty;

        if (compendium != null)
        {
            compendium.OpenChanged += MarkDirty;
            compendium.SpreadChanged += MarkDirty;
        }

        // belt to the UnseenChanged braces, as BookGlow does. Sticky, so it answers at once if loaded.
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.OnPuzzlesLoaded += MarkDirty;
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.NewWorldConstructed += MarkDirty;

        dirty = true;
    }


    private void OnDestroy()
    {
        CompendiumTabManager.UnseenChanged -= MarkDirty;

        if (compendium != null)
        {
            compendium.OpenChanged -= MarkDirty;
            compendium.SpreadChanged -= MarkDirty;
        }

        if (PuzzleManager.Instance != null) PuzzleManager.Instance.OnPuzzlesLoaded -= MarkDirty;
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.NewWorldConstructed -= MarkDirty;
    }


    // one recompute per frame at most, however many entries a load marks in a batch
    private void LateUpdate()
    {
        if (!dirty) return;

        dirty = false;
        Refresh();
    }


    private void MarkDirty()
    {
        dirty = true;
    }


    // MARK: glows


    private void Refresh()
    {
        if (compendium == null) return;

        // Keyed off where the read is going, not IsReading. Closed, everything goes dark: the canvas
        // is off anyway, and opening marks this dirty again.
        bool open = compendium.IsOpen;
        int current = compendium.CurrentTabIndex;

        if (tabGlows != null)
        {
            for (int i = 0; i < tabGlows.Length; i++)
            {
                CompendiumTabManager tab = compendium.GetTab(i);
                bool signpost = open && i != current && tab != null;

                Apply(tabGlows[i], signpost ? tab.HighestUnseen() : UnseenPriority.None);
            }
        }

        UnseenPriority previous = UnseenPriority.None;
        UnseenPriority next = UnseenPriority.None;
        CompendiumTabManager currentTab = compendium.GetTab(current);

        if (open && currentTab != null)
        {
            // the target spread shows pages 2s and 2s + 1; Previous covers everything before, Next after
            int firstShown = compendium.TargetSpread * 2;

            previous = currentTab.HighestUnseenInPages(0, firstShown - 1);
            next = currentTab.HighestUnseenInPages(firstShown + 2, currentTab.PageCount - 1);
        }

        Apply(previousGlow, previous);
        Apply(nextGlow, next);
    }


    private void Apply(SpriteGlow glow, UnseenPriority priority)
    {
        if (glow == null) return;

        if (priority == UnseenPriority.None) glow.Hide();
        else glow.Show(ColourOf(priority));
    }


    private Color ColourOf(UnseenPriority value)
    {
        switch (value)
        {
            case UnseenPriority.SoftLock: return softLockColour;
            case UnseenPriority.Completion: return completionColour;
            default: return discoveryColour;
        }
    }


    // MARK: setup


    private void FindGlows()
    {
        if (compendium == null) return;

        int count = compendium.TabCount;
        tabGlows = new SpriteGlow[count];

        for (int i = 0; i < count; i++)
        {
            CompendiumTabManager tab = compendium.GetTab(i);
            if (tab == null) continue;

            tabGlows[i] = GlowUnder(tab.TabButton, "tab " + tab.name);
        }

        previousGlow = GlowUnder(compendium.TurnPreviousButton, "Turn Previous");
        nextGlow = GlowUnder(compendium.TurnNextButton, "Turn Next");
    }


    // inactive ones too: the turn buttons are switched off on the first and last spreads
    private SpriteGlow GlowUnder(Button button, string label)
    {
        if (button == null) return null;

        SpriteGlow glow = button.GetComponentInChildren<SpriteGlow>(true);

        if (glow == null)
        {
            Debug.LogError($"CompendiumGlows: {label} has no SpriteGlow child, so it cannot point to unseen entries", button);
        }

        return glow;
    }
}
