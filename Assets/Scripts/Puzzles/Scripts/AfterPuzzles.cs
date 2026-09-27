using System.Collections.Generic;
using UnityEngine;


// Done once every puzzle in requires is completed. It has nothing in the world of its own, so it borrows
// the credit of the last one to complete: its ping lands where that one's did. Caught up after a load or
// an unlock there's no saved order, so the last in the list whose credit still resolves stands in, and
// failing everything, the book.
//
// While any required puzzle can still complete, it waits rather than being judged stuck (Awaits), so a
// spent ingredient (Wood You Be Mine's sapling, already planted) doesn't read as a fail.
[CreateAssetMenu(
    fileName = "After Puzzles",
    menuName = "Puzzles/After Puzzles"
)]
public class AfterPuzzles : ScriptedPuzzle
{
    [Tooltip("completes once every one of these is completed")]
    [SerializeField] private Puzzle[] requires = new Puzzle[0];


    // the required puzzle whose completion is being heard, for its credit. Only set within one call
    [System.NonSerialized] private Puzzle justCompleted;


    public override IReadOnlyList<Puzzle> Awaits
    {
        get { return requires; }
    }


    public override void OnPuzzleCompleted(WorldContext context, Puzzle completed)
    {
        if (!Requires(completed)) return;

        justCompleted = completed;

        try
        {
            TryComplete(context);
        }
        finally
        {
            justCompleted = null;
        }
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (requires == null || requires.Length == 0) return false;

        for (int i = 0; i < requires.Length; i++)
        {
            Puzzle required = requires[i];

            // a hole or itself can never be completed first, and OnValidate has already said so
            if (required == null || required == this) return false;
            if (!context.IsCompleted(required)) return false;
        }

        credit = Credit(context);
        return true;
    }


    private bool Requires(Puzzle puzzle)
    {
        if (puzzle == null || requires == null) return false;

        for (int i = 0; i < requires.Length; i++)
        {
            if (requires[i] == puzzle) return true;
        }

        return false;
    }


    // MARK: credit


    // the one that just finished it, else the last one listed that still resolves, else the book
    private PuzzleCredit Credit(WorldContext context)
    {
        if (justCompleted != null && context.TryGetCredit(justCompleted, out PuzzleCredit credit)) return credit;

        for (int i = requires.Length - 1; i >= 0; i--)
        {
            if (context.TryGetCredit(requires[i], out credit)) return credit;
        }

        // the fail ping's target too: anywhere but over the inventory
        EntityId book = CompendiumUIManager.Instance != null
            ? CompendiumUIManager.Instance.GetComponentInParent<EntityId>()
            : null;

        return book != null ? PuzzleCredit.Entity(book) : PuzzleCredit.None;
    }


    // MARK: validation


    private void OnValidate()
    {
        if (requires == null || requires.Length == 0)
        {
            Debug.LogError($"AfterPuzzles: '{name}' requires nothing, so it can never complete", this);
            return;
        }

        for (int i = 0; i < requires.Length; i++)
        {
            if (requires[i] == null)
            {
                Debug.LogError($"AfterPuzzles: '{name}' requires entry {i} is empty, so it can never complete", this);
            }
            else if (requires[i] == this)
            {
                Debug.LogError($"AfterPuzzles: '{name}' requires itself, so it can never complete", this);
            }
        }

#if UNITY_EDITOR
        WarnAboutUnreachable();
#endif
    }


#if UNITY_EDITOR
    // a required puzzle nothing unlocks and no new world starts with will never complete. Only against
    // the registry's own puzzles, which is what the game knows about
    private void WarnAboutUnreachable()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:PuzzleRegistry");
        if (guids.Length == 0) return;

        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
        PuzzleRegistry registry = UnityEditor.AssetDatabase.LoadAssetAtPath<PuzzleRegistry>(path);
        if (registry == null) return;

        for (int i = 0; i < requires.Length; i++)
        {
            Puzzle required = requires[i];
            if (required == null || required == this) continue;

            if (!CanEverUnlock(registry, required))
            {
                Debug.LogWarning($"AfterPuzzles: '{name}' requires '{required.name}', which no registered puzzle " +
                    $"unlocks and isn't a starting puzzle, so '{name}' can never complete", this);
            }
        }
    }


    private static bool CanEverUnlock(PuzzleRegistry registry, Puzzle puzzle)
    {
        IReadOnlyList<Puzzle> starting = registry.StartingPuzzles;

        for (int i = 0; starting != null && i < starting.Count; i++)
        {
            if (starting[i] == puzzle) return true;
        }

        IReadOnlyList<Puzzle> entries = registry.Entries;

        for (int i = 0; entries != null && i < entries.Count; i++)
        {
            Puzzle parent = entries[i];
            if (parent == null || parent.Unlocks == null) continue;

            for (int j = 0; j < parent.Unlocks.Count; j++)
            {
                if (parent.Unlocks[j] == puzzle) return true;
            }
        }

        return false;
    }
#endif
}
