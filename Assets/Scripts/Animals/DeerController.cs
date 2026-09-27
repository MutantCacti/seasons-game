using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;


public enum DeerState
{
    Stand,
    Wander,
    Graze,
    Rest,
    Alert,
    Flee,
}


public enum DeerFacing
{
    Down,
    Up,
    Left,
    Right,
}


// A deer: wanders the top surface on short grid paths, grazes beside ferns, rests under trees, and
// bolts from the animals it fears. It is crepuscular: one activity value, peaking at sunrise and
// sunset, makes it quicker, busier and louder around them.
//
// Facing is picked against the camera's flat right and forward, not the world axes: the camera is
// yawed, so world cardinals are screen diagonals, and the sprites are drawn for screen directions.
// It never stands still on the up or down sprite (they look odd to stare at): the last moment of a
// path turns it side-on, and every still state asserts a side sprite on the way in.
//
// Paths are a BFS over a window around it, so it routes round water and cliffs. Level ground is
// walked; a step one level up or down is hopped, since a sprite sliding up a cliff face reads wrong.
//
// Grazing never touches the fern's state: it rustles the fern's sprite child and munches, nothing
// more. Placed items drive puzzles, so eating one could strand the player.
//
// Size, tint and personality come from a hash of its entity guid, so a deer looks and acts the same
// every load. Read in Start: the guid is minted on first read, and a loaded deer only has its saved
// one assigned after Awake.
//
// Nothing here is saved. AnimalManager writes the position through every LateUpdate, and a loaded
// deer snaps back onto the surface, calm, and carries on.
[RequireComponent(typeof(SpriteRenderer))]
public class DeerController : AnimalController
{
    private static readonly Vector2Int[] NEIGHBOURS =
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

    // every deer, for company, herding and answering a bray. only live deer are in it
    private static readonly List<DeerController> herd = new List<DeerController>();

    // salts for the per-trait hashes; new traits get new salts, so adding one never moves the others
    private const uint SALT_SIZE = 0x9E3779B9u;
    private const uint SALT_TEMPERATURE = 0x85EBCA6Bu;
    private const uint SALT_BRIGHTNESS = 0xC2B2AE35u;
    private const uint SALT_BOLDNESS = 0x27D4EB2Fu;
    private const uint SALT_APPETITE = 0x165667B1u;
    private const uint SALT_RESTLESSNESS = 0xD3A2646Cu;
    private const uint SALT_GAIT = 0xFD7046C5u;
    private const uint SALT_PHASE = 0xB55A4F09u;
    private const uint SALT_HANDEDNESS = 0x7ED55D16u;

    private const float ARRIVED_SQR = 0.000001f;


