using UnityEngine;


// The risen sea floods the seafloor. When WorldCondition.SeaRisen starts, every terrain level whose
// surface sits below the sea's final height is remapped to one tile, a whole level at a time.
//
// The flood follows the water up rather than running on a timer: each level waits until the sheet
// has actually reached that level's height, then waits remapDelaySeconds more so the water is
// visibly over it, then the whole level changes at once. Nothing here animates; the level swaps in
// a single frame, which is why TerrainManager.RemapLevel batches it.
//
// Reads the sea's live surface in LateUpdate instead of computing crossing times from the rise
// curve, so it cannot drift from the SmoothStep and the 1/32 pixel stepping SeaLevel applies, and
// riseDuration == 0 needs no special case.
//
// It survives a load and a checkpoint reset without any saved progress flag: a load replays the
// sticky SeaRisen during subscribe and floods every submerged level at once, and a level that is
// already fully flood-tile makes RemapLevel do nothing, so re-hearing it is free.
public class SeaFloorRemapper : MonoBehaviour
{
    // which tile the seafloor becomes. Set by the director, and the tile has to be registered in the
    // scene's TerrainTileRegistry or nothing can be written to a save
    [SerializeField] private TerrainTile seafloorTile;


    // "slightly after" the sheet passes a level, so the water is over it before the ground changes
    [SerializeField] private float remapDelaySeconds = 0.02f;


    private WorldStateManager worldStates;
    private SeaLevel seaLevel;
    private TerrainManager terrain;


    // true only while the sticky ConditionChanged is replaying the saved states at subscribe time
    private bool replaying;

    private bool rising;

    // true once the flood has finished for this session. A no-op remap already makes a second
    // hearing free, so this only saves re-scanning the levels again
    private bool flooded;

    // levels still to flood, and how many are left
    private LevelRise[] levels;
    private int remaining;


    private struct LevelRise
    {
        public int level;
        public float height;

        // Time.time the sheet was first seen at or above this level. A level that was already under
        // the water when tracking began is treated as crossed at rise start, so it waits only the
        // delay rather than being popped in late by an arbitrary grace period
        public float crossedAt;

        public bool done;
    }


    private void Start()
    {
        if (!Application.isPlaying) return;

        worldStates = WorldStateManager.Instance;
        seaLevel = SeaLevel.Instance;
        terrain = TerrainManager.Instance;

        bool ok = true;

        if (worldStates == null)
        {
            Debug.LogError("SeaFloorRemapper: no WorldStateManager, so SeaRisen is never heard and the seafloor will never flood", this);
            ok = false;
        }

        if (seaLevel == null)
        {
            Debug.LogError("SeaFloorRemapper: no SeaLevel, so there is no water to flood under", this);
            ok = false;
        }

        if (terrain == null)
        {
            Debug.LogError("SeaFloorRemapper: no TerrainManager, so there is no terrain to remap", this);
            ok = false;
        }

        if (seafloorTile == null)
        {
            Debug.LogError("SeaFloorRemapper: no seafloorTile assigned, so the flood has nothing to become", this);
            ok = false;
        }

        if (!ok) return;

        // sticky: a save with the sea already risen is replayed straight away, inside this +=
        replaying = true;
        worldStates.ConditionChanged += OnConditionChanged;
        replaying = false;
    }


    private void OnDestroy()
    {
        if (worldStates != null) worldStates.ConditionChanged -= OnConditionChanged;
    }


    private void OnConditionChanged(WorldCondition condition, ConditionPhase phase)
    {
        // an ended SeaRisen still counts: the sea never drains away
        if (condition != WorldCondition.SeaRisen || phase == ConditionPhase.NotStarted) return;
        if (rising || flooded) return;

        // a load or a checkpoint reset has snapped the sheet to its final height already, so a delay
        // would only make the ground pop in late after the water is already there
        if (replaying)
        {
            FloodEverySubmergedLevel();
            return;
        }

        BeginTracking();
    }


    // MARK: live rise


