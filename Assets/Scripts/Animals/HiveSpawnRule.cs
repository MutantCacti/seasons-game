using UnityEngine;


// Every cooldown seconds of daytime, a random hive with room gets a new bee at its exit, who calls
// it home. A hive holds Beehive.Capacity bees at most; the animal's mob cap applies on top.
//
// Only by day: real bees don't leave the hive at night, so the timer holds overnight and the first
// new bee of the morning comes out shortly after sunrise. A newly placed hive gets its first bee
// quickly, so placing one pays off where the player can see it.
//
// The cooldown is not saved. A load (a new session, a reload, a checkpoint reset) starts it part way,
// and nothing spawns on the load itself, or every reload would pop a bee.
[CreateAssetMenu(
    fileName = "HiveSpawnRule",
    menuName = "Animals/Spawn Rules/Hive"
)]
public class HiveSpawnRule : AnimalSpawnRule
{
    [SerializeField] [Min(1.0f)] private float cooldown = 45.0f;               // seconds of game time
    [SerializeField] [Min(0.0f)] private float firstBeeDelay = 5.0f;           // after a hive is placed
    [SerializeField] [Range(0.0f, 1.0f)] private float loadedFraction = 0.5f;  // of the cooldown, left after a load

    // the share of the day (0 sunrise, 1 sunset) new bees come out in: not so late they must turn
    // straight round for home
    [SerializeField] private Vector2 dayWindow = new Vector2(0.05f, 0.8f);


    // a ScriptableObject outlives the scene, so this is reset in OnLoaded
    [System.NonSerialized] private float timer;


    public override void OnLoaded(WorldContext context)
    {
        timer = cooldown * loadedFraction;
    }


    // the timer is already running; the unlock only lets it spawn
    public override void OnPuzzleUnlocked(WorldContext context)
    {
    }


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item == null || item.Prefab == null || item.Prefab.GetComponent<Beehive>() == null) return;

        timer = Mathf.Min(timer, firstBeeDelay);
    }


    public override void OnTick(WorldContext context, float seconds)
    {
        if (context == null || !InDayWindow(context.States)) return;

        timer -= seconds;
        if (timer > 0.0f) return;

        // no room anywhere, or at the mob cap: try again next tick, so a new hive or an adoption that
        // frees one is answered straight away
        timer = 0.0f;

        if (!context.CanSpawn(this)) return;

        Beehive hive = PickHive();
        if (hive == null) return;

        if (context.Spawn(this, hive.ExitPosition, hive.Guid))
        {
            timer = cooldown;
            hive.Rustle();
        }
    }


    // only the catch-up paths ask this, and this rule overrides both not to spawn; kept truthful anyway
    protected override bool TryFindSpawn(WorldContext context, out Vector3 position)
    {
        position = default;

        Beehive hive = PickHive();
        if (hive == null) return false;

        position = hive.ExitPosition;
        return true;
    }


    // MARK: internals


    // a random hive with room that the sea hasn't reached, picked without keeping a list
    private static Beehive PickHive()
    {
        Beehive chosen = null;
        int seen = 0;

        for (int i = 0; i < Beehive.All.Count; i++)
        {
            Beehive hive = Beehive.All[i];
            if (hive == null || string.IsNullOrEmpty(hive.Guid) || hive.IsFlooded || !hive.HasRoom) continue;

            seen++;
            if (Random.Range(0, seen) == 0) chosen = hive;
        }

        return chosen;
    }


    private bool InDayWindow(WorldStateManager world)
    {
        if (world == null || world.DayDuration <= 0.0f) return true;
        if (world.CycleTime >= world.DayDuration) return false;

        float u = world.CycleTime / world.DayDuration;
        return u >= dayWindow.x && u <= dayWindow.y;
    }
}
