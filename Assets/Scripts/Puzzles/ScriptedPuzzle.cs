using UnityEngine;


// Who a puzzle's completion is credited to: the thing its ping points at, and what the compendium's
// row goes to. None is allowed; the completion then just counts.
public readonly struct PuzzleCredit
{
    public readonly EntityId entity;


    private PuzzleCredit(EntityId entity)
    {
        this.entity = entity;
    }


    public static PuzzleCredit None { get { return default; } }
    public static PuzzleCredit Entity(EntityId entity) { return new PuzzleCredit(entity); }
}


// A puzzle with its own code: one subclass per puzzle, one asset of it. It hears world events
// through WorldEvents while it is active (unlocked and not completed), and never before or after.
//
// Progress is derived from the world, never counted and saved: IsSatisfied answers "is it done, by the
// world as it is now?", and the load and unlock hooks ask it by default. So a reload, a checkpoint
// rollback, or something done while it was still locked all come right on their own. A subclass
// overrides the hooks that could change the answer and calls TryComplete from them.
public abstract class ScriptedPuzzle : Puzzle
{
    // Is it done, by the world as it is now, and whom to credit? Abstract on purpose: the catch-up
    // paths depend on it, and a default of "never" would be the easy one to forget
    protected abstract bool IsSatisfied(WorldContext context, out PuzzleCredit credit);


    protected bool TryComplete(WorldContext context)
    {
        if (context == null) return false;
        if (!IsSatisfied(context, out PuzzleCredit credit)) return false;

        return context.Complete(this, credit);
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
