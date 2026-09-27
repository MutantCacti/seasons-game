using System;
using System.Collections.Generic;
using UnityEngine;


// What a world listener (a spawn rule, a scripted puzzle) is allowed to touch. WorldEvents implements
// it, so a listener never has to reach for singletons, and spawning and completing always go through
// their managers' bookkeeping.
public interface WorldContext
{
    TerrainManager Terrain { get; }

    // null in a scene without one; a listener that gates on a season or condition then never acts
    WorldStateManager States { get; }

    // True while a placement's tile transition is still growing the ground over a cell. The world is
    // remapped the moment a tile is placed, so a listener that acts on what the player can see has to
    // wait for this to clear before it fires
    bool IsPresenting { get; }

    // live animals of this kind
    int Count(Animal animal);

    // every live animal of this kind, so a rule can look at each one (a breeder and the clock stamp it
    // carries). Fills the caller's list
    void CollectAnimals(Animal animal, List<AnimalController> results);

    // world positions of every placed instance of this item: its entities and, for a tile item, its
    // cells' tops. Fills the caller's list
    void CollectPositionsOf(GameItem item, List<Vector3> results);

    // the nearest instance of this item (entity or tile) within radius world units of point
    bool FindWithin(Vector3 point, float radius, GameItem item, out Vector3 found);

    // a random top-of-column cell whose tile passes accept (null accepts any), optionally only
    // among cells the game camera cannot currently see
    bool TryRandomSurface(Predicate<TerrainTile> accept, bool offScreen, out Vector3 position);


    // MARK: spawning


    // whether Spawn would accept this rule right now: registered, its puzzle unlocked (or completed),
    // and its animal under its mob cap. Cheap, so check it before an expensive scan
    bool CanSpawn(AnimalSpawnRule rule);

    // refuses anything CanSpawn would refuse, so calling it too often is harmless
    bool Spawn(AnimalSpawnRule rule, Vector3 position);

    // the same, with the guid of the entity the animal lives in (a bee's hive) saved on it
    bool Spawn(AnimalSpawnRule rule, Vector3 position, string homeGuid);


    // MARK: puzzles


    // How many of this item the player has placed that are in the world right now. For a tile item,
    // the cells the player changed that hold its tile NOW: one later changed again (dirt watered into
    // grass) stops counting, so a puzzle that must count "ever placed" should complete as soon as it
    // can. The world's own authored tiles and starting fixtures never count
    int CountPlaced(GameItem item);

    // the cells behind CountPlaced for a tile item. Fills the caller's list
    void CollectPlacedCells(GameItem item, List<Vector3Int> results);

    // through PuzzleManager.Complete, with its queue, ping and completedBy
    bool Complete(Puzzle puzzle, PuzzleCredit credit);

    bool IsCompleted(Puzzle puzzle);

    // what a completed puzzle was credited to, resolved: its entity if it is still in the world, else
    // its cell. False when it isn't completed, was credited to nothing, or neither resolves
    bool TryGetCredit(Puzzle puzzle, out PuzzleCredit credit);

    // every one of this active puzzle's ingredients has been spent (its ledger is all zeroes)
    bool IsExhausted(Puzzle puzzle);
}


// Something that hears the world's events through WorldEvents (a spawn rule, a scripted puzzle):
// every hook is empty here, and a listener overrides the ones it cares about.
//
// Nothing is heard until the world has finished loading, and then OnLoaded comes once. An event a
// listener causes while hearing another (a spawn inside OnAnimalSpawned) is queued and heard after, so
// a chain resolves in one frame without the listeners being walked twice at once.
public abstract class WorldListener : ScriptableObject
{
    // once per scene load (a new world, a reload, a checkpoint reset), after puzzles, world states and
    // entities are all loaded
    public virtual void OnLoaded(WorldContext context)
    {
    }


    // the puzzle this listener belongs to just unlocked: for a puzzle itself, for a spawn rule the one
    // it completes
    public virtual void OnPuzzleUnlocked(WorldContext context)
    {
    }


    // after a placement has been committed, consumed (if it came from the inventory) and saved. A growth
    // is a placement too: this hears the grown item, and nothing hears the old one leave
    public virtual void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
    }


    // the placement's presentation is over: its tile transition finished, or nothing played. A rule
    // that reacts to what has just appeared hears it here, so it never fires while the ground is still
    // changing. Raised for every placement, exactly once
    public virtual void OnItemShown(WorldContext context, GameItem item, Vector3Int cell)
    {
    }


    // a cell changed tile without a placement (an animal spreading greenery). Heard once its tile
    // transition has finished, like OnItemShown, so the new tile is what the player sees. The remap
    // is already saved and counts as the player's own, so tile puzzles re-check here
    public virtual void OnTerrainMutated(WorldContext context, Vector3Int cell, TerrainTile from, TerrainTile to)
    {
    }


    // any puzzle's live completion, once it is saved. Never raised for a puzzle restored as completed
    // by a load: catch up on those in OnLoaded and OnPuzzleUnlocked
    public virtual void OnPuzzleCompleted(WorldContext context, Puzzle completed)
    {
    }


    // any spawn rule's spawn
    public virtual void OnAnimalSpawned(WorldContext context, Animal spawned, EntityId entity)
    {
    }


    public virtual void OnSeasonChanged(WorldContext context, Season season)
    {
    }


    public virtual void OnConditionChanged(WorldContext context, WorldCondition condition, ConditionPhase phase)
    {
    }


    // the day's moments. Something that depends on a population checks here rather than every frame
    public virtual void OnSunrise(WorldContext context)
    {
    }


    public virtual void OnNoon(WorldContext context)
    {
    }


    public virtual void OnSunset(WorldContext context)
    {
    }


    public virtual void OnMidnight(WorldContext context)
    {
    }


    // once every WorldEvents.TICK_INTERVAL seconds of game time (so a paused game doesn't tick), for a
    // listener that works on a timer. seconds is the game time since the last tick. Keep a timer in a
    // [NonSerialized] field and reset it in OnLoaded: a ScriptableObject outlives the scene reload a
    // checkpoint reset does, and so does anything it holds
    public virtual void OnTick(WorldContext context, float seconds)
    {
    }
}
