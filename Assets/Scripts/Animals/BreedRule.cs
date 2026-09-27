using System.Collections.Generic;
using UnityEngine;


// Every intervalDays, each live animal of this kind has one baby beside it, up to its mob cap. The
// interval is in days rather than seconds, so changing how long a day or a night lasts keeps it
// meaning the same thing.
//
// The timing is a per-animal stamp, AnimalSave.bredDay/bredCycleTime: the clock reading of its last
// birth, its own when it has never bred. 0 is a save from before stamping, stamped with the clock the
// first time this rule looks at it, so an old save has no overdue queue and bursts nothing. The stamp
// is world state, so a checkpoint reset rolls it back with the animal.
//
// The baby goes on the top surface in a neighbouring column, on ground the parent's own controller
// would walk on (AnimalController.SpawnWeightOn). Nowhere valid, it waits and tries again at the next
// moment. Nothing here credits a puzzle: a bred animal is population, not progress (CreditsCompletion).
[CreateAssetMenu(
    fileName = "BreedRule",
    menuName = "Animals/Spawn Rules/Breed"
)]
public class BreedRule : AnimalSpawnRule
{
    private static readonly Vector2Int[] NEIGHBOUR_OFFSETS =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, -1),
    };


    [Tooltip("days between one animal's births, in clock moments of a quarter day each: 0.25 = the " +
        "next sunrise/noon/sunset/midnight, 1 = the same moment tomorrow")]
    [SerializeField] [Min(0.25f)] private float intervalDays = 2.0f;


    // The live animals being walked. Filled through CollectAnimals, which clears it, so a scene reload
    // between two checks cannot leave a stale controller behind
    private readonly List<AnimalController> breeders = new List<AnimalController>();

    // the eight neighbours' cells and weights, refilled per parent
    private readonly Vector3Int[] cells = new Vector3Int[NEIGHBOUR_OFFSETS.Length];
    private readonly float[] weights = new float[NEIGHBOUR_OFFSETS.Length];


    // a bred animal is a background population: it must never tick off a puzzle
    public override bool CreditsCompletion { get { return false; } }


    public override void OnLoaded(WorldContext context)
    {
        BreedDue(context);
    }


    // every moment, so a parent that found nowhere to put one, or had no stamp, is asked again a
    // quarter day later
    public override void OnSunrise(WorldContext context) { BreedDue(context); }


    public override void OnNoon(WorldContext context) { BreedDue(context); }


    public override void OnSunset(WorldContext context) { BreedDue(context); }


    public override void OnMidnight(WorldContext context) { BreedDue(context); }


    // The base's catch-up hooks would spawn a parentless animal, so they are overridden above; this
    // stays truthful for the contract's sake: the first parent with somewhere to put one
    protected override bool TryFindSpawn(WorldContext context, out Vector3 position)
    {
        position = default;

        if (context == null) return false;

        context.CollectAnimals(Animal, breeders);

        for (int i = 0; i < breeders.Count; i++)
        {
            if (breeders[i] == null) continue;
            if (TryFindBirthCell(context, breeders[i], out position)) return true;
        }

        return false;
    }


    // intervalDays as a count of clock moments, never less than the next one, the same conversion
    // GameItem.GrowthMoments uses
    private int IntervalMoments
    {
        get { return Mathf.Max(1, Mathf.RoundToInt(intervalDays * WorldStateManager.MOMENTS_PER_DAY)); }
    }


    // MARK: breeding


    private void BreedDue(WorldContext context)
    {
        if (context == null) return;

        WorldStateManager states = context.States;
        if (states == null) return;

        context.CollectAnimals(Animal, breeders);
        if (breeders.Count == 0) return;

        int interval = IntervalMoments;
        int now = states.CurrentMomentIndex;

        for (int i = 0; i < breeders.Count; i++)
        {
            // no room for another one anywhere, so the rest of this check waits. CanSpawn is the same
            // gate Spawn applies, so it covers the registry and the unlock too
            if (!context.CanSpawn(this)) return;

            AnimalController parent = breeders[i];
            if (parent == null) continue;

            // a save from before stamping, or a spawn from a scene with no clock: it starts its clock
            // now rather than being read as two days overdue
            if (parent.BredDay <= 0)
            {
                parent.SetBredStamp(states.Day, states.CycleTime);
                continue;
            }

            int due = states.MomentIndex(parent.BredDay, parent.BredCycleTime) + interval;
            if (due > now) continue;

            // boxed in, or its ground has gone: it keeps its place in the queue and is asked again
            if (!TryFindBirthCell(context, parent, out Vector3 position)) continue;

            // stamped at the birth itself, so "again in two days" is measured from where the last one
            // really landed
            if (context.Spawn(this, position)) parent.SetBredStamp(states.Day, states.CycleTime);
        }
    }


    // A neighbour of the parent's column whose top is on the parent's level and which the parent's own
    // controller allows, picked with its own weights. False when there is nowhere to put one
    private bool TryFindBirthCell(WorldContext context, AnimalController parent, out Vector3 position)
    {
        position = default;

        TerrainManager terrain = context.Terrain;
        if (terrain == null) return false;

        Vector3 feet = parent.transform.position;

        int x = Mathf.FloorToInt(feet.x);
        int z = Mathf.FloorToInt(feet.z);

        int level = terrain.GetTopLevel(x, z);
        if (level < 0) return false;

        int count = 0;
        float total = 0.0f;

        for (int i = 0; i < NEIGHBOUR_OFFSETS.Length; i++)
        {
            Vector2Int offset = NEIGHBOUR_OFFSETS[i];

            int nx = x + offset.x;
            int nz = z + offset.y;

            // crawling never changes level, so a birth doesn't either
            if (terrain.GetTopLevel(nx, nz) != level) continue;

            Vector3Int cell = new Vector3Int(nx, level, nz);

            float weight = parent.SpawnWeightOn(terrain.GetTile(cell));
            if (weight <= 0.0f) continue;

            cells[count] = cell;
            weights[count] = weight;
            total += weight;
            count++;
        }

        if (count == 0) return false;

        float roll = Random.value * total;
        int chosen = count - 1;

        for (int i = 0; i < count; i++)
        {
            roll -= weights[i];
            if (roll > 0.0f) continue;

            chosen = i;
            break;
        }

        position = terrain.CellToWorld(cells[chosen]);
        return true;
    }
}
