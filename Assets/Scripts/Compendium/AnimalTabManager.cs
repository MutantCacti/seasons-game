using UnityEngine;


// Stub tab: no page prefab and no pages, so its faces show just the empty paper background.
// A later agent replaces this with the real Animals tab.
public class AnimalTabManager : CompendiumTabManager
{
    public override GameObject PagePrefab { get { return null; } }


    public override int PageCount { get { return 0; } }


    public override void BindPage(GameObject page, int pageIndex)
    {
    }
}