    [Header("Look")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    // six frames each: walking towards the camera, away from it, and to the screen's left and right
    [SerializeField] private Sprite[] downFrames = new Sprite[0];
    [SerializeField] private Sprite[] upFrames = new Sprite[0];
    [SerializeField] private Sprite[] leftFrames = new Sprite[0];
    [SerializeField] private Sprite[] rightFrames = new Sprite[0];
    [SerializeField] [Min(0)] private int standFrame = 0;                 // the side frame it stands still on
    [SerializeField] private float framesPerUnit = 12.0f;                 // walk frames per world unit, so the feet never skate
    [SerializeField] [Min(1.0f)] private float sideBias = 1.1f;           // side sprites read better, so they win near-diagonals
    [SerializeField] private float facingHysteresis = 0.15f;              // a new facing must beat the old by this much
    [SerializeField] private float turnDistance = 0.12f;                  // world units before the end of a path it turns side-on


    [Header("Variety")]
    [SerializeField] private Vector2 sizeRange = new Vector2(0.85f, 1.15f);
    [SerializeField] private Color warmTint = new Color(1.0f, 0.95f, 0.86f);
    [SerializeField] private Color coolTint = new Color(0.9f, 0.95f, 1.0f);
    [SerializeField] private Vector2 brightnessRange = new Vector2(0.92f, 1.0f);
    [SerializeField] [Range(0.0f, 0.5f)] private float gaitVariation = 0.08f;


    [Header("Ground")]
    [SerializeField] private TerrainTile[] forbiddenTiles = new TerrainTile[0];
    [SerializeField] [Min(1)] private int pathRadius = 6;                 // cells each way it can plan a path over
    [SerializeField] private float cellMargin = 0.2f;                     // path points stay this far inside their cell
    [SerializeField] private float pathJitter = 0.15f;                    // how far off-centre the corners of a path wander


    [Header("Movement")]
    [SerializeField] private float walkSpeed = 0.45f;                     // world units a second, calm
    [SerializeField] private float rushSpeed = 1.1f;                      // at the height of dawn and dusk
    [SerializeField] private float fleeSpeed = 2.4f;
    [SerializeField] private Vector2Int wanderSteps = new Vector2Int(1, 4);
    [SerializeField] private float hopDuration = 0.32f;
    [SerializeField] private float hopArc = 0.25f;                        // world units above the straight line, plus half the climb


    [Header("Crepuscular")]
    // how far either side of sunrise and sunset the rush reaches, as a share of the whole day and night
    [SerializeField] [Range(0.01f, 0.5f)] private float activityWidth = 0.1f;
    [SerializeField] [Range(0.0f, 1.0f)] private float activityFloor = 0.15f;
    [SerializeField] [Min(1.0f)] private float loudBoost = 1.6f;          // volume at full activity


    [Header("Choosing")]
    [SerializeField] private Vector2 standDuration = new Vector2(1.5f, 4.0f);
    [SerializeField] private float standWeight = 1.0f;
    [SerializeField] private float standHappyWeight = 1.5f;
    [SerializeField] private float standActiveWeight = 0.8f;
    [SerializeField] private float wanderWeight = 0.8f;
    [SerializeField] private float wanderActiveWeight = 2.0f;
    [SerializeField] private float grazeWeight = 2.0f;
    [SerializeField] private float restWeight = 1.5f;
    [SerializeField] private float sightRadius = 6.0f;                    // world units it looks for ferns and trees in


    [Header("Grazing")]
    [SerializeField] private GameItem[] foodItems = new GameItem[0];
    [SerializeField] private Vector2 grazeDuration = new Vector2(3.0f, 6.0f);
    [SerializeField] private Vector2 munchInterval = new Vector2(0.8f, 1.6f);
    [SerializeField] private float rustleAngle = 8.0f;                    // degrees
    [SerializeField] private float rustleDuration = 0.4f;
    // how far from the fern's middle it stands to eat, in world units at size 1: close enough that
    // its muzzle is in the leaves. a little towards the camera too, so it draws in front of the fern
    [SerializeField] private float grazeDistance = 0.4f;
    [SerializeField] private float grazeInFront = 0.08f;


    [Header("Resting")]
    [SerializeField] private GameItem[] shelterItems = new GameItem[0];
    [SerializeField] private Vector2 restDuration = new Vector2(4.0f, 8.0f);


    [Header("Fleeing")]
    [SerializeField] private Animal[] fleeFrom = new Animal[0];
    [SerializeField] private float detectRadius = 4.0f;
    [SerializeField] private float detectInterval = 0.25f;
    [SerializeField] private Vector2 alertDuration = new Vector2(0.25f, 0.5f);
    [SerializeField] [Min(1)] private int fleeSteps = 6;
    [SerializeField] private Vector2 spookedDuration = new Vector2(6.0f, 10.0f);
    [SerializeField] [Min(1)] private int stotEvery = 3;                  // footfalls between bounces while fleeing
    [SerializeField] private float stotHeight = 0.08f;
    [SerializeField] private float stotDuration = 0.22f;


    [Header("Herd")]
    [SerializeField] private float herdRadius = 5.0f;
    [SerializeField] private float herdWeight = 2.0f;                     // extra pull on wander cells near another deer
    [SerializeField] private float answerRadius = 8.0f;
    [SerializeField] [Range(0.0f, 1.0f)] private float answerChance = 0.6f;
    [SerializeField] private Vector2 answerDelay = new Vector2(0.5f, 1.5f);


    [Header("Reactions")]
    [SerializeField] private float focusFreeze = 0.6f;


    [Header("Happiness")]
    [SerializeField] private float happinessRadius = 3.0f;
    [SerializeField] [Range(0.0f, 1.0f)] private float baseHappiness = 0.45f;
    [SerializeField] private float fernBonus = 0.25f;
    [SerializeField] [Min(1)] private int fernsForFull = 3;
    [SerializeField] private float shelterBonus = 0.15f;
    [SerializeField] private float companyBonus = 0.15f;
    [SerializeField] private float spookedPenalty = 0.4f;
    [SerializeField] private string noFoodTip = "Nothing green to nibble nearby";
    [SerializeField] private string noShelterTip = "No trees to shelter under";
    [SerializeField] private string lonelyTip = "Lonely without the herd";
    [SerializeField] private string spookedTip = "Still trembling from a fright";
    [SerializeField] private string[] happyLines =
    {
        "Nose deep in ferns",
        "Ears up, all is quiet",
        "Dozing in the dappled shade",
        "Tail flicking contentedly",
        "Hooves light, heart lighter",
        "Keeping one ear on the treeline",
        "Dusk smells delicious",
    };


    [Header("Sound")]
    [SerializeField] private SoundObject stepSound;
    [SerializeField] private SoundObject braySound;
    [SerializeField] private SoundObject snortSound;
    [SerializeField] private SoundObject munchSound;
    [SerializeField] private int[] footfallFrames = { 1, 4 };
    [SerializeField] private float landVolume = 1.3f;
    [SerializeField] [Min(1)] private int busyHerdSize = 3;               // above this many deer, only every other footfall sounds
    [SerializeField] private Vector2 brayInterval = new Vector2(8.0f, 20.0f);
    [SerializeField] [Range(0.05f, 1.0f)] private float brayActiveScale = 0.35f;   // the interval at full activity


    [Header("Spreading")]
    // while it grazes or rests, it turns ground near its feet from spreadFrom into spreadTo, as long as
    // that ground already touches one of spreadNextTo (see TerrainSpread). nearest first, so it greens
    // out from it
    [SerializeField] private TerrainTile spreadFrom;    // dirt
    [SerializeField] private TerrainTile spreadTo;      // grass
    [Tooltip("the ground a spread creeps out from. empty means Spread To")]
    [SerializeField] private TerrainTile[] spreadNextTo = new TerrainTile[0];
    [Tooltip("cells converted per whole day and night, while it has somewhere to graze or rest")]
    [SerializeField] [Min(0.0f)] private float tilesPerDay = 2.0f;
    [SerializeField] [Min(1.0f)] private float creditCap = 2.0f;
    [SerializeField] [Min(0)] private int spreadRadius = 2;               // cells each way from its feet
    [SerializeField] [Min(0.1f)] private float spreadInterval = 1.0f;     // seconds between tries while it stays
    [SerializeField] private SoundObject spreadSound;


    private TerrainManager terrain;
    private Vector3 cameraRight;
    private Vector3 cameraForward;
    private Transform sun;

    // the lowest vertex of every frame below its pivot, in sprite units, measured once
    private float[][] frameBottoms;
    private Sprite[][] frames;

    // traits, from the guid
    private uint seed;
    private float boldness;
    private float appetite;
    private float restlessness;
    private float gait = 1.0f;
    private float size = 1.0f;
    private float brayPitch = 1.0f;
    private bool leftHanded;

    private Vector3 feet;
    private int level;
    private float activity;

    private DeerState state;
    private float stateTimer;
    private bool lingering;         // arrived and staying: grazing, resting
    private float focusTimer;

    private DeerFacing facing = DeerFacing.Right;
    private int frame;
    private float framePhase;
    private int footfalls;
    private float faceHint;         // screen x of what it will face at the end of this path, 0 for none

    // path
    private Vector3[] pathPoints;
    private int[] pathLevels;
    private int pathCount;
    private int pathIndex;

    private bool hopping;
    private Vector3 hopFrom;
    private Vector3 hopTo;
    private float hopTime;

    // bfs over the window around the start, preallocated so planning never allocates
    private int bfsWidth;
    private int bfsOriginX;
    private int bfsOriginZ;
    private int bfsStampNow;
    private int[] bfsStamp;
    private int[] bfsParent;
    private int[] bfsSteps;
    private int[] bfsLevel;
    private int[] bfsQueue;
    private int bfsReachedCount;
    private float seaY = float.NegativeInfinity;

    // grazing
    private EntityId grazeFern;
    private float munchTimer;

    // fear
    private float detectTimer;
    private float spookedTimer;
    private Vector3 threatAway;     // the flat direction away from what it saw
    private Vector3 threatPoint;

    private float brayTimer;
    private float answerTimer;

    private int happyLineIndex = -1;

    // spreading: credit earned towards the next conversion, and the countdown to the next try
    private float spreadCredit;
    private float spreadTimer;

    private readonly List<EntityId> scratchIds = new List<EntityId>();


    // MARK: read-only state


    public DeerState State { get { return state; } }


    public bool IsSpooked { get { return spookedTimer > 0.0f; } }


    // MARK: lifecycle


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        herd.Clear();
    }


    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) Debug.LogError("DeerController: no SpriteRenderer, so the deer cannot walk or turn", this);

        frames = new[] { downFrames, upFrames, leftFrames, rightFrames };
        frameBottoms = new float[frames.Length][];

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] == null || frames[i].Length == 0)
            {
                Debug.LogError($"DeerController: no {(DeerFacing)i} frames, so the deer shows whatever sprite it had when facing that way", this);
                frames[i] = new Sprite[0];
            }

            frameBottoms[i] = new float[frames[i].Length];

            for (int j = 0; j < frames[i].Length; j++) frameBottoms[i][j] = MeasureBottom(frames[i][j]);
        }

        bfsWidth = pathRadius * 2 + 1;
        int cells = bfsWidth * bfsWidth;

        bfsStamp = new int[cells];
        bfsParent = new int[cells];
        bfsSteps = new int[cells];
        bfsLevel = new int[cells];
        bfsQueue = new int[cells];

        pathPoints = new Vector3[cells];
        pathLevels = new int[cells];
    }


    private void OnEnable()
    {
        if (!herd.Contains(this)) herd.Add(this);
    }


    private void OnDisable()
    {
        herd.Remove(this);
    }


    private void Start()
    {
        terrain = TerrainManager.Instance;

        if (terrain == null)
        {
            Debug.LogError("DeerController: no TerrainManager, so the deer cannot find the ground and stays still", this);
            enabled = false;
            return;
        }

        if (WorldStateManager.Instance == null)
        {
            Debug.LogWarning("DeerController: no WorldStateManager, so the deer never feels dawn or dusk", this);
        }

        if (spreadFrom == null || spreadTo == null)
        {
            Debug.LogError("DeerController: no spread tiles assigned, so the deer never turns dirt into grass", this);
        }

        FindCameraAxes();
        FindSun();

        // here and not in Awake: a loaded deer's guid is only assigned after Awake, and reading it
        // any earlier would mint a new one and lose the saved identity
        ApplyTraits();

        SnapToSurface();

        SetFacing(leftHanded ? DeerFacing.Left : DeerFacing.Right);
        ShowStill();

        detectTimer = detectInterval * Trait01(SALT_PHASE);
        brayTimer = RandomIn(brayInterval);

        BeginStand(RandomIn(standDuration) * 0.5f);
        ApplyPosition();
    }


    private void Update()
    {
        float dt = Time.deltaTime;   // scaled, so a paused game freezes the deer
        if (dt <= 0.0f) return;

        activity = Activity();

        spreadCredit = TerrainSpread.Accrue(spreadCredit, tilesPerDay, creditCap, dt);

        UpdateFear(dt);
        UpdateVoice(dt);

        if (focusTimer > 0.0f)
        {
            focusTimer -= dt;
        }
        else
        {
            switch (state)
            {
                case DeerState.Stand: UpdateStand(dt); break;
                case DeerState.Wander: UpdateWander(dt); break;
                case DeerState.Graze: UpdateGraze(dt); break;
                case DeerState.Rest: UpdateRest(dt); break;
                case DeerState.Alert: UpdateAlert(dt); break;
                case DeerState.Flee: UpdateFlee(dt); break;
            }
        }

        ApplyPosition();
    }


    // MARK: choosing


    // Rolls the next calm state. Plans from where it stands, so a deer standing on ground that has
    // just turned forbidden (water poured under it) walks off to the nearest good cell first
    private void Decide()
    {
        seaY = SeaSurfaceY();

        RunBfs();

        if (!IsWalkable(Mathf.FloorToInt(feet.x), Mathf.FloorToInt(feet.z), out int _))
        {
            if (TryPlanNearestWalkable())
            {
                state = DeerState.Wander;
                lingering = false;
                return;
            }

            BeginStand(RandomIn(standDuration));
            return;
        }

        float happiness = Happiness;
        bool spooked = IsSpooked;

        int fernGoal = -1;
        Vector3 fernPoint = default;
        EntityId fern = null;
        if (!spooked) fernGoal = FindGrazeGoal(out fern, out fernPoint);

        int shelterGoal = -1;
        Vector3 shelterPoint = default;
        if (!spooked) shelterGoal = FindRestGoal(out shelterPoint);

        float stand = Mathf.Max(0.05f, standWeight + standHappyWeight * happiness - standActiveWeight * activity);
        float wander = Mathf.Max(0.05f, wanderWeight + wanderActiveWeight * activity + restlessness);
        float graze = fernGoal >= 0 ? grazeWeight * Mathf.Lerp(0.5f, 1.5f, appetite) : 0.0f;
        float rest = shelterGoal >= 0 ? restWeight * (1.0f - activity) : 0.0f;

        float roll = Random.value * (stand + wander + graze + rest);

        if (roll < graze)
        {
            BuildPath(fernGoal, fernPoint);
            grazeFern = fern;
            faceHint = ScreenX(fern.transform.position - fernPoint);
            state = DeerState.Graze;
            lingering = false;
            return;
        }

        roll -= graze;

        if (roll < rest)
        {
            BuildPath(shelterGoal, shelterPoint);
            faceHint = 0.0f;
            state = DeerState.Rest;
            lingering = false;
            return;
        }

        roll -= rest;

        if (roll < wander && TryPlanWander())
        {
            faceHint = 0.0f;
            state = DeerState.Wander;
            lingering = false;
            return;
        }

        // happy deer stand longer; busy ones don't
        BeginStand(RandomIn(standDuration) * (1.0f + happiness) * Mathf.Lerp(1.0f, 0.4f, activity) * Mathf.Lerp(1.2f, 0.8f, restlessness));
    }


    // MARK: states


    private void BeginStand(float duration)
    {
        state = DeerState.Stand;
        stateTimer = duration;
        lingering = true;
        pathCount = 0;

        TurnSide(IsDusk() ? SunScreenX() : 0.0f);
        ShowStill();
    }


    private void UpdateStand(float dt)
    {
        stateTimer -= dt;
        if (stateTimer <= 0.0f) Decide();
    }


    private void UpdateWander(float dt)
    {
        if (Travel(WalkSpeed(), dt)) Decide();
    }


    private void UpdateGraze(float dt)
    {
        if (!lingering)
        {
            if (!Travel(WalkSpeed(), dt)) return;

            // the fern went (grew, or was replaced) while it walked over
            if (grazeFern == null)
            {
                Decide();
                return;
            }

            lingering = true;
            stateTimer = RandomIn(grazeDuration);
            munchTimer = RandomIn(munchInterval) * 0.5f;
            spreadTimer = 0.0f;
            TurnSide(ScreenX(grazeFern.transform.position - feet));
            ShowStill();
            return;
        }

        if (grazeFern == null)
        {
            Decide();
            return;
        }

        UpdateSpread(dt);

        munchTimer -= dt;

        if (munchTimer <= 0.0f)
        {
            munchTimer = RandomIn(munchInterval);
            Munch();
        }

        stateTimer -= dt;
        if (stateTimer <= 0.0f) Decide();
    }


    private void UpdateRest(float dt)
    {
        if (!lingering)
        {
            if (!Travel(WalkSpeed(), dt)) return;

            lingering = true;
            stateTimer = RandomIn(restDuration);
            spreadTimer = 0.0f;
            TurnSide(IsDusk() ? SunScreenX() : 0.0f);
            ShowStill();
            return;
        }

        UpdateSpread(dt);

        stateTimer -= dt;
        if (stateTimer <= 0.0f) Decide();
    }


    private void BeginAlert()
    {
        state = DeerState.Alert;
        stateTimer = RandomIn(alertDuration) * Mathf.Lerp(1.3f, 0.7f, boldness);
        lingering = true;
        grazeFern = null;

        LandNow();
        pathCount = 0;

        TurnSide(ScreenX(threatPoint - feet));
        ShowStill();

        PlaySound(snortSound, 1.0f, loudBoost);
    }


    private void UpdateAlert(float dt)
    {
        stateTimer -= dt;
        if (stateTimer > 0.0f) return;

        if (SenseThreat()) BeginFlee();
        else BeginStand(RandomIn(standDuration));
    }


    private void BeginFlee()
    {
        seaY = SeaSurfaceY();
        RunBfs();

        if (!TryPlanFlee())
        {
            // cornered: hold still, watching it, and look again shortly
            state = DeerState.Alert;
            stateTimer = RandomIn(alertDuration) * 2.0f;
            TurnSide(ScreenX(threatPoint - feet));
            ShowStill();
            return;
        }

        state = DeerState.Flee;
        lingering = false;
        faceHint = ScreenX(threatPoint - feet);
        footfalls = 0;
    }


    private void UpdateFlee(float dt)
    {
        if (!Travel(fleeSpeed * gait, dt)) return;

        // still something close: keep going
        if (IsSpooked && SenseThreat())
        {
            BeginFlee();
            return;
        }

        // got away, but it stays jumpy until it calms down
        BeginStand(RandomIn(standDuration));
    }


    // MARK: fear


    private void UpdateFear(float dt)
    {
        if (spookedTimer > 0.0f) spookedTimer -= dt;

        detectTimer -= dt;
        if (detectTimer > 0.0f) return;

        detectTimer = detectInterval;

        if (!SenseThreat()) return;

        spookedTimer = RandomIn(spookedDuration);

        if (state == DeerState.Alert || state == DeerState.Flee) return;

        focusTimer = 0.0f;
        BeginAlert();
    }


    // Looks over the live animals for any it fears within reach. Sets threatAway to the direction
    // away from them all, the nearer ones counting for more, and threatPoint to the nearest
    private bool SenseThreat()
    {
        AnimalManager animals = AnimalManager.Instance;
        if (animals == null || fleeFrom == null || fleeFrom.Length == 0) return false;

        float radius = detectRadius * Mathf.Lerp(1.3f, 0.8f, boldness) * (1.0f + 0.2f * activity);
        float radiusSqr = radius * radius;

        Vector3 away = Vector3.zero;
        float nearestSqr = float.MaxValue;
        bool found = false;

        for (int i = 0; i < animals.LiveCount; i++)
        {
            AnimalController other = animals.LiveAt(i);
            if (other == null || other == this || !Fears(other.Animal)) continue;

            Vector3 offset = feet - other.transform.position;
            offset.y = 0.0f;

            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > radiusSqr) continue;

            found = true;
            away += offset / Mathf.Max(0.01f, distanceSqr);

            if (distanceSqr < nearestSqr)
            {
                nearestSqr = distanceSqr;
                threatPoint = other.transform.position;
            }
        }

        if (!found) return false;

        // standing right on top of it: any way is away
        if (away.sqrMagnitude < 0.000001f) away = leftHanded ? -cameraRight : cameraRight;

        threatAway = away.normalized;
        return true;
    }


    private bool Fears(Animal animal)
    {
        if (animal == null) return false;

        for (int i = 0; i < fleeFrom.Length; i++)
        {
            if (fleeFrom[i] == animal) return true;
        }

        return false;
    }


    // MARK: moving


    private float WalkSpeed()
    {
        return Mathf.Lerp(walkSpeed, rushSpeed, activity) * gait;
    }


    // Walks the planned path, hopping where it changes level. True once it has arrived, and then it
    // is already standing side-on
    private bool Travel(float speed, float dt)
    {
        if (hopping) return UpdateHop(dt);

        if (pathIndex >= pathCount)
        {
            ShowStill();
            return true;
        }

        Vector3 target = pathPoints[pathIndex];

        if (pathLevels[pathIndex] != level)
        {
            BeginHop(target);
            return false;
        }

        Vector3 before = feet;
        feet = Vector3.MoveTowards(feet, target, speed * dt);

        Vector3 moved = feet - before;
        FaceMovement(target - before);
        Animate(moved.magnitude);

        bool last = pathIndex == pathCount - 1;
        if (last && (target - feet).sqrMagnitude < turnDistance * turnDistance) TurnSide(EndHint(target - before));

        if ((feet - target).sqrMagnitude > ARRIVED_SQR) return false;

        return AdvancePath();
    }


    // which side to end a path on: towards what it came for, else the way it was heading
    private float EndHint(Vector3 heading)
    {
        if (faceHint != 0.0f) return faceHint;

        heading.y = 0.0f;
        return heading.sqrMagnitude > 0.000001f ? ScreenX(heading.normalized) : 0.0f;
    }


    // on to the next point, unless the ground there has changed under the plan
    private bool AdvancePath()
    {
        pathIndex++;

        if (pathIndex < pathCount)
        {
            Vector3 next = pathPoints[pathIndex];

            if (IsWalkable(Mathf.FloorToInt(next.x), Mathf.FloorToInt(next.z), out int top) && top == pathLevels[pathIndex]) return false;

            // cut short where it is; the next decision plans afresh
            pathCount = pathIndex;
        }

        TurnSide(faceHint);
        ShowStill();
        return true;
    }


    // cuts a hop short on the far side, for anything that stops it dead
    private void LandNow()
    {
        if (!hopping) return;

        hopping = false;
        feet = hopTo;
        level = pathLevels[pathIndex];
    }


    private void BeginHop(Vector3 target)
    {
        hopping = true;
        hopFrom = feet;
        hopTo = target;
        hopTime = 0.0f;
    }


    // A parabola from here to there, high enough to clear the ledge, in the facing it was walking
    private bool UpdateHop(float dt)
    {
        hopTime += dt;

        float t = Mathf.Clamp01(hopTime / Mathf.Max(0.01f, hopDuration));
        float climb = Mathf.Abs(hopTo.y - hopFrom.y);

        Vector3 before = feet;

        feet = Vector3.Lerp(hopFrom, hopTo, t);
        feet.y += (hopArc + climb * 0.5f) * 4.0f * t * (1.0f - t);

        FaceMovement(hopTo - hopFrom);
        Animate(new Vector2(feet.x - before.x, feet.z - before.z).magnitude);

        if (t < 1.0f) return false;

        hopping = false;
        feet = hopTo;
        level = pathLevels[pathIndex];

        PlaySound(stepSound, 1.0f, landVolume * Loudness());

        if (pathIndex == pathCount - 1) TurnSide(EndHint(hopTo - hopFrom));

        return AdvancePath();
    }


    // MARK: planning


    // Breadth first over the window around its cell: every cell it could walk to, how many steps
    // away, and by which way. Level ground is walked, one level up or down is hopped between
    // orthogonal neighbours, and a diagonal needs both cells it cuts between open on the same level
    private void RunBfs()
    {
        bfsStampNow++;
        bfsOriginX = Mathf.FloorToInt(feet.x) - pathRadius;
        bfsOriginZ = Mathf.FloorToInt(feet.z) - pathRadius;

        int start = pathRadius * bfsWidth + pathRadius;

        bfsStamp[start] = bfsStampNow;
        bfsParent[start] = -1;
        bfsSteps[start] = 0;
        bfsLevel[start] = level;

        int head = 0;
        int tail = 0;
        bfsQueue[tail++] = start;

        while (head < tail)
        {
            int current = bfsQueue[head++];
            int cx = current % bfsWidth;
            int cz = current / bfsWidth;
            int currentLevel = bfsLevel[current];

            for (int i = 0; i < NEIGHBOURS.Length; i++)
            {
                int nx = cx + NEIGHBOURS[i].x;
                int nz = cz + NEIGHBOURS[i].y;
                if (nx < 0 || nz < 0 || nx >= bfsWidth || nz >= bfsWidth) continue;

                int next = nz * bfsWidth + nx;
                if (bfsStamp[next] == bfsStampNow) continue;

                int worldX = bfsOriginX + nx;
                int worldZ = bfsOriginZ + nz;
                if (!IsWalkable(worldX, worldZ, out int top)) continue;

                bool diagonal = NEIGHBOURS[i].x != 0 && NEIGHBOURS[i].y != 0;

                if (diagonal)
                {
                    if (top != currentLevel) continue;
                    if (!IsWalkable(worldX, bfsOriginZ + cz, out int sideA) || sideA != currentLevel) continue;
                    if (!IsWalkable(bfsOriginX + cx, worldZ, out int sideB) || sideB != currentLevel) continue;
                }
                else if (Mathf.Abs(top - currentLevel) > 1)
                {
                    continue;
                }

                bfsStamp[next] = bfsStampNow;
                bfsParent[next] = current;
                bfsSteps[next] = bfsSteps[current] + 1;
                bfsLevel[next] = top;
                bfsQueue[tail++] = next;
            }
        }

        bfsReachedCount = tail;
    }


    // the window index of a world column, or -1 when it's outside the window or wasn't reached
    private int ReachedIndex(int x, int z)
    {
        int wx = x - bfsOriginX;
        int wz = z - bfsOriginZ;
        if (wx < 0 || wz < 0 || wx >= bfsWidth || wz >= bfsWidth) return -1;

        int index = wz * bfsWidth + wx;
        return bfsStamp[index] == bfsStampNow ? index : -1;
    }


    // Lays the path to a reached cell out as points, one per cell, ending on finalPoint
    private void BuildPath(int goal, Vector3 finalPoint)
    {
        int count = 0;
        for (int at = goal; at >= 0 && bfsParent[at] >= 0; at = bfsParent[at]) count++;

        pathCount = count;
        pathIndex = 0;
        hopping = false;
        footfalls = 0;

        // already in the goal cell: just the step over to the point, e.g. up to a fern beside it
        if (count == 0)
        {
            pathLevels[0] = bfsLevel[goal];
            pathPoints[0] = new Vector3(finalPoint.x, terrain.GetLevelHeight(bfsLevel[goal]), finalPoint.z);
            pathCount = 1;
            return;
        }

        int slot = count - 1;

        for (int at = goal; at >= 0 && bfsParent[at] >= 0; at = bfsParent[at])
        {
            int x = bfsOriginX + at % bfsWidth;
            int z = bfsOriginZ + at / bfsWidth;

            pathLevels[slot] = bfsLevel[at];
            pathPoints[slot] = at == goal
                ? new Vector3(finalPoint.x, terrain.GetLevelHeight(bfsLevel[at]), finalPoint.z)
                : new Vector3(
                    x + 0.5f + Random.Range(-pathJitter, pathJitter),
                    terrain.GetLevelHeight(bfsLevel[at]),
                    z + 0.5f + Random.Range(-pathJitter, pathJitter));

            slot--;
        }
    }


    // A weighted pick of a cell some steps away, pulled towards the rest of the herd
    private bool TryPlanWander()
    {
        int minSteps = Mathf.Max(1, Mathf.Min(wanderSteps.x, wanderSteps.y));
        int maxSteps = Mathf.Max(minSteps, Mathf.Max(wanderSteps.x, wanderSteps.y));

        float total = 0.0f;

        for (int i = 1; i < bfsReachedCount; i++) total += WanderWeight(bfsQueue[i], minSteps, maxSteps);

        if (total <= 0.0f) return false;

        // a few tries, so it doesn't pick a cell with something standing in it
        for (int attempt = 0; attempt < 4; attempt++)
        {
            float roll = Random.value * total;

            for (int i = 1; i < bfsReachedCount; i++)
            {
                int cell = bfsQueue[i];
                float weight = WanderWeight(cell, minSteps, maxSteps);
                if (weight <= 0.0f) continue;

                roll -= weight;
                if (roll > 0.0f) continue;

                if (IsOccupied(cell)) break;

                BuildPath(cell, PointIn(cell));
                return true;
            }
        }

        return false;
    }


    private float WanderWeight(int cell, int minSteps, int maxSteps)
    {
        int steps = bfsSteps[cell];
        if (steps < minSteps || steps > maxSteps) return 0.0f;

        return 1.0f + (NearOtherDeer(CellCentre(cell), herdRadius) ? herdWeight : 0.0f);
    }


    // off forbidden ground by the shortest way
    private bool TryPlanNearestWalkable()
    {
        // the queue is in step order and the start is first in it, so the second is the nearest
        if (bfsReachedCount < 2) return false;

        int cell = bfsQueue[1];
        BuildPath(cell, PointIn(cell));
        faceHint = 0.0f;
        return true;
    }


    // the reached cell that best gets it away: along threatAway, and far from the nearest threat
    private bool TryPlanFlee()
    {
        int best = -1;
        float bestScore = 0.0f;

        Vector3 flatFeet = new Vector3(feet.x, 0.0f, feet.z);
        Vector3 flatThreat = new Vector3(threatPoint.x, 0.0f, threatPoint.z);
        float startDistance = Vector3.Distance(flatFeet, flatThreat);

        for (int i = 1; i < bfsReachedCount; i++)
        {
            int cell = bfsQueue[i];
            if (bfsSteps[cell] > fleeSteps) continue;

            Vector3 centre = CellCentre(cell);
            centre.y = 0.0f;

            float along = Vector3.Dot(centre - flatFeet, threatAway);
            float gained = Vector3.Distance(centre, flatThreat) - startDistance;
            float score = along + 0.5f * gained;

            if (score <= bestScore) continue;

            best = cell;
            bestScore = score;
        }

        if (best < 0 || bestScore < 0.5f) return false;

        BuildPath(best, PointIn(best));
        return true;
    }


    // A reached free cell beside a fern, preferring the fern's screen left or right so the deer ends
    // up side-on to it. Picked across ferns by nearness
    private int FindGrazeGoal(out EntityId fern, out Vector3 point)
    {
        fern = null;
        point = default;

        EntityManager entities = EntityManager.Instance;
        if (entities == null || foodItems == null) return -1;

        float total = 0.0f;
        int chosen = -1;

        for (int i = 0; i < foodItems.Length; i++)
        {
            if (foodItems[i] == null) continue;

            entities.CollectEntitiesOf(foodItems[i], scratchIds);

            for (int j = 0; j < scratchIds.Count; j++)
            {
                EntityId candidate = scratchIds[j];
                if (candidate == null || !InSight(candidate.transform.position)) continue;

                int goal = BestNeighbour(candidate.transform.position, true);
                if (goal < 0) continue;

                // reservoir pick, weighted by nearness, so no list is kept
                float weight = 1.0f / (1.0f + bfsSteps[goal]);
                total += weight;
                if (Random.value * total > weight) continue;

                chosen = goal;
                fern = candidate;
            }
        }

        if (chosen < 0) return -1;

        point = GrazePoint(chosen, fern.transform.position);
        return chosen;
    }


    // Right up against the fern, on the screen side the path comes in from, so it eats side-on. That
    // point is in the fern's own cell, so it needs that cell walkable and on the same level as the
    // cell beside it; otherwise it eats from the edge of the cell beside it
    private Vector3 GrazePoint(int cell, Vector3 fern)
    {
        int fx = Mathf.FloorToInt(fern.x);
        int fz = Mathf.FloorToInt(fern.z);

        if (!IsWalkable(fx, fz, out int top) || top != bfsLevel[cell]) return PointTowards(cell, fern);

        float side = ScreenX(CellCentre(cell) - fern) >= 0.0f ? 1.0f : -1.0f;

        Vector3 point = fern + cameraRight * (side * grazeDistance * size) - cameraForward * grazeInFront;
        point.y = terrain.GetLevelHeight(top);

        return point;
    }


    // A reached free cell beside a tree, on its screen-front side so the trunk never hides the deer
    private int FindRestGoal(out Vector3 point)
    {
        point = default;

        EntityManager entities = EntityManager.Instance;
        if (entities == null || shelterItems == null) return -1;

        float total = 0.0f;
        int chosen = -1;
        Vector3 tree = default;

        for (int i = 0; i < shelterItems.Length; i++)
        {
            if (shelterItems[i] == null) continue;

            entities.CollectEntitiesOf(shelterItems[i], scratchIds);

            for (int j = 0; j < scratchIds.Count; j++)
            {
                EntityId candidate = scratchIds[j];
                if (candidate == null || !InSight(candidate.transform.position)) continue;

                int goal = BestNeighbour(candidate.transform.position, false);
                if (goal < 0) continue;

                float weight = 1.0f / (1.0f + bfsSteps[goal]);
                total += weight;
                if (Random.value * total > weight) continue;

                chosen = goal;
                tree = candidate.transform.position;
            }
        }

        if (chosen < 0) return -1;

        point = PointTowards(chosen, tree);
        return chosen;
    }


    // the best reached, free neighbour of an item's cell: to its screen side, or in front of it
    private int BestNeighbour(Vector3 itemPosition, bool side)
    {
        int ix = Mathf.FloorToInt(itemPosition.x);
        int iz = Mathf.FloorToInt(itemPosition.z);

        int best = -1;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < NEIGHBOURS.Length; i++)
        {
            int cell = ReachedIndex(ix + NEIGHBOURS[i].x, iz + NEIGHBOURS[i].y);
            if (cell < 0) continue;

            Vector3 offset = new Vector3(NEIGHBOURS[i].x, 0.0f, NEIGHBOURS[i].y);
            float sx = Vector3.Dot(offset, cameraRight);
            float sy = Vector3.Dot(offset, cameraForward);

            float score = side ? Mathf.Abs(sx) - 0.5f * Mathf.Abs(sy) : -sy;
            score -= 0.1f * bfsSteps[cell];

            if (score <= bestScore || IsOccupied(cell)) continue;

            best = cell;
            bestScore = score;
        }

        return best;
    }


    private bool InSight(Vector3 position)
    {
        float dx = position.x - feet.x;
        float dz = position.z - feet.z;

        return dx * dx + dz * dz <= sightRadius * sightRadius;
    }


    private bool IsOccupied(int cell)
    {
        EntityManager entities = EntityManager.Instance;
        if (entities == null) return false;

        return entities.HasEntityAt(new Vector3Int(bfsOriginX + cell % bfsWidth, bfsLevel[cell], bfsOriginZ + cell / bfsWidth));
    }


    private Vector3 CellCentre(int cell)
    {
        return new Vector3(bfsOriginX + cell % bfsWidth + 0.5f, terrain.GetLevelHeight(bfsLevel[cell]), bfsOriginZ + cell / bfsWidth + 0.5f);
    }


    // a random point inside the cell, on its top
    private Vector3 PointIn(int cell)
    {
        return new Vector3(
            bfsOriginX + cell % bfsWidth + Random.Range(cellMargin, 1.0f - cellMargin),
            terrain.GetLevelHeight(bfsLevel[cell]),
            bfsOriginZ + cell / bfsWidth + Random.Range(cellMargin, 1.0f - cellMargin));
    }


    // near the cell's centre, nudged towards what it came for
    private Vector3 PointTowards(int cell, Vector3 towards)
    {
        Vector3 centre = CellCentre(cell);
        Vector3 offset = towards - centre;
        offset.y = 0.0f;

        if (offset.sqrMagnitude > 0.0001f) centre += offset.normalized * (0.5f - cellMargin);

        return centre;
    }


    // MARK: spreading


    // Only while it stays put grazing or resting: straight away when it settles, then every
    // spreadInterval, one cell per whole credit
    private void UpdateSpread(float dt)
    {
        spreadTimer -= dt;
        if (spreadTimer > 0.0f) return;

        spreadTimer = spreadInterval;

        if (spreadCredit < 1.0f || spreadFrom == null || spreadTo == null) return;
        if (!TryPickSpread(out Vector3Int cell)) return;

        if (TerrainSpread.TrySpread(cell, spreadFrom, spreadTo, spreadNextTo, spreadSound)) spreadCredit -= 1.0f;
    }


    // the spreadable cell nearest its feet within spreadRadius, ties at random, so the grass visibly
    // comes from the deer. At most (2r+1)^2 columns, once a second, and no allocation
    private bool TryPickSpread(out Vector3Int picked)
    {
        picked = default;

        int x = Mathf.FloorToInt(feet.x);
        int z = Mathf.FloorToInt(feet.z);

        float best = float.PositiveInfinity;
        int ties = 0;

        for (int dz = -spreadRadius; dz <= spreadRadius; dz++)
        {
            for (int dx = -spreadRadius; dx <= spreadRadius; dx++)
            {
                Vector3Int cell = new Vector3Int(x + dx, terrain.GetTopLevel(x + dx, z + dz), z + dz);
                if (!TerrainSpread.CanSpread(cell, spreadFrom, spreadTo, spreadNextTo)) continue;

                float distance = new Vector2(cell.x + 0.5f - feet.x, cell.z + 0.5f - feet.z).sqrMagnitude;
                if (distance > best + ARRIVED_SQR) continue;

                // a new nearest starts the ties over; an equal one replaces it with a fair chance
                if (distance < best - ARRIVED_SQR)
                {
                    best = distance;
                    ties = 0;
                }

                ties++;
                if (Random.Range(0, ties) == 0) picked = cell;
            }
        }

        return ties > 0;
    }


    // MARK: ground


    // A column it can stand on: it has a top, the top isn't forbidden, and the risen sea isn't over it
    private bool IsWalkable(int x, int z, out int top)
    {
        top = terrain.GetTopLevel(x, z);
        if (top < 0) return false;

        if (terrain.GetLevelHeight(top) < seaY) return false;

        TerrainTile tile = terrain.GetTile(new Vector3Int(x, top, z));
        return tile != null && !IsForbidden(tile);
    }


    private bool IsForbidden(TerrainTile tile)
    {
        if (forbiddenTiles == null || tile == null) return false;

        for (int i = 0; i < forbiddenTiles.Length; i++)
        {
            if (forbiddenTiles[i] == tile) return true;
        }

        return false;
    }


    // the sea's surface only counts once it has risen; before that it waits below the terrain
    private static float SeaSurfaceY()
    {
        SeaLevel sea = SeaLevel.Instance;
        if (sea == null || !sea.IsRisen) return float.NegativeInfinity;

        return sea.SurfaceY;
    }


    // the transform may already be lifted (a saved deer), so the height comes from the terrain alone
    private void SnapToSurface()
    {
        Vector3 position = transform.position;
        int top = terrain.GetTopLevel(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));

        if (top < 0)
        {
            Debug.LogWarning("DeerController: the deer is not over the terrain, so it stays where it is", this);
            level = Mathf.Max(0, terrain.GetLevelBelow(position.y));
            feet = position;
            return;
        }

        level = top;
        feet = new Vector3(position.x, terrain.GetLevelHeight(top), position.z);
    }


    // the lift is measured per call so it follows the frame, the hover grow and the deer's size
    private void ApplyPosition()
    {
        float lift = -CurrentBottom() * transform.lossyScale.y;
        transform.position = feet + Vector3.up * lift + ReactionOffset;
    }


    // MARK: facing and frames


    private void FindCameraAxes()
    {
        Camera view = Camera.main;

        if (view == null)
        {
            Debug.LogError("DeerController: no camera tagged MainCamera, so the deer faces as if the camera looked along +Z", this);
            cameraRight = Vector3.right;
            cameraForward = Vector3.forward;
            return;
        }

        // the camera never rotates, so its flat axes are read once
        cameraRight = view.transform.right;
        cameraRight.y = 0.0f;
        cameraRight.Normalize();

        cameraForward = view.transform.forward;
        cameraForward.y = 0.0f;
        cameraForward.Normalize();
    }


    private float ScreenX(Vector3 direction)
    {
        return Vector3.Dot(direction, cameraRight);
    }


    // The closest of the four screen directions, with the sides favoured a little and a margin
    // before it changes its mind, so a diagonal doesn't flicker between two sprites
    private void FaceMovement(Vector3 direction)
    {
        direction.y = 0.0f;
        if (direction.sqrMagnitude < 0.000001f) return;

        direction.Normalize();

        float sx = Vector3.Dot(direction, cameraRight) * sideBias;
        float sy = Vector3.Dot(direction, cameraForward);

        DeerFacing best = DeerFacing.Right;

        for (int i = 0; i < 4; i++)
        {
            if (FacingScore((DeerFacing)i, sx, sy) > FacingScore(best, sx, sy)) best = (DeerFacing)i;
        }

        if (best == facing) return;
        if (FacingScore(best, sx, sy) < FacingScore(facing, sx, sy) + facingHysteresis) return;

        SetFacing(best);
    }


    private static float FacingScore(DeerFacing candidate, float sx, float sy)
    {
        switch (candidate)
        {
            case DeerFacing.Right: return sx;
            case DeerFacing.Left: return -sx;
            case DeerFacing.Up: return sy;
            default: return -sy;
        }
    }


    // Never left on the up or down sprite: turn to whichever side the hint leans, else stay on the
    // side it's on, else its own favourite side. An already sideways deer keeps its side unless the
    // hint says otherwise
    private void TurnSide(float screenX)
    {
        if (screenX > 0.05f)
        {
            SetFacing(DeerFacing.Right);
            return;
        }

        if (screenX < -0.05f)
        {
            SetFacing(DeerFacing.Left);
            return;
        }

        if (facing == DeerFacing.Up || facing == DeerFacing.Down) SetFacing(leftHanded ? DeerFacing.Left : DeerFacing.Right);
    }


    private void SetFacing(DeerFacing newFacing)
    {
        facing = newFacing;
        ShowFrame();
    }


    // Moves the walk cycle on by distance. Each new frame that is a footfall makes a step
    private void Animate(float distance)
    {
        Sprite[] row = frames[(int)facing];
        if (row.Length == 0) return;

        int before = Mathf.FloorToInt(framePhase);
        framePhase += distance * framesPerUnit;

        int after = Mathf.FloorToInt(framePhase);
        if (after == before) return;

        frame = after % row.Length;
        ShowFrame();

        // kept small, so a deer that walks all session never runs out of float precision
        framePhase -= after - frame;

        if (IsFootfall(frame)) Footfall();
    }


    private bool IsFootfall(int index)
    {
        if (footfallFrames == null) return false;

        for (int i = 0; i < footfallFrames.Length; i++)
        {
            if (footfallFrames[i] == index) return true;
        }

        return false;
    }


    private void Footfall()
    {
        footfalls++;

        // a big herd would fill every voice with hooves
        bool sound = herd.Count <= busyHerdSize || footfalls % 2 == 0;
        if (sound) PlaySound(stepSound, 1.0f, Loudness());

        // stotting: a bounce every few strides while it runs
        if (state == DeerState.Flee && footfalls % stotEvery == 0) Hop(stotHeight, stotDuration);
    }


    private void ShowStill()
    {
        TurnSide(0.0f);
        frame = standFrame;
        ShowFrame();
    }


    private void ShowFrame()
    {
        Sprite[] row = frames[(int)facing];
        if (row.Length == 0 || spriteRenderer == null) return;

        frame = Mathf.Clamp(frame, 0, row.Length - 1);
        spriteRenderer.sprite = row[frame];
    }


    private float CurrentBottom()
    {
        if (frameBottoms == null) return 0.0f;

        float[] row = frameBottoms[(int)facing];
        return row.Length > 0 ? row[Mathf.Clamp(frame, 0, row.Length - 1)] : 0.0f;
    }


    // The sprite's lowest vertex below its pivot. Vertices rather than bounds, so a tight sprite
    // mesh stands on its visible bottom; the slices are tight and their heights differ, so each
    // frame gets its own. Kept in sprite units: ApplyPosition scales it
    private static float MeasureBottom(Sprite sprite)
    {
        if (sprite == null) return 0.0f;

        Vector2[] vertices = sprite.vertices;
        if (vertices == null || vertices.Length == 0) return 0.0f;

        float lowest = vertices[0].y;
        for (int i = 1; i < vertices.Length; i++) lowest = Mathf.Min(lowest, vertices[i].y);

        return lowest;
    }


    // MARK: the day


    // 0..1: the floor through the middle of the day and night, rising to 1 at sunrise and sunset.
    // Everything is a share of the whole cycle, read live, so it follows any day length
    private float Activity()
    {
        WorldStateManager world = WorldStateManager.Instance;
        if (world == null) return activityFloor;

        float cycle = world.DayDuration + world.NightDuration;
        if (cycle <= 0.0f) return activityFloor;

        float t = world.CycleTime / cycle;
        float sunset = world.DayDuration / cycle;

        // sunrise is at 0, so the dawn straddles the wrap
        float toSunrise = Mathf.Min(t, 1.0f - t);
        float toSunset = Mathf.Abs(t - sunset);

        float bump = Mathf.Max(Bump(toSunrise), Bump(toSunset));

        return Mathf.Lerp(activityFloor, 1.0f, bump);
    }


    private float Bump(float distance)
    {
        return 1.0f - Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(distance / activityWidth));
    }


    private bool IsDusk()
    {
        WorldStateManager world = WorldStateManager.Instance;
        if (world == null) return false;

        float cycle = world.DayDuration + world.NightDuration;
        if (cycle <= 0.0f) return false;

        return Mathf.Abs(world.CycleTime - world.DayDuration) / cycle < activityWidth;
    }


    // the directional light that DayNightManager turns; RenderSettings.sun when the scene names one
    private void FindSun()
    {
        if (RenderSettings.sun != null)
        {
            sun = RenderSettings.sun.transform;
            return;
        }

        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type != LightType.Directional) continue;

            sun = lights[i].transform;
            return;
        }
    }


    // which side of the screen the sun is on; the light shines along its forward, so the sun is behind it
    private float SunScreenX()
    {
        return sun != null ? ScreenX(-sun.forward) : 0.0f;
    }


    // MARK: grazing


    // the fern's sprite child gives a little shake, and the deer munches. the fern itself is never
    // changed: the punch is on the child (growth animates the root), linked to the fern, and
    // BillboardSprite only sets that rotation once, in its Start
    private void Munch()
    {
        PlaySound(munchSound, 1.0f, 1.0f);

        if (grazeFern == null) return;

        SpriteRenderer fernSprite = grazeFern.GetComponentInChildren<SpriteRenderer>();
        if (fernSprite == null) return;

        Transform shaken = fernSprite.transform;
        if (shaken == grazeFern.transform) return;

        // two deer at one fern: finish the last shake first, so it springs back to where it rests
        shaken.DOComplete();
        shaken.DOPunchRotation(new Vector3(0.0f, 0.0f, Random.value < 0.5f ? rustleAngle : -rustleAngle), rustleDuration, 6, 0.5f)
            .SetLink(grazeFern.gameObject);
    }


    // MARK: voice


    private float Loudness()
    {
        return Mathf.Lerp(1.0f, loudBoost, activity);
    }


    // brays come round sooner at dawn and dusk, and an answer comes when another deer called
    private void UpdateVoice(float dt)
    {
        if (answerTimer > 0.0f)
        {
            answerTimer -= dt;

            if (answerTimer <= 0.0f && CanBray())
            {
                Bray(false);
                brayTimer = RandomIn(brayInterval);
            }
        }

        brayTimer -= dt / Mathf.Lerp(1.0f, brayActiveScale, activity);
        if (brayTimer > 0.0f) return;

        brayTimer = RandomIn(brayInterval);

        if (CanBray()) Bray(true);
    }


    private bool CanBray()
    {
        return state != DeerState.Alert && state != DeerState.Flee;
    }


    // a call is answered by some of the deer within earshot; an answer is never answered again
    private void Bray(bool call)
    {
        PlaySound(braySound, brayPitch, Loudness());

        if (!call) return;

        for (int i = 0; i < herd.Count; i++)
        {
            DeerController other = herd[i];
            if (other == this || other.answerTimer > 0.0f) continue;
            if ((other.feet - feet).sqrMagnitude > answerRadius * answerRadius) continue;
            if (Random.value > answerChance) continue;

            other.answerTimer = RandomIn(answerDelay);
        }
    }


    private void PlaySound(SoundObject sound, float pitchScale, float volumeScale)
    {
        if (sound == null || AudioManager.Instance == null) return;

        AudioManager.Instance.PlaySFX(sound, transform.position, pitchScale, volumeScale);
    }


    // MARK: herd


    private bool NearOtherDeer(Vector3 point, float radius)
    {
        for (int i = 0; i < herd.Count; i++)
        {
            DeerController other = herd[i];
            if (other == this) continue;

            float dx = other.feet.x - point.x;
            float dz = other.feet.z - point.z;
            if (dx * dx + dz * dz <= radius * radius) return true;
        }

        return false;
    }


    // MARK: traits


    // from the guid, so a deer has the same look every load (see EntitySeed)
    private void ApplyTraits()
    {
        EntityId id = EntityId != null ? EntityId : GetComponent<EntityId>();
        string guid = id != null ? id.Guid : name;

        seed = EntitySeed.FromGuid(guid);

        size = Mathf.Lerp(sizeRange.x, sizeRange.y, Trait01(SALT_SIZE));

        // before anything can hover it, so the hover grow rests on the grown size
        transform.localScale *= size;

        float temperature = Trait01(SALT_TEMPERATURE) * 2.0f - 1.0f;
        Color tint = temperature >= 0.0f
            ? Color.Lerp(Color.white, warmTint, temperature)
            : Color.Lerp(Color.white, coolTint, -temperature);
        tint *= Mathf.Lerp(brightnessRange.x, brightnessRange.y, Trait01(SALT_BRIGHTNESS));
        tint.a = 1.0f;

        if (spriteRenderer != null) spriteRenderer.color = tint;

        boldness = Trait01(SALT_BOLDNESS);
        appetite = Trait01(SALT_APPETITE);
        restlessness = Trait01(SALT_RESTLESSNESS);
        gait = 1.0f + (Trait01(SALT_GAIT) * 2.0f - 1.0f) * gaitVariation;
        leftHanded = Trait01(SALT_HANDEDNESS) < 0.5f;

        // smaller deer have higher voices
        brayPitch = 1.0f / Mathf.Sqrt(Mathf.Max(0.1f, size));

        // not in step with the rest of the herd
        framePhase = Trait01(SALT_PHASE) * 6.0f;
    }


    // one trait, 0..1, from the seed mixed with its own salt
    private float Trait01(uint salt)
    {
        return EntitySeed.Trait01(seed, salt);
    }


    // MARK: reactions


    // stops and turns side-on to look back, then carries on
    public override void OnFocused()
    {
        base.OnFocused();
        happyLineIndex = -1;

        // mid-hop or running for its life, it has no time to look
        if (hopping || state == DeerState.Alert || state == DeerState.Flee) return;

        focusTimer = focusFreeze;

        TurnSide(0.0f);
        ShowStill();
    }


    // it answers to its new name, and the herd may answer it
    public override void OnRenamed()
    {
        base.OnRenamed();

        if (CanBray()) Bray(true);
    }


    // MARK: happiness


    public override float Happiness
    {
        get
        {
            if (terrain == null) return 1.0f;

            float happiness = baseHappiness;

            happiness += fernBonus * Mathf.Clamp01((float)CountNear(foodItems, happinessRadius) / fernsForFull);

            // with no trees to want yet, it doesn't miss them
            if (!HasAny(shelterItems) || CountNear(shelterItems, happinessRadius) > 0) happiness += shelterBonus;

            // likewise company, if it's the only deer there can be
            if (!CanHaveCompany() || NearOtherDeer(feet, herdRadius)) happiness += companyBonus;

            if (IsSpooked) happiness -= spookedPenalty;

            return Mathf.Clamp01(happiness);
        }
    }


    public override string[] GetHappinessTips()
    {
        if (terrain == null) return base.GetHappinessTips();

        bool hungry = HasAny(foodItems) && CountNear(foodItems, happinessRadius) == 0;
        bool exposed = HasAny(shelterItems) && CountNear(shelterItems, happinessRadius) == 0;
        bool lonely = CanHaveCompany() && !NearOtherDeer(feet, herdRadius);
        bool spooked = IsSpooked;

        int count = (hungry ? 1 : 0) + (exposed ? 1 : 0) + (lonely ? 1 : 0) + (spooked ? 1 : 0);
        if (count == 0) return base.GetHappinessTips();

        // happy again later is a new mood, so a new line
        happyLineIndex = -1;

        string[] tips = new string[count];
        int at = 0;

        if (spooked) tips[at++] = spookedTip;
        if (hungry) tips[at++] = noFoodTip;
        if (exposed) tips[at++] = noShelterTip;
        if (lonely) tips[at++] = lonelyTip;

        return tips;
    }


    public override string GetHappyLine()
    {
        if (happyLines == null || happyLines.Length == 0) return null;

        if (happyLineIndex < 0 || happyLineIndex >= happyLines.Length) happyLineIndex = Random.Range(0, happyLines.Length);

        return happyLines[happyLineIndex];
    }


    private bool CanHaveCompany()
    {
        return Animal != null && Animal.MobCap > 1;
    }


    private static bool HasAny(GameItem[] items)
    {
        if (items == null) return false;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null) return true;
        }

        return false;
    }


    // how many of these items stand within radius. only while deciding, or while its panel asks
    private int CountNear(GameItem[] items, float radius)
    {
        EntityManager entities = EntityManager.Instance;
        if (entities == null || items == null) return 0;

        int count = 0;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null) continue;

            entities.CollectEntitiesOf(items[i], scratchIds);

            for (int j = 0; j < scratchIds.Count; j++)
            {
                if (scratchIds[j] == null) continue;

                Vector3 position = scratchIds[j].transform.position;
                float dx = position.x - feet.x;
                float dz = position.z - feet.z;

                if (dx * dx + dz * dz <= radius * radius) count++;
            }
        }

        return count;
    }


    // MARK: internals


    private static float RandomIn(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }
}
