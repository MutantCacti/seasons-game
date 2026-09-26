using System;
using System.Collections.Generic;
using UnityEngine;


// What phase the world is in: the game clock (day count and time of day) and the progression states
// (the season, and conditions like the lake being habitable). Owns SaveData.world.time and
// SaveData.world.states, so a checkpoint reset rolls both back.
//
// The seasons and conditions are hard coded rather than assets: there are very few of them, and what
// starts or ends each one is wired in code as the game grows. DayNightManager only draws the clock.
//
// Two kinds of event:
//   moments  - DayStarted, Sunrise, Noon, Sunset, Midnight. Not sticky: a script subscribing at 3pm
//              must not hear "noon". CurrentDayPart says which moment was passed last.
//   states   - Loaded, SeasonChanged, ConditionChanged. Sticky: a late subscriber is told the current
//              state straight away, so its handlers must cope with hearing the same state twice.
public class WorldStateManager : MonoBehaviour
{
    private static WorldStateManager instance;
    public static WorldStateManager Instance { get { return instance; } }


    // the scene's values when the clock moved here from DayNightManager
    [Header("Clock")]
    [Tooltip("Duration in seconds")]
    [SerializeField] private float dayDuration = 30f;
    [Tooltip("Duration in seconds")]
    [SerializeField] private float nightDuration = 10f;


    // MARK: clock API


    public enum DayPart
    {
        Sunrise,
        Noon,
        Sunset,
        Midnight,
    }


    public int Day { get; private set; }


    // Normalised day phase: 0 at sunrise, 0.5 at noon, 1 at sunset. Holds 1 throughout the night.
    public float DayPhase01 { get; private set; }


    // The clock dial's rotation in degrees, counter-clockwise positive. 0 is noon (sun centred).
    public float DialAngleDegrees { get; private set; }


    // seconds since sunrise, [0, DayDuration + NightDuration)
    public float CycleTime { get { return cycleTime; } }
    public float DayDuration { get { return dayDuration; } }
    public float NightDuration { get { return nightDuration; } }


    // the last of the four moments the clock passed
    public DayPart CurrentDayPart { get { return PartAt(cycleTime); } }


    // Raised each time the cycle wraps into a new day, just before Sunrise
    public event Action DayStarted;
    public event Action Sunrise;
    public event Action Noon;
    public event Action Sunset;
    public event Action Midnight;


    // MARK: state API


    public Season CurrentSeason { get { return states.season; } }


    // Sticky: raised once the states have been read from the save
    public event Action Loaded
    {
        add
        {
            loaded += value;
            if (isLoaded && value != null) value();
        }
        remove
        {
            loaded -= value;
        }
    }


    // Sticky: a new subscriber hears the current season
    public event Action<Season> SeasonChanged
    {
        add
        {
            seasonChanged += value;
            if (isLoaded && value != null) value(states.season);
        }
        remove
        {
            seasonChanged -= value;
        }
    }


    // Sticky: a new subscriber hears every condition that has started, in its current phase
    public event Action<WorldCondition, ConditionPhase> ConditionChanged
    {
        add
        {
            conditionChanged += value;
            if (!isLoaded || value == null) return;

            for (int i = 0; i < states.conditions.Count; i++)
            {
                ConditionSave entry = states.conditions[i];
                if (entry != null && entry.phase != ConditionPhase.NotStarted) value(entry.condition, entry.phase);
            }
        }
        remove
        {
            conditionChanged -= value;
        }
    }


    // MARK: internals


    private event Action loaded;
    private event Action<Season> seasonChanged;
    private event Action<WorldCondition, ConditionPhase> conditionChanged;

    private bool isLoaded;

    // cached the way AnimalManager caches world: only a new game or a checkpoint restore swaps
    // SaveData.world out, and both reload this scene, so the cache lives for the scene's whole life
    private SaveData save;
    private WorldState world;
    private WorldStatesSaveData states;

    private float cycleTime;
    private double playRemainder;   // sub-millisecond leftover, so no ms is lost per frame


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one WorldStateManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (dayDuration <= 0f || nightDuration <= 0f)
        {
            Debug.LogError("WorldStateManager: dayDuration and nightDuration must be positive, so they have been reset to 30 and 10", this);
            dayDuration = 30f;
            nightDuration = 10f;
        }

        ResolveWorld();
        RestoreTime();
        RefreshTimeApi();

