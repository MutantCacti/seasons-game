using System;
using System.Collections.Generic;


// JsonUtility writes an enum as its int, so these values are pinned: never reorder or renumber them


// One at a time; a season ends when the next begins
public enum Season
{
    Summer = 0,
    Autumn = 1,
    Winter = 2,
    Spring = 3,
}


// Started, and optionally ended, by progression
public enum WorldCondition
{
    LakeHabitable = 0,
    BearsHibernating = 1,
    SeaRisen = 2,
}


public enum ConditionPhase
{
    NotStarted = 0,
    Active = 1,
    Ended = 2,
}


[Serializable]
public class ConditionSave
{
    public WorldCondition condition;
    public ConditionPhase phase;
}


// The season and every condition that has started. Lives in the world, so a checkpoint reset rolls
// them back with everything else. A condition that is not listed has not started.
[Serializable]
public class WorldStatesSaveData
{
    public Season season = Season.Summer;
    public List<ConditionSave> conditions = new List<ConditionSave>();


    // Deterministic output (identical worlds write identical files)
    public void Sort()
    {
        if (conditions == null) return;

        conditions.Sort((a, b) => (a != null ? (int)a.condition : -1).CompareTo(b != null ? (int)b.condition : -1));
    }
}
