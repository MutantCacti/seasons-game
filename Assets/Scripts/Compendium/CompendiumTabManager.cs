using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


// How loudly a tab's unseen entries call for attention. Ordered, so the larger value wins when tabs
// are combined (the book glow takes the max over every tab).
public enum UnseenPriority
{
    None = 0,
    Discovery = 1,
    Completion = 2,
    SoftLock = 3,
}


// One tab in the compendium book. Concrete tabs supply a page prefab, a page count and
// a way to bind a page instance to an index; the CompendiumUIManager owns the leaves and
// the turning, and asks the current tab to fill each face through BindPage.
public abstract class CompendiumTabManager : MonoBehaviour
{
    [SerializeField] private Button tabButton;

    public Button TabButton { get { return tabButton; } }


    // Raised when a tab's content changes and its visible pages should be re-bound.
    public event Action Changed;


    // Raised whenever any tab's unseen list actually changes, batched or not. Static so the book can
    // listen once for every tab, including ones that bind before it exists.
    public static event Action UnseenChanged;


    // May be null: the tab then instantiates nothing and its faces stay empty (just the
    // paper background the face itself provides).
    public abstract GameObject PagePrefab { get; }


    public abstract int PageCount { get; }


    // pageIndex may be -1 or past the end, in which case the page must render blank.
    public abstract void BindPage(GameObject page, int pageIndex);


    public virtual void OnShown() { }


    public virtual void OnHidden() { }


    // Asked each time the book opens. A tab with something urgent to show (a failed puzzle, say)
    // returns true and the book opens on it at pageIndex; otherwise the book opens as it always does.
    public virtual bool TryGetRequestedPage(out int pageIndex)
    {
        pageIndex = 0;
        return false;
    }


    protected void RaiseChanged()
    {
        if (Changed != null) Changed();
    }


    // The loudest thing this tab has unseen. The base only knows that something is unseen, so anything
    // counts as a discovery; tabs whose kinds mean more override this.
    public virtual UnseenPriority HighestUnseen()
    {
        int count = UnseenCount;

        for (int i = 0; i < count; i++)
        {
            if (TryGetUnseenAt(i, out int _)) return UnseenPriority.Discovery;
        }

        return UnseenPriority.None;
    }


    // MARK: saved state


    // Names this tab's entry in the save. The class name, so nothing needs wiring; renaming the class
    // orphans whatever the old name saved, which the next save simply leaves behind.
    protected virtual string SaveKey { get { return GetType().Name; } }


    // read-only for the book, which remembers which tab was open by this key
    public string TabKey { get { return SaveKey; } }


    private bool warnedAboutMissingSave;


    // The left page of the spread this tab was last on. Setting it saves, but only when it changed,
    // so a book that settles on the same spread again does not rewrite the file.
    public int RememberedPage
    {
        get
        {
            CompendiumTabSave tab = TabSave;
            return tab != null ? tab.page : 0;
        }
        set
        {
            CompendiumTabSave tab = TabSave;
            if (tab == null || tab.page == value) return;

            tab.page = value;
            SaveCompendium();
        }
    }


    protected bool TryGetUnseen(string id, out int kind)
    {
        kind = 0;

        if (string.IsNullOrEmpty(id)) return false;

        CompendiumTabSave tab = TabSave;
        if (tab == null) return false;

        int index = tab.IndexOfUnseen(id);
        if (index < 0) return false;

        kind = tab.unseen[index].kind;
        return true;
    }


    protected int UnseenCount
    {
        get
        {
            CompendiumTabSave tab = TabSave;
            return tab != null && tab.unseen != null ? tab.unseen.Count : 0;
        }
    }


    // By position, so an override can walk the kinds without copying the list
    protected bool TryGetUnseenAt(int index, out int kind)
    {
        kind = 0;

        CompendiumTabSave tab = TabSave;
        if (tab == null || tab.unseen == null || index < 0 || index >= tab.unseen.Count) return false;

        UnseenSave entry = tab.unseen[index];
        if (entry == null) return false;

        kind = entry.kind;
        return true;
    }


    // Overwrites any earlier kind. Pass save false inside a batch and call SaveCompendium once after.
    protected void MarkUnseen(string id, int kind, bool save = true)
    {
        if (string.IsNullOrEmpty(id)) return;

        CompendiumTabSave tab = TabSave;
        if (tab == null) return;

        if (tab.unseen == null) tab.unseen = new List<UnseenSave>();

        int index = tab.IndexOfUnseen(id);

        if (index >= 0)
        {
            if (tab.unseen[index].kind == kind) return;

            tab.unseen[index].kind = kind;
        }
        else
        {
            tab.unseen.Add(new UnseenSave { id = id, kind = kind });
        }

        RaiseUnseenChanged();

        if (save) SaveCompendium();
    }


    protected void ClearUnseen(string id, bool save = true)
    {
        if (string.IsNullOrEmpty(id)) return;

        CompendiumTabSave tab = TabSave;
        if (tab == null) return;

        int index = tab.IndexOfUnseen(id);
        if (index < 0) return;

        tab.unseen.RemoveAt(index);

        RaiseUnseenChanged();

        if (save) SaveCompendium();
    }


    // A copy, so a load can prune the real list while walking this one
    protected void CopyUnseen(List<UnseenSave> results)
    {
        results.Clear();

        CompendiumTabSave tab = TabSave;
        if (tab == null || tab.unseen == null) return;

        for (int i = 0; i < tab.unseen.Count; i++)
        {
            UnseenSave entry = tab.unseen[i];
            if (entry != null) results.Add(new UnseenSave { id = entry.id, kind = entry.kind });
        }
    }


    private static void RaiseUnseenChanged()
    {
        if (UnseenChanged != null) UnseenChanged();
    }


    protected void SaveCompendium()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
    }


    // Looked up on every use and never cached: binds can arrive before Awake, and a new game swaps
    // the whole SaveData out from under us
    private CompendiumTabSave TabSave
    {
        get
        {
            SaveDataManager manager = SaveDataManager.Instance;

            if (manager == null || manager.SaveData == null)
            {
                if (!warnedAboutMissingSave)
                {
                    warnedAboutMissingSave = true;
                    Debug.LogError($"{GetType().Name}: no SaveDataManager, so this tab's page and unseen entries are not remembered", this);
                }

                return null;
            }

            if (manager.SaveData.compendium == null) manager.SaveData.compendium = new CompendiumSaveData();

            return manager.SaveData.compendium.GetOrAddTab(SaveKey);
        }
    }
}