        isLoaded = true;
        loaded?.Invoke();
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }


    private void ResolveWorld()
    {
        SaveDataManager manager = SaveDataManager.Instance;
        save = manager != null ? manager.SaveData : null;
        world = save != null ? save.world : null;

        if (world == null)
        {
            Debug.LogWarning("WorldStateManager: no SaveDataManager, so the time of day, play time and world states will not be saved", this);
            world = new WorldState();
        }

        world.EnsureSections();

        states = world.states;
        if (states.conditions == null) states.conditions = new List<ConditionSave>();
    }


    private void RestoreTime()
    {
        TimeSaveData time = world.time;

        Day = Mathf.Max(1, time.day);

        cycleTime = time.elapsedInCycle;
        if (float.IsNaN(cycleTime) || cycleTime < 0f) cycleTime = 0f;
        cycleTime %= dayDuration + nightDuration;
    }


    // MARK: clock


    private void Update()
    {
        float dt = Time.deltaTime;   // scaled, so a paused game freezes the cycle
        if (dt > 0f) Advance(dt);

        // play time counts in real time, but only while the game is not paused
        if (Time.timeScale > 0f && save != null)
        {
            playRemainder += Time.unscaledDeltaTime * 1000.0;
            long whole = (long)playRemainder;
            if (whole > 0)
            {
                save.playTimeElapsed += whole;
                playRemainder -= whole;
            }
        }

        RefreshTimeApi();
    }


    private void LateUpdate()
    {
        world.time.day = Day;
        world.time.elapsedInCycle = cycleTime;
    }


    // Steps from moment to moment, so a long frame that passes several of them raises every one, in
    // order, with the clock already standing on it
    private void Advance(float dt)
    {
        float total = dayDuration + nightDuration;

        while (dt > 0f)
        {
            float next = NextMoment(cycleTime);
            float step = next - cycleTime;

            if (dt < step)
            {
                cycleTime += dt;
                return;
            }

            dt -= step;
            cycleTime = next;

            if (cycleTime >= total)
            {
                cycleTime = 0f;
                Day++;
                DayStarted?.Invoke();
            }

            RefreshTimeApi();
            RaiseMoment(PartAt(cycleTime));
        }
    }


    // the first moment strictly after t; the day's end is the next sunrise
    private float NextMoment(float t)
    {
        float noon = dayDuration * 0.5f;
        float midnight = dayDuration + nightDuration * 0.5f;

        if (t < noon) return noon;
        if (t < dayDuration) return dayDuration;
        if (t < midnight) return midnight;
        return dayDuration + nightDuration;
    }


    private DayPart PartAt(float t)
    {
        if (t < dayDuration * 0.5f) return DayPart.Sunrise;
        if (t < dayDuration) return DayPart.Noon;
        if (t < dayDuration + nightDuration * 0.5f) return DayPart.Sunset;
        return DayPart.Midnight;
    }


    private void RaiseMoment(DayPart part)
    {
        switch (part)
        {
            case DayPart.Sunrise: Sunrise?.Invoke(); break;
            case DayPart.Noon: Noon?.Invoke(); break;
            case DayPart.Sunset: Sunset?.Invoke(); break;
            case DayPart.Midnight: Midnight?.Invoke(); break;
        }
    }


    private void RefreshTimeApi()
    {
        DayPhase01 = cycleTime < dayDuration ? cycleTime / dayDuration : 1f;
        DialAngleDegrees = cycleTime < dayDuration
            ? -90f + 180f * (cycleTime / dayDuration)
            : 90f + 180f * ((cycleTime - dayDuration) / nightDuration);
    }


    // MARK: states


    public void SetSeason(Season season)
    {
        if (states.season == season) return;

        states.season = season;
        Save();

        seasonChanged?.Invoke(season);
    }


    public ConditionPhase GetPhase(WorldCondition condition)
    {
        ConditionSave entry = Find(condition);
        return entry != null ? entry.phase : ConditionPhase.NotStarted;
    }


    public bool IsActive(WorldCondition condition)
    {
        return GetPhase(condition) == ConditionPhase.Active;
    }


    // a condition runs once: it can only start from not started, and only end while active
    public void StartCondition(WorldCondition condition)
    {
        ConditionPhase phase = GetPhase(condition);

        if (phase != ConditionPhase.NotStarted)
        {
            if (phase == ConditionPhase.Ended) Debug.LogWarning($"WorldStateManager: {condition} has already ended, so it cannot start again", this);
            return;
        }

        SetPhase(condition, ConditionPhase.Active);
    }


    public void EndCondition(WorldCondition condition)
    {
        ConditionPhase phase = GetPhase(condition);

        if (phase != ConditionPhase.Active)
        {
            if (phase == ConditionPhase.NotStarted) Debug.LogWarning($"WorldStateManager: {condition} has not started, so it cannot end", this);
            return;
        }

        SetPhase(condition, ConditionPhase.Ended);
    }


    private void SetPhase(WorldCondition condition, ConditionPhase phase)
    {
        ConditionSave entry = Find(condition);

        if (entry == null)
        {
            entry = new ConditionSave { condition = condition };
            states.conditions.Add(entry);
        }

        entry.phase = phase;
        Save();

        conditionChanged?.Invoke(condition, phase);
    }


    private ConditionSave Find(WorldCondition condition)
    {
        for (int i = 0; i < states.conditions.Count; i++)
        {
            ConditionSave entry = states.conditions[i];
            if (entry != null && entry.condition == condition) return entry;
        }

        return null;
    }


    private void Save()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
    }


    // MARK: debug


    // for testing until progression wires the real triggers. play mode only: the states are in the save
    [ContextMenu("Next Season")]
    private void DebugNextSeason() { if (Application.isPlaying) SetSeason((Season)(((int)states.season + 1) % 4)); }

    [ContextMenu("Start Lake Habitable")]
    private void DebugStartLake() { if (Application.isPlaying) StartCondition(WorldCondition.LakeHabitable); }

    [ContextMenu("End Lake Habitable")]
    private void DebugEndLake() { if (Application.isPlaying) EndCondition(WorldCondition.LakeHabitable); }

    [ContextMenu("Start Bears Hibernating")]
    private void DebugStartBears() { if (Application.isPlaying) StartCondition(WorldCondition.BearsHibernating); }

    [ContextMenu("End Bears Hibernating")]
    private void DebugEndBears() { if (Application.isPlaying) EndCondition(WorldCondition.BearsHibernating); }

    [ContextMenu("Start Sea Risen")]
    private void DebugStartSea() { if (Application.isPlaying) StartCondition(WorldCondition.SeaRisen); }
}