    private void BeginTracking()
    {
        int count = terrain.LevelCount;
        int submerged = 0;

        for (int level = 0; level < count; level++)
        {
            if (terrain.GetLevelHeight(level) < seaLevel.Y) submerged++;
        }

        // nothing to flood, so nothing will ever need tracking: no rise to follow
        if (submerged == 0)
        {
            flooded = true;
            return;
        }

        // the level list is fixed for the rise, so it is taken once here
        float now = Time.time;
        float surface = seaLevel.SurfaceY;

        levels = new LevelRise[submerged];
        remaining = submerged;

        int next = 0;

        for (int level = 0; level < count; level++)
        {
            float height = terrain.GetLevelHeight(level);

            if (height >= seaLevel.Y) continue;

            levels[next] = new LevelRise
            {
                level = level,
                height = height,

                // a level the water is already over - what a late subscription sees - has crossed,
                // so it is due after the ordinary delay rather than waiting to be reached
                crossedAt = surface >= height ? now : float.NegativeInfinity,
                done = false,
            };

            next++;
        }

        rising = true;
    }


    private void LateUpdate()
    {
        if (!rising) return;

        // the sea is moved in Update, so by here the sheet is where it will be drawn this frame
        float surface = seaLevel.SurfaceY;

        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i].done) continue;

            if (surface < levels[i].height)
            {
                // not reached yet: forget a crossing so the delay starts from the sighting below
                levels[i].crossedAt = float.NegativeInfinity;
                continue;
            }

            // first sighting: the delay starts from here rather than from a time computed off the
            // rise curve, so it cannot drift from the SmoothStep or the pixel stepping
            if (float.IsNegativeInfinity(levels[i].crossedAt))
            {
                levels[i].crossedAt = Time.time;
                continue;
            }

            if (Time.time < levels[i].crossedAt + remapDelaySeconds) continue;

            FloodLevel(i);
        }

        if (remaining <= 0)
        {
            rising = false;
            flooded = true;
        }
    }


    // MARK: flooding


    private void FloodLevel(int index)
    {
        levels[index].done = true;
        remaining--;

        int changed = terrain.RemapLevel(levels[index].level, seafloorTile);

        // already all seafloor: nothing changed, so nothing to save and nothing to say
        if (changed == 0) return;

        // the terrain only reaches the file on the next save, so persist each level as it floods: a
        // quit halfway up has to leave the levels that did flood flooded
        SaveDataManager saveManager = SaveDataManager.Instance;

        if (saveManager != null) saveManager.Save();
        else Debug.LogWarning($"SeaFloorRemapper: level {levels[index].level} flooded but there is no " +
            $"SaveDataManager, so it will not be saved", this);

        Debug.Log($"SeaFloorRemapper: level {levels[index].level} at {levels[index].height} flooded, " +
            $"{changed} cells remapped");
    }


    // The replay path: the sheet is already at its final height, so every level under it changes at
    // once with no delay
    private void FloodEverySubmergedLevel()
    {
        int count = terrain.LevelCount;

        for (int level = 0; level < count; level++)
        {
            if (terrain.GetLevelHeight(level) >= seaLevel.Y) continue;

            int changed = terrain.RemapLevel(level, seafloorTile);

            // nothing to do: this level is already all seafloor, so no save either
            if (changed == 0) continue;

            SaveDataManager saveManager = SaveDataManager.Instance;

            if (saveManager != null) saveManager.Save();
            else Debug.LogWarning($"SeaFloorRemapper: level {level} flooded but there is no " +
                $"SaveDataManager, so it will not be saved", this);

            Debug.Log($"SeaFloorRemapper: level {level} flooded on load, {changed} cells remapped");
        }

        flooded = true;
    }


    // MARK: debug


    // play mode only: floods every submerged level right now, for testing without waiting for the sea
    [ContextMenu("Force Flood")]
    private void ForceFlood()
    {
        if (!Application.isPlaying) return;
        if (terrain == null || seaLevel == null || seafloorTile == null) return;

        FloodEverySubmergedLevel();
    }
}
