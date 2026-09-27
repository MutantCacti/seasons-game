using UnityEngine;


// Done once any hive has requiredBees living in it. The puzzle hands out one hive, which the player
// puts in a tree; the bees then move in on their own over a few minutes (HiveSpawnRule), so it is
// settling, not stuck, while a hive can still fill. Credited to the entity the hive is in, so its ping
// hovers over the tree.
//
// The count is derived from the bees' saved homes (Beehive.Residents), so a reload or a checkpoint
// reset comes right on its own.
[CreateAssetMenu(
    fileName = "A Bee Cs",
    menuName = "Puzzles/A Bee Cs"
)]
public class ABeeCs : ScriptedPuzzle
{
    [SerializeField] [Min(1)] private int requiredBees = 3;
    [SerializeField] private Animal bee;

    // only for validation: the item the hive is placed as, whose Beehive holds the per-hive cap
    [SerializeField] private GameItem hiveItem;


    public override void OnAnimalSpawned(WorldContext context, Animal spawned, EntityId entity)
    {
        if (spawned == bee) TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        Beehive fullest = null;
        int most = 0;

        for (int i = 0; i < Beehive.All.Count; i++)
        {
            Beehive hive = Beehive.All[i];
            if (hive == null || hive.Entity == null) continue;

            int residents = hive.Residents;
            if (residents <= most) continue;

            fullest = hive;
            most = residents;
        }

        if (fullest == null || most < requiredBees) return false;

        credit = PuzzleCredit.Entity(fullest.Entity);
        return true;
    }


    // some hive the sea hasn't reached is short of bees, and the mob cap still has room for them
    protected override bool IsStillSettling(WorldContext context)
    {
        if (bee == null) return false;

        int room = bee.MobCap - context.Count(bee);

        for (int i = 0; i < Beehive.All.Count; i++)
        {
            Beehive hive = Beehive.All[i];
            if (hive == null || hive.Entity == null || hive.IsFlooded) continue;

            int missing = requiredBees - hive.Residents;
            if (missing > 0 && missing <= room) return true;
        }

        return false;
    }


    // MARK: validation


    private void OnValidate()
    {
        if (bee == null)
        {
            Debug.LogError($"ABeeCs: '{name}' has no Bee animal, so it can never tell whether bees are still on their way", this);
        }
        else if (requiredBees > bee.MobCap)
        {
            Debug.LogError($"ABeeCs: '{name}' needs {requiredBees} bees in one hive, but the Bee mob cap is {bee.MobCap}, so it can never complete", this);
        }

        if (hiveItem == null)
        {
            Debug.LogError($"ABeeCs: '{name}' has no Hive Item, so the per-hive cap can't be checked", this);
            return;
        }

        Beehive hive = hiveItem.Prefab != null ? hiveItem.Prefab.GetComponentInChildren<Beehive>(true) : null;

        if (hive == null)
        {
            Debug.LogError($"ABeeCs: '{hiveItem.name}' has no Beehive in its prefab, so no bee can ever live in it", this);
            return;
        }

        if (requiredBees > hive.Capacity)
        {
            Debug.LogError($"ABeeCs: '{name}' needs {requiredBees} bees in one hive, but '{hiveItem.name}' holds " +
                $"at most {hive.Capacity}, so it can never complete", this);
        }
    }
}
