using System;
using System.Collections.Generic;


// One entry a compendium tab has marked unseen. kind is the tab's own meaning (a puzzle tab tells
// unlocked, completed and failed apart; a tab with one kind leaves it 0).
[Serializable]
public class UnseenSave
{
    public string id;
    public int kind;
}


// What one tab remembers: the left page of the spread it was last on, what it still has unseen, and,
// for a tab that discovers things, every id the player has ever seen
[Serializable]
public class CompendiumTabSave
{
    public string tabKey;
    public int page;
    public List<UnseenSave> unseen = new List<UnseenSave>();

    // only ever grows; a new game is the one thing that clears it
    public List<string> discovered = new List<string>();

    // false in a save from before discovery was remembered, which then gets seeded from the world once
    public bool discoverySeeded;


    // -1 when the id is not unseen
    public int IndexOfUnseen(string id)
    {
        if (unseen == null) return -1;

        for (int i = 0; i < unseen.Count; i++)
        {
            if (unseen[i] != null && unseen[i].id == id) return i;
        }

        return -1;
    }
}


// Global rather than part of WorldState, so a checkpoint reset never rolls it back. Only a new game
// (a fresh SaveData) clears it.
[Serializable]
public class CompendiumSaveData
{
    public string currentTab;
    public List<CompendiumTabSave> tabs = new List<CompendiumTabSave>();


    // null when the tab has never saved anything
    public CompendiumTabSave FindTab(string tabKey)
    {
        if (tabs == null) return null;

        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i] != null && tabs[i].tabKey == tabKey) return tabs[i];
        }

        return null;
    }


    public CompendiumTabSave GetOrAddTab(string tabKey)
    {
        CompendiumTabSave tab = FindTab(tabKey);
        if (tab != null) return tab;

        if (tabs == null) tabs = new List<CompendiumTabSave>();

        tab = new CompendiumTabSave { tabKey = tabKey };
        tabs.Add(tab);

        return tab;
    }


    // Deterministic output (identical states write identical files)
    public void Sort()
    {
        if (tabs == null) return;

        tabs.Sort((a, b) => string.CompareOrdinal(a != null ? a.tabKey : "", b != null ? b.tabKey : ""));

        for (int i = 0; i < tabs.Count; i++)
        {
            CompendiumTabSave tab = tabs[i];
            if (tab == null) continue;

            if (tab.unseen != null) tab.unseen.Sort((a, b) => string.CompareOrdinal(a != null ? a.id : "", b != null ? b.id : ""));
            if (tab.discovered != null) tab.discovered.Sort(string.CompareOrdinal);
        }
    }
}
