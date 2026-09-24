using System;
using UnityEngine;
using UnityEngine.UI;


// One tab in the compendium book. Concrete tabs supply a page prefab, a page count and
// a way to bind a page instance to an index; the CompendiumUIManager owns the leaves and
// the turning, and asks the current tab to fill each face through BindPage.
public abstract class CompendiumTabManager : MonoBehaviour
{
    [SerializeField] private Button tabButton;

    public Button TabButton { get { return tabButton; } }


    // Raised when a tab's content changes and its visible pages should be re-bound.
    public event Action Changed;


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
}
