using System.Collections.Generic;
using UnityEngine;


// Grows placed things into other things. A GameItem with a growsInto is replaced by it, in the same
// cell, once growthDays have passed since the entity became its current stage. The replacement goes
// through PlacementController.Replace, so a growth is a placement like any other: one save, ItemPlaced,
// the world's listeners, discovery.
//
// Every entity is pinned to the clock reading it became its stage at (EntitySave.stageDay and
// stageCycleTime), and its age is the number of clock moments crossed since, a quarter day each.
// Progress is derived from the pin and the clock, never counted, so a checkpoint reset (which rolls
// both back) and a reload come right on their own.
//
// Checked at every clock moment, through WorldStateManager.MomentReached: once per moment, with the
// clock already standing on it, and before Sunrise/Noon/Sunset/Midnight go out, so their listeners see
// the grown world. A growth that can't be placed right now just waits and is re-checked every moment.
// Once the loaded world is ready, whatever is overdue grows silently, chaining through stages.
public class GrowthManager : MonoBehaviour
{
    private static GrowthManager instance;
    public static GrowthManager Instance { get { return instance; } }


    // a guard against a growsInto cycle, which the registry's validation should already have caught
    private const int MAX_CATCH_UP_PASSES = 32;


    [SerializeField] private EntityManager entityManager;
    [SerializeField] private PlacementController placementController;
    [SerializeField] private WorldStateManager worldStates;

    // growth works without it, but a puzzle waiting on growth would never be judged again
    [SerializeField] private PuzzleManager puzzleManager;

    [Header("Several growths on one moment")]
    [Tooltip("each growth after the first pops after a random delay up to this, so they come in as a wave")]
    [SerializeField] [Min(0.0f)] private float staggerMax = 0.2f;
    [Tooltip("the rest grow quietly")]
    [SerializeField] [Min(0)] private int soundsPerMoment = 3;


    private WorldEvents worldEvents;
    private bool caughtUp;

    private readonly List<EntityManager.LiveEntity> scratch = new List<EntityManager.LiveEntity>();


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one GrowthManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }


    // Start rather than Awake: the managers are only guaranteed once every Awake has run
    private void Start()
    {
        if (entityManager == null) entityManager = EntityManager.Instance;
        if (placementController == null) placementController = PlacementController.Instance;
        if (worldStates == null) worldStates = WorldStateManager.Instance;
        if (puzzleManager == null) puzzleManager = PuzzleManager.Instance;

        if (entityManager == null) Debug.LogError("GrowthManager: no EntityManager, so nothing placed can ever grow", this);
        if (placementController == null) Debug.LogError("GrowthManager: no PlacementController, so nothing placed can ever grow", this);
        if (worldStates == null) Debug.LogError("GrowthManager: no WorldStateManager, so there is no clock to grow by", this);

        if (puzzleManager == null) Debug.LogError("GrowthManager: no PuzzleManager, so a puzzle waiting on growth is never judged again once it grows", this);

        if (entityManager == null || placementController == null || worldStates == null) return;

        worldStates.MomentReached += OnMomentReached;

        // sticky, so it answers at once if the world is already ready
        worldEvents = WorldEvents.Ensure();
        worldEvents.Ready += OnWorldReady;
    }


    private void OnDestroy()
    {
        if (worldStates != null) worldStates.MomentReached -= OnMomentReached;
        if (worldEvents != null) worldEvents.Ready -= OnWorldReady;

        if (instance == this) instance = null;
    }


    // MARK: triggers


    // A loaded world catches up silently: the player never saw it happen. Several passes, because a
    // stage grown here can itself be overdue already
    private void OnWorldReady()
    {
        for (int pass = 0; pass < MAX_CATCH_UP_PASSES; pass++)
        {
            if (GrowDue(PlacementController.PlaceFeel.Silent) == 0) break;

            if (pass == MAX_CATCH_UP_PASSES - 1)
            {
                Debug.LogError("GrowthManager: still growing after " + MAX_CATCH_UP_PASSES + " catch-up passes, " +
                    "so some growsInto chain probably loops. Run Validate on the GameItemRegistry", this);
            }
        }

        caughtUp = true;

        // a puzzle held back while its ingredients grew is judged against the caught-up world
        JudgePuzzles();
    }


    // before the catch-up, a moment could only grow things out of order, and the catch-up covers it
    private void OnMomentReached(WorldStateManager.DayPart part)
    {
        if (!caughtUp) return;

        if (GrowDue(PlacementController.PlaceFeel.Growth) > 0) JudgePuzzles();
    }


    // Soft locks skip a puzzle while its ingredients are still growing, so once something grows it is
    // judged again: a grown item that doesn't satisfy it (and won't grow further) fails it properly
    private void JudgePuzzles()
    {
        if (puzzleManager != null) puzzleManager.ScheduleSoftLockCheck();
    }


    // MARK: growth


    // Replaces every entity whose growth is due. Returns how many grew
    private int GrowDue(PlacementController.PlaceFeel feel)
    {
        int now = worldStates.CurrentMomentIndex;
        bool catchingUp = feel == PlacementController.PlaceFeel.Silent;

        // a snapshot, because each growth changes the live list
        entityManager.CollectLive(scratch);

        int grown = 0;

        for (int i = 0; i < scratch.Count; i++)
        {
            EntityManager.LiveEntity entity = scratch[i];

            if (entity.item == null || entity.entityId == null) continue;

            GameItem into = entity.item.GrowsInto;
            if (into == null) continue;

            int due = worldStates.MomentIndex(entity.stageDay, entity.stageCycleTime) + entity.item.GrowthMoments;
            if (due > now) continue;

            int stageDay = worldStates.Day;
            float stageCycleTime = worldStates.CycleTime;

            // caught up, it's stamped at the moment it should have grown, so a chain lands where it would
            // have. Live, it grew now: one that waited on its ground is pinned to when it really grew
            if (catchingUp && due < now) worldStates.MomentToTime(due, out stageDay, out stageCycleTime);

            float delay = !catchingUp && grown > 0 ? Random.Range(0.0f, staggerMax) : 0.0f;
            bool withSound = grown < soundsPerMoment;

            // false when it can't go there right now, e.g. the ground no longer suits it: it waits
            if (placementController.Replace(entity.entityId, into, stageDay, stageCycleTime, feel, delay, withSound)) grown++;
        }

        scratch.Clear();

        return grown;
    }


    // MARK: debug


    // play mode only: grows everything placed right now, whatever its age
    [ContextMenu("Grow Everything Now")]
    private void DebugGrowEverything()
    {
        if (!Application.isPlaying || entityManager == null || placementController == null || worldStates == null) return;

        entityManager.CollectLive(scratch);

        int grown = 0;

        for (int i = 0; i < scratch.Count; i++)
        {
            EntityManager.LiveEntity entity = scratch[i];
            if (entity.item == null || entity.item.GrowsInto == null) continue;

            float delay = grown > 0 ? Random.Range(0.0f, staggerMax) : 0.0f;

            if (placementController.Replace(entity.entityId, entity.item.GrowsInto, worldStates.Day, worldStates.CycleTime,
                PlacementController.PlaceFeel.Growth, delay, grown < soundsPerMoment)) grown++;
        }

        scratch.Clear();

        if (grown > 0) JudgePuzzles();
    }
}
