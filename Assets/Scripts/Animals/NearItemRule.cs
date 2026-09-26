using System.Collections.Generic;
using UnityEngine;


// Spawns its animal on a placed item that has another item within a distance of it. Either can be a
// tile item (Water) or an entity item, and either can be the one just placed. Plain world distance,
// so something on the level above counts if it is close enough; about 1 is orthogonally next to,
// 1.5 takes in the diagonals.
//
// The two items may be the same (water next to water); an instance never counts as its own neighbour.
[CreateAssetMenu(
    fileName = "NearItemRule",
    menuName = "Animals/Spawn Rules/Near Item"
)]
public class NearItemRule : AnimalSpawnRule
{
    // anything closer than this is the same thing, not a neighbour
    private const float SAME_PLACE = 0.01f;


    // the animal spawns on this one
    [SerializeField] private GameItem placed;
    [SerializeField] private GameItem near;
    [SerializeField] private float radius = 1.5f;

    // The terrain does not remember which tiles were placed, so catching up on a load or an unlock
    // counts the level's own tiles too: with water near water, any lake would spawn the animal at
    // once. Off, the rule only reacts to placements, as it always has
    [SerializeField] private bool catchUpFromWorld = false;


    [System.NonSerialized] private List<Vector3> placedPositions;
    [System.NonSerialized] private List<Vector3> nearPositions;


    public override void OnLoaded(WorldContext context)
    {
        if (catchUpFromWorld) base.OnLoaded(context);
    }


    public override void OnPuzzleUnlocked(WorldContext context)
    {
        if (catchUpFromWorld) base.OnPuzzleUnlocked(context);
    }


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (context == null || item == null) return;
        if (placed == null || near == null) return;
        if (item != placed && item != near) return;

        if (!context.CanSpawn(this)) return;

        TerrainManager terrain = context.Terrain;
        if (terrain == null) return;

        Vector3 point = terrain.CellToWorld(cell);

        // the new one completes a pair: spawn on it if it is the placed kind, else on its partner
        if (item == placed && HasOtherWithin(context, point, near))
        {
            context.Spawn(this, point);
            return;
        }

        if (item == near && TryFindOtherWithin(context, point, placed, out Vector3 partner))
        {
            context.Spawn(this, partner);
        }
    }


    // any placed one with a near one beside it, for catching up on a load or an unlock. Each list is
    // collected once, since collecting a tile item scans the whole map
    protected override bool TryFindSpawn(WorldContext context, out Vector3 position)
    {
        position = default;
        if (placed == null || near == null) return false;

        if (placedPositions == null) placedPositions = new List<Vector3>();
        if (nearPositions == null) nearPositions = new List<Vector3>();

        context.CollectPositionsOf(placed, placedPositions);
        if (placedPositions.Count == 0) return false;

        context.CollectPositionsOf(near, nearPositions);

        for (int i = 0; i < placedPositions.Count; i++)
        {
            for (int j = 0; j < nearPositions.Count; j++)
            {
                if (!IsNeighbour(placedPositions[i], nearPositions[j])) continue;

                position = placedPositions[i];
                return true;
            }
        }

        return false;
    }


    // MARK: internals


    private bool HasOtherWithin(WorldContext context, Vector3 point, GameItem item)
    {
        return TryFindOtherWithin(context, point, item, out Vector3 _);
    }


    private bool TryFindOtherWithin(WorldContext context, Vector3 point, GameItem item, out Vector3 found)
    {
        found = default;

        if (nearPositions == null) nearPositions = new List<Vector3>();

        context.CollectPositionsOf(item, nearPositions);

        for (int i = 0; i < nearPositions.Count; i++)
        {
            if (!IsNeighbour(point, nearPositions[i])) continue;

            found = nearPositions[i];
            return true;
        }

        return false;
    }


    private bool IsNeighbour(Vector3 a, Vector3 b)
    {
        float distance = Vector3.Distance(a, b);
        return distance >= SAME_PLACE && distance <= radius;
    }
}
