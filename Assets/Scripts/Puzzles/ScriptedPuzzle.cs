using System.Collections.Generic;
using UnityEngine;


// A puzzle with its own code: one subclass per puzzle, one asset of it. It hears world events
// through WorldEvents while it is active (unlocked and not completed), and never before or after.
//
// Progress is derived from the world, never counted and saved: IsSatisfied answers "is it done, by the
// world as it is now?", and the load and unlock hooks ask it by default. So a reload, a checkpoint
// rollback, or something done while it was still locked all come right on their own. A subclass
// overrides the hooks that could change the answer and calls TryComplete from them.
public abstract class ScriptedPuzzle : Puzzle
{
    // Taken by the credit helpers below, which fill and read one within a single call, so nothing is
    // left in them between calls
    private static readonly List<EntityId> scratchEntities = new List<EntityId>();
    private static readonly List<EntityManager.LiveEntity> scratchLive = new List<EntityManager.LiveEntity>();


    // Is it done, by the world as it is now, and whom to credit? Abstract on purpose: the catch-up
    // paths depend on it, and a default of "never" would be the easy one to forget
    protected abstract bool IsSatisfied(WorldContext context, out PuzzleCredit credit);


    // True while the world is still on its way to satisfying it without the player: everything is
    // spent, but what completes it arrives on its own over time (bees moving into a hive). A soft lock
    // is held off meanwhile. It should only ever go from true to false: PuzzleManager skips rather than
    // clears on it, so it never withdraws a flag it already raised
    protected virtual bool IsStillSettling(WorldContext context)
    {
        return false;
    }


    // for PuzzleManager's soft-lock judgement
    public bool IsSettling(WorldContext context)
    {
        return context != null && IsStillSettling(context);
    }


    protected bool TryComplete(WorldContext context)
    {
        if (context == null) return false;
        if (!IsSatisfied(context, out PuzzleCredit credit)) return false;

        return context.Complete(this, credit);
    }


    // MARK: credit


    // A live, non-fixture entity of this item standing on the cell, for a puzzle that was completed by
    // something the player just placed. A null item accepts any live, non-fixture entity there, for a
    // placement that is some other item's result (the sprout a fertiliser made).
    //
    // WorldContext has no "what stands on this cell" query, so this asks EntityManager directly, as
    // WorldEvents' own CountPlaced does
    protected static EntityId PlacedEntityAt(Vector3Int cell, GameItem item)
    {
        EntityManager entities = EntityManager.Instance;
        if (entities == null) return null;

        entities.CollectLiveAt(cell, scratchEntities);

        // a cell can hold more than one entity, so take the one that is actually wanted
        for (int i = 0; i < scratchEntities.Count; i++)
        {
            if (!entities.TryGetLive(scratchEntities[i], out EntityManager.LiveEntity live)) continue;
            if (live.entityId == null || live.fixture) continue;
            if (item != null && live.item != item) continue;

            return live.entityId;
        }

        return null;
    }


    // Caught up after a load or an unlock, when there is no "just placed": any placed one will do, the
    // last to arrive. Null item means any live, non-fixture entity, as above
    protected static EntityId AnyPlacedEntity(GameItem item)
    {
        EntityManager entities = EntityManager.Instance;
        if (entities == null) return null;

        entities.CollectLive(scratchLive);

        EntityId found = null;

        for (int i = 0; i < scratchLive.Count; i++)
        {
            EntityManager.LiveEntity live = scratchLive[i];
            if (live.entityId == null || live.fixture) continue;
            if (item != null && live.item != item) continue;

            found = live.entityId;
        }

        return found;
    }


    // A checkpoint reset despawns what was just placed while the puzzle asset, which outlives the
    // scene, still remembers it: a dead reference would complete with nothing for the ping to follow
    protected static bool IsLive(EntityId entity)
    {
        if (entity == null) return false;

        EntityManager entities = EntityManager.Instance;

        return entities != null && entities.TryGetLive(entity, out _);
    }


    // MARK: catch-up


    // whatever was done while this wasn't listening: before a reload, or before it unlocked
    public override void OnLoaded(WorldContext context)
    {
        TryComplete(context);
    }


    public override void OnPuzzleUnlocked(WorldContext context)
    {
        TryComplete(context);
    }
}
