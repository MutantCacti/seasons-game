using System;
using System.Collections.Generic;
using UnityEngine;


// What a spawn rule is allowed to touch. AnimalManager implements it, so a rule never has to reach
// for singletons and cannot spawn outside the manager's bookkeeping.
public interface AnimalSpawnContext
{
    TerrainManager Terrain { get; }

    // null in a scene without one; a rule that gates on a season or condition then never spawns
    WorldStateManager States { get; }

    // live animals of this kind
    int Count(Animal animal);

    // whether Spawn would accept this rule right now: registered, its puzzle unlocked (or completed),
    // and its animal under its mob cap. Cheap, so check it before an expensive scan
    bool CanSpawn(AnimalSpawnRule rule);

    // refuses anything CanSpawn would refuse, so calling it too often is harmless
    bool Spawn(AnimalSpawnRule rule, Vector3 position);

    // world positions of every placed instance of this item: its entities and, for a tile item, its
    // cells' tops. Fills the caller's list
    void CollectPositionsOf(GameItem item, List<Vector3> results);

    // the nearest instance of this item (entity or tile) within radius world units of point
    bool FindWithin(Vector3 point, float radius, GameItem item, out Vector3 found);

    // a random top-of-column cell whose tile passes accept (null accepts any), optionally only
    // among cells the game camera cannot currently see
    bool TryRandomSurface(Predicate<TerrainTile> accept, bool offScreen, out Vector3 position);
}


// One way an animal comes into the world. Rules are per animal where that is simpler, and a few
// shared mechanics (NearItemRule, NinePatchTileRule) are reused across animals.
//
// AnimalManager forwards world events to every rule through the virtual hooks below; a rule overrides
// the ones it cares about. Whether it may spawn at all (the unlock, the mob cap) is decided centrally
// in Spawn, so no rule can forget it.
//
// An event can pass while a rule is not ready: its puzzle still locked, or a reload in between. So
// every rule also answers TryFindSpawn, "could it spawn now, from the world as it is, and where", and
// the load and unlock hooks ask it by default. That is how a rule catches up on what it missed.
public abstract class AnimalSpawnRule : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] private Animal animal;

    // optional: the puzzle this spawn completes. The rule only spawns while that puzzle is unlocked
    // (or completed); with none, it is never gated
    [SerializeField] private Puzzle completes;


    // falls back to the asset name, so a rule with no id is still found by the registry
    public string Id { get { return string.IsNullOrEmpty(id) ? name : id; } }
    public Animal Animal { get { return animal; } }
    public Puzzle Completes { get { return completes; } }


    // MARK: the spawn check


    // Could this animal spawn right now, from the world as it is, and where? Abstract on purpose: the
    // catch-up paths depend on it, and a default of "never" would be the easy one to forget.
    // CanSpawn has not necessarily been checked; TrySpawn checks it first.
    protected abstract bool TryFindSpawn(AnimalSpawnContext context, out Vector3 position);


    protected bool TrySpawn(AnimalSpawnContext context)
    {
        if (context == null || !context.CanSpawn(this)) return false;
        if (!TryFindSpawn(context, out Vector3 position)) return false;

        return context.Spawn(this, position);
    }


    // MARK: hooks


    // once per scene load (a new world, a reload, a checkpoint reset), after puzzles, world states and
    // entities are all loaded
    public virtual void OnLoaded(AnimalSpawnContext context)
    {
        TrySpawn(context);
    }


    // this rule's own puzzle just unlocked
    public virtual void OnPuzzleUnlocked(AnimalSpawnContext context)
    {
        TrySpawn(context);
    }


    // after a placement has been committed, consumed and saved
    public virtual void OnItemPlaced(AnimalSpawnContext context, GameItem item, Vector3Int cell)
    {
    }


    // any rule's spawn, this one's included
    public virtual void OnAnimalSpawned(AnimalSpawnContext context, Animal spawned, EntityId entity)
    {
    }


    public virtual void OnSeasonChanged(AnimalSpawnContext context, Season season)
    {
    }


    public virtual void OnConditionChanged(AnimalSpawnContext context, WorldCondition condition, ConditionPhase phase)
    {
    }


    // the day's moments. A population rule checks here rather than every frame
    public virtual void OnSunrise(AnimalSpawnContext context)
    {
    }


    public virtual void OnNoon(AnimalSpawnContext context)
    {
    }


    public virtual void OnSunset(AnimalSpawnContext context)
    {
    }


    public virtual void OnMidnight(AnimalSpawnContext context)
    {
    }
}
