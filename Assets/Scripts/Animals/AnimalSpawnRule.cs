using UnityEngine;


// One way an animal comes into the world. Rules are per animal where that is simpler, and a few
// shared mechanics (NearItemRule, NinePatchTileRule) are reused across animals.
//
// It hears world events through WorldEvents (see WorldListener) and overrides the hooks it cares
// about. Whether it may spawn at all (the unlock, the mob cap) is decided centrally in
// AnimalManager.Spawn, so no rule can forget it.
//
// An event can pass while a rule is not ready: its puzzle still locked, or a reload in between. So
// every rule also answers TryFindSpawn, "could it spawn now, from the world as it is, and where", and
// the load and unlock hooks ask it by default. That is how a rule catches up on what it missed.
public abstract class AnimalSpawnRule : WorldListener
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


    // Some rules must never credit a puzzle (a bred animal is a background population, not progress).
    // Completes still gates the rule as it always did; it just cannot complete anything
    public virtual bool CreditsCompletion { get { return true; } }


    // MARK: the spawn check


    // Could this animal spawn right now, from the world as it is, and where? Abstract on purpose: the
    // catch-up paths depend on it, and a default of "never" would be the easy one to forget.
    // CanSpawn has not necessarily been checked; TrySpawn checks it first.
    protected abstract bool TryFindSpawn(WorldContext context, out Vector3 position);


    protected bool TrySpawn(WorldContext context)
    {
        if (context == null || !context.CanSpawn(this)) return false;
        if (!TryFindSpawn(context, out Vector3 position)) return false;

        return context.Spawn(this, position);
    }


    // MARK: catch-up


    // what was missed while not listening is caught up on at load and at the unlock
    public override void OnLoaded(WorldContext context)
    {
        TrySpawn(context);
    }


    // this rule's own puzzle just unlocked
    public override void OnPuzzleUnlocked(WorldContext context)
    {
        TrySpawn(context);
    }
}
