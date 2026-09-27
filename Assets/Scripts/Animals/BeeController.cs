using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;


public enum BeeState
{
    Waiting,    // the loaded world isn't ready yet, so it doesn't know where home is
    InHive,
    Emerge,
    Fly,
    Drink,
    Dance,
    Enter,
    Peek,
    Sleep,
}


public enum BeeFacing
{
    Down,
    Up,
    Left,
    Right,
}


// A bee: lives in a hive, forages on flowers by day, dances on the way home, and stays in at night.
//
// Its day is a share of the daylight, read live, so it follows any day length: it wakes a little after
// sunrise, heads home a little before sunset (each bee at its own time), and hurries at the end. At
// night it is inside. Inside, the sprite is off but the bee keeps milling about within the hive's
// drawn bounds, so a starred bee's glow (drawn even while hidden, see AnimalGlow) shows it moving
// about in there. Clicking that glow makes it peek out of the exit.
//
// A trip is a few flowers: fly to one, hover over it with a dip or two while it drinks (the flower
// rustles, and is never changed: placed items drive puzzles), then on to another, preferring a
// different one and a different kind. Home again, it waggle-dances in front of the exit: the waggle
// runs point at the last flower (as the player sees it, or measured from the sun, as real bees do)
// and are longer the further away it was. The dance also offers that flower to the next bee out.
//
// Flight floats over the top surface, and the risen sea: it climbs ahead of a cliff rather than
// hopping, and every target is on the map. Facing is the nearest of four screen directions, as the
// deer's; side-on is the bob cycle, and up or down (which have one frame each) vibrate in the
// sprite's own plane instead.
//
// Home is the hive's guid in its save row (AnimalController.HomeGuid). A bee whose hive is gone adopts
// the nearest one with room; with none, it forages wild and sleeps on a flower at night. A bee whose
// hive is under the risen sea keeps it (so the count stays honest), but can't go in.
//
// Nothing else is saved. AnimalManager writes the position through; a loaded bee works out from the
// clock whether it should be in or out, and carries on.
[RequireComponent(typeof(SpriteRenderer))]
public class BeeController : AnimalController
{
    // salts for the per-trait hashes; new traits get new salts, so adding one never moves the others
    private const uint SALT_SIZE = 0x9E3779B9u;
    private const uint SALT_TEMPERATURE = 0x85EBCA6Bu;
    private const uint SALT_BRIGHTNESS = 0xC2B2AE35u;
    private const uint SALT_SPEED = 0x27D4EB2Fu;
    private const uint SALT_PHASE = 0x165667B1u;
    private const uint SALT_WAKE = 0xD3A2646Cu;
    private const uint SALT_HOME = 0xFD7046C5u;
    private const uint SALT_VISITS = 0xB55A4F09u;
    private const uint SALT_HANDEDNESS = 0x7ED55D16u;
    private const uint SALT_CRUISE = 0x94D049BBu;

    private const float TAU = Mathf.PI * 2.0f;

    // at most one bee buzzes this often, so a swarm never fills the SFX voices
    private static float lastBuzzTime = float.NegativeInfinity;


    [Header("Look")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    // side-on, pointing right (flipped for left), played in order: bee_1, bee_2, bee_1, bee_5
    [SerializeField] private Sprite[] sideFrames = new Sprite[0];
    [SerializeField] private Sprite downSprite;                            // towards the camera
    [SerializeField] private Sprite upSprite;                              // away from it
    [SerializeField] private float framesPerSecond = 12.0f;
    [SerializeField] [Min(1.0f)] private float sideBias = 1.1f;            // side sprites read better, so they win near-diagonals
    [SerializeField] private float facingHysteresis = 0.15f;               // a new facing must beat the old by this much
    // up and down have no cycle, so while moving on them it vibrates in its own plane, never along z
    [SerializeField] private Vector2 vibrateAmplitude = new Vector2(0.012f, 0.008f);   // world units, across and up
    [SerializeField] private float vibrateFrequency = 28.0f;               // hz
    [SerializeField] private float fadeDuration = 0.3f;                    // in and out of the hive


    [Header("Variety")]
    [SerializeField] private Vector2 sizeRange = new Vector2(0.88f, 1.12f);
    [SerializeField] private Color warmTint = new Color(1.0f, 0.95f, 0.82f);
    [SerializeField] private Color coolTint = new Color(0.92f, 0.95f, 1.0f);
    [SerializeField] private Vector2 brightnessRange = new Vector2(0.93f, 1.0f);
    [SerializeField] [Range(0.0f, 0.5f)] private float speedVariation = 0.1f;


    [Header("Flight")]
    [SerializeField] private float flySpeed = 0.9f;                        // world units a second
    [SerializeField] private float hurrySpeed = 1.5f;                      // heading home at the end of the day
    [SerializeField] private float cruiseHeight = 0.55f;                   // above the ground or the sea
    [SerializeField] [Range(0.0f, 0.5f)] private float cruiseVariation = 0.15f;
    [SerializeField] private float lookAhead = 0.7f;                       // world units ahead it climbs for
    [SerializeField] private float climbSmoothTime = 0.25f;
    [SerializeField] private float descentDistance = 0.8f;                 // it drops from cruise to its target over this
    [SerializeField] private float arriveDistance = 0.04f;
    [SerializeField] private float wobbleAmplitude = 0.07f;                // sideways drift off the straight line
    [SerializeField] private float wobbleFrequency = 0.8f;                 // hz
    [SerializeField] private float mapMargin = 0.3f;                       // targets stay this far inside the map


    [Header("The day")]
    // shares of the daylight (0 sunrise, 1 sunset): each bee picks its own time in these
    [SerializeField] private Vector2 wakeRange = new Vector2(0.03f, 0.12f);
    [SerializeField] private Vector2 homeRange = new Vector2(0.82f, 0.92f);
    [SerializeField] [Range(0.0f, 1.0f)] private float hurryFrom = 0.95f;
    [SerializeField] private Vector2 hiveRest = new Vector2(3.0f, 8.0f);    // seconds home between trips


    [Header("Foraging")]
    [SerializeField] private GameItem[] flowerItems = new GameItem[0];
    [SerializeField] private float forageRadius = 8.0f;                    // world units from its hive
    [SerializeField] private Vector2Int visitsPerTrip = new Vector2Int(3, 5);
    [SerializeField] [Range(0.0f, 1.0f)] private float sameKindWeight = 0.3f;
    [SerializeField] [Min(1.0f)] private float recruitWeight = 4.0f;       // pull of the flower last danced for
    [SerializeField] private Vector2 drinkDuration = new Vector2(2.0f, 4.0f);
    // where on a flower it drinks: a child named "Nectar..." under the flower (or its sprite) if it has
    // any, else a random point in this part of the flower's sprite rect (0..1 across and up), where
    // the blossoms of a bushy flower are rather than the top of the bush
    [SerializeField] private Vector2 nectarAcross = new Vector2(0.3f, 0.7f);
    [SerializeField] private Vector2 nectarUp = new Vector2(0.35f, 0.7f);
    [SerializeField] private float drinkHeight = 0.02f;                    // above the nectar point
    [SerializeField] private float drinkInFront = 0.05f;                   // towards the camera, so it draws in front
    [SerializeField] private float dipDepth = 0.03f;
    [SerializeField] [Min(1)] private int dipsPerDrink = 2;
    [SerializeField] private float flowerRustleAngle = 6.0f;               // degrees
    [SerializeField] private float flowerRustleDuration = 0.4f;
    [SerializeField] private float wanderRadius = 2.0f;                    // with no flower to go to


    [Header("Dance")]
    [SerializeField] private float danceInFront = 0.2f;                    // of the exit, towards the camera
    [SerializeField] private float danceAbove = 0.12f;
    [SerializeField] private Vector2 waggleRunLength = new Vector2(0.08f, 0.22f);   // near flower .. forageRadius away
    [SerializeField] private float waggleRunSpeed = 0.12f;                 // world units a second along the run
    [SerializeField] private float waggleAmplitude = 0.02f;
    [SerializeField] private float waggleFrequency = 13.0f;                // hz, as a real bee's
    [SerializeField] private float returnSpeed = 0.35f;
    [SerializeField] private Vector2Int danceCircuits = new Vector2Int(2, 3);
    // real bees dance the flower's angle from the sun; off, the run points where the player sees it
    [SerializeField] private bool danceFromSun = false;


    [Header("In the hive")]
    [SerializeField] private float millSpeed = 0.15f;                      // across the hive's sprite a second
    [SerializeField] private Vector2 millPause = new Vector2(0.3f, 1.5f);


    [Header("Reactions")]
    [SerializeField] private float emergeOut = 0.25f;                      // towards the camera from the exit
    [SerializeField] private float emergeDuration = 0.6f;
    [SerializeField] private float enterDuration = 0.5f;
    [SerializeField] private float peekOut = 0.12f;
    [SerializeField] private float peekHold = 1.2f;
    [SerializeField] private float focusFreeze = 0.6f;
    [SerializeField] private float loopRadius = 0.12f;
    [SerializeField] private float loopDuration = 0.7f;
    [SerializeField] private float sleepBreath = 0.004f;


    [Header("Happiness")]
    [SerializeField] [Range(0.0f, 1.0f)] private float baseHappiness = 0.4f;
    [SerializeField] private float flowerBonus = 0.3f;
    [SerializeField] [Min(1)] private int flowersForFull = 4;
    [SerializeField] private float varietyBonus = 0.1f;
    [SerializeField] private float homeBonus = 0.15f;
    [SerializeField] private float companyBonus = 0.05f;
    [SerializeField] private string noFlowersTip = "No flowers to sip nearby";
    [SerializeField] private string oneKindTip = "Only one kind of flower to visit";
    [SerializeField] private string noHomeTip = "Has no hive to go home to";
    [SerializeField] private string floodedTip = "Its hive is under the sea";
    [SerializeField] private string lonelyTip = "The only bee in its hive";
    [SerializeField] private string homeAtNightLine = "Tucked up in the hive";
    [SerializeField] private string[] happyLines =
    {
        "Humming a little tune",
        "Dusted in pollen",
        "Busy, busy, busy",
    };


    [Header("Sound")]
    [SerializeField] private SoundObject buzzSound;
    [SerializeField] private float buzzGap = 0.3f;                         // seconds between any two bees' buzzes


    private TerrainManager terrain;
    private Camera view;
    private Vector3 cameraRight;
    private Vector3 cameraForward;
    private Transform sun;
    private Vector2 mapMin;
    private Vector2 mapMax;

    // traits, from the guid
    private uint seed;
    private float size = 1.0f;
    private float speedScale = 1.0f;
    private float phase;
    private float wakeAt;
    private float homeAt;
    private float cruise;
    private float buzzPitch = 1.0f;
    private bool leftHanded;
    private Color tint = Color.white;

    private BeeState state = BeeState.Waiting;
    private float stateTime;
    private float stateDuration;

    // where it is, before the display offsets (wobble, vibration, dips, loop, hop)
    private Vector3 pos;
    private float yVelocity;
    private Vector3 heading = Vector3.right;
    private bool moving;

    private BeeFacing facing = BeeFacing.Right;
    private float frameClock;

    // home, looked up again whenever the cached one is gone (a replace makes a new object)
    private Beehive home;

    // flying
    private Vector3 flyTarget;
    private bool flyingHome;
    private bool flyingToSleep;
    private bool flyingToFlower;

    // foraging
    private EntityId flower;
    private EntityId lastFlower;
    private GameItem lastKind;
    private int visitsLeft;
    private int visitsThisTrip;

    // moving between two points over stateDuration (emerge, enter, peek)
    private Vector3 moveFrom;
    private Vector3 moveTo;

    // the dance
    private Vector2 danceDirection;
    private float danceRun;
    private int circuitsLeft;
    private bool waggling;
    private float returnSide = 1.0f;
    private Vector3 danceCentre;

    // milling
    private Vector2 millAt;
    private Vector2 millTo;
    private float millWait;

    // peeking: 0 out, 1 holding, 2 back in
    private int peekStage;

    private float focusTimer;
    private float loopTime = -1.0f;
    private float alpha = 1.0f;

    private int happyLineIndex = -1;

    private readonly List<EntityId> scratchIds = new List<EntityId>();


    // MARK: read-only state


    public BeeState State { get { return state; } }


    public bool IsHome { get { return state == BeeState.InHive || state == BeeState.Peek; } }


    // MARK: lifecycle


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lastBuzzTime = float.NegativeInfinity;
    }


    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) Debug.LogError("BeeController: no SpriteRenderer, so the bee cannot be seen or turn", this);

        if (sideFrames == null || sideFrames.Length == 0)
        {
            Debug.LogError("BeeController: no side frames, so the bee shows whatever sprite it had when flying sideways", this);
            sideFrames = new Sprite[0];
        }

        if (downSprite == null || upSprite == null)
        {
            Debug.LogError("BeeController: no up or down sprite, so the bee shows a side frame when flying towards or away from the camera", this);
        }

        // hidden until it knows where home is
        if (spriteRenderer != null) spriteRenderer.enabled = false;
    }


    private void Start()
    {
        terrain = TerrainManager.Instance;

        if (terrain == null)
        {
            Debug.LogError("BeeController: no TerrainManager, so the bee cannot find the ground and stays still", this);
            enabled = false;
            return;
        }

        terrain.GetWorldXZBounds(out mapMin, out mapMax);

        FindCameraAxes();
        FindSun();

        // here and not in Awake: a loaded bee's guid is only assigned after Awake, and reading it any
        // earlier would mint a new one and lose the saved identity
        ApplyTraits();

        pos = transform.position;
        frameClock = phase * 10.0f;

        SetFacing(leftHanded ? BeeFacing.Left : BeeFacing.Right);
        state = BeeState.Waiting;
    }


    private void Update()
    {
        float dt = Time.deltaTime;   // scaled, so a paused game freezes the bee
        if (dt <= 0.0f) return;

        stateTime += dt;
        moving = false;

        if (focusTimer > 0.0f)
        {
            focusTimer -= dt;
        }
        else
        {
            switch (state)
            {
                case BeeState.Waiting: UpdateWaiting(); break;
                case BeeState.InHive: UpdateInHive(dt); break;
                case BeeState.Emerge: UpdateEmerge(); break;
                case BeeState.Fly: UpdateFly(dt); break;
                case BeeState.Drink: UpdateDrink(); break;
                case BeeState.Dance: UpdateDance(dt); break;
                case BeeState.Enter: UpdateEnter(); break;
                case BeeState.Peek: UpdatePeek(); break;
                case BeeState.Sleep: UpdateSleep(); break;
            }
        }

        if (state != BeeState.Sleep) frameClock += dt * framesPerSecond;

        ShowFrame();
        ApplyPosition();
    }


    // MARK: deciding


    // the world has to have loaded its entities before home can be looked up: guessing early would
    // re-home a bee whose hive simply hasn't spawned yet
    private void UpdateWaiting()
    {
        WorldEvents events = WorldEvents.Instance;
        if (events != null && !events.IsReady) return;

        Beehive hive = Home();

        // a bee loaded by its exit was inside; one just spawned there is about to come out
        if (hive != null && !hive.IsFlooded && Vector3.Distance(pos, hive.ExitPosition) < 0.6f)
        {
            BeginInHive(Random.Range(0.0f, 1.5f));
            return;
        }

        ShowSprite(true);
        yVelocity = 0.0f;

        if (WantsOut()) BeginTrip(false);
        else GoHome();
    }


    // a new trip from wherever it is: a few flowers, then home
    private void BeginTrip(bool fromHive)
    {
        visitsLeft = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(visitsPerTrip.x, visitsPerTrip.y, Trait01(SALT_VISITS))));
        visitsThisTrip = 0;

        NextFlower(fromHive);
    }


    private void NextFlower(bool fromHive)
    {
        if (!WantsOut() || visitsLeft <= 0)
        {
            GoHome();
            return;
        }

        if (PickFlower(fromHive, out EntityId picked, out Vector3 point))
        {
            flower = picked;
            BeginFly(point, false, false, true);
            return;
        }

        // nothing in bloom within reach: drift about near home, then try again
        flower = null;
        visitsLeft--;
        BeginFly(WanderPoint(), false, false, false);
    }


    // home to dance and go in; a bee with no hive to go to finds a flower to sleep on instead
    private void GoHome()
    {
        flower = null;

        Beehive hive = Home();

        if (hive == null || hive.IsFlooded)
        {
            if (WantsOut())
            {
                BeginTrip(false);
                return;
            }

            if (PickFlower(false, out EntityId bed, out Vector3 point))
            {
                flower = bed;
                BeginFly(point, false, true, true);
                return;
            }

            BeginSleep();
            return;
        }

        BeginFly(DancePoint(hive), true, false, false);
    }


    // MARK: states


    private void BeginInHive(float rest)
    {
        state = BeeState.InHive;
        stateTime = 0.0f;
        stateDuration = rest;

        ShowSprite(false);

        // low in the middle, near where the exit usually is, so a starred bee's glow doesn't jump
        millAt = new Vector2(0.5f, 0.2f);
        millTo = millAt;
        millWait = RandomIn(millPause);
    }


    // milling about inside, slowly, in the sprite's own plane; out again when the rest is over and
    // it's still day
    private void UpdateInHive(float dt)
    {
        Beehive hive = Home();

        if (hive == null)
        {
            // the hive became something else while it was inside: out it comes, and finds another
            BeginEmerge(pos);
            return;
        }

        if (hive.IsFlooded)
        {
            BeginEmerge(hive.ExitPosition);
            return;
        }

        Vector2 toward = millTo - millAt;

        if (toward.sqrMagnitude < 0.0001f)
        {
            millWait -= dt;

            if (millWait <= 0.0f)
            {
                millTo = new Vector2(Random.value, Random.value);
                millWait = RandomIn(millPause);
            }
        }
        else
        {
            millAt = Vector2.MoveTowards(millAt, millTo, millSpeed * dt);
            FaceScreen(toward.x, toward.y * 0.5f);
            moving = true;
        }

        pos = hive.InsidePoint(millAt, HalfWidth());

        if (stateTime >= stateDuration && WantsOut()) BeginEmerge(hive.ExitPosition);
    }


    private void BeginEmerge(Vector3 from)
    {
        state = BeeState.Emerge;
        stateTime = 0.0f;
        stateDuration = emergeDuration;

        moveFrom = from;
        moveTo = from - cameraForward * emergeOut + Vector3.up * 0.12f;

        pos = moveFrom;
        alpha = 0.0f;
        ShowSprite(true);
        SetFacing(BeeFacing.Down);

        Beehive hive = Home();
        if (hive != null) hive.Rustle();

        Buzz();
    }


    private void UpdateEmerge()
    {
        float t = Mathf.Clamp01(stateTime / stateDuration);

        pos = Vector3.Lerp(moveFrom, moveTo, 1.0f - (1.0f - t) * (1.0f - t));
        alpha = Mathf.Clamp01(stateTime / fadeDuration);
        moving = true;

        if (t < 1.0f) return;

        alpha = 1.0f;
        yVelocity = 0.0f;

        // the hive may be gone (it came out because of that): GoHome finds another, or a flower to sleep on
        Beehive hive = Home();

        if (WantsOut() && hive != null && !hive.IsFlooded) BeginTrip(true);
        else GoHome();
    }


    private void BeginFly(Vector3 target, bool home, bool toSleep, bool toFlower)
    {
        state = BeeState.Fly;
        stateTime = 0.0f;

        flyTarget = ClampToMap(target);
        flyingHome = home;
        flyingToSleep = toSleep;
        flyingToFlower = toFlower;

        ShowSprite(true);
    }


    private void UpdateFly(float dt)
    {
        // the day ended under it: whatever it was doing, home
        if (!flyingHome && !flyingToSleep && !WantsOut())
        {
            GoHome();
            return;
        }

        if (flyingHome)
        {
            Beehive hive = Home();

            // its hive went while it was on the way
            if (hive == null || hive.IsFlooded)
            {
                GoHome();
                return;
            }

            flyTarget = DancePoint(hive);
        }

        // the flower it was going to went (a growth, a reaction). a destroyed EntityId compares null
        if (flyingToFlower && flower == null)
        {
            if (flyingToSleep) GoHome();
            else NextFlower(false);

            return;
        }

        if (!FlyTowards(flyTarget, dt)) return;

        if (flyingHome)
        {
            Beehive hive = Home();

            if (visitsThisTrip > 0 && lastFlower != null && Late() < 0.97f) BeginDance(hive);
            else BeginEnter(hive);

            return;
        }

        if (flyingToSleep)
        {
            BeginSleep();
            return;
        }

        if (flower != null)
        {
            BeginDrink();
            return;
        }

        // arrived at a wander point
        NextFlower(false);
    }


    // Moves pos towards the target: flat at its speed, while the height floats at cruise over whatever
    // is under it and ahead, and comes down onto the target over the last descentDistance. True once there
    private bool FlyTowards(Vector3 target, float dt)
    {
        Vector3 flat = target - pos;
        flat.y = 0.0f;
        float distance = flat.magnitude;

        if (distance > 0.0001f)
        {
            heading = flat / distance;

            float step = Mathf.Min(distance, Speed() * dt);
            pos += heading * step;
            distance -= step;

            FaceMovement(heading);
            moving = true;
        }

        if (distance > arriveDistance)
        {
            float cruiseY = CruiseY(pos, heading);
            float wanted = Mathf.Lerp(target.y, cruiseY, Mathf.Clamp01(distance / descentDistance));

            // never into a cliff on the way down
            if (distance > descentDistance * 0.5f) wanted = Mathf.Max(wanted, GroundY(pos) + cruise * 0.3f);

            pos.y = Mathf.SmoothDamp(pos.y, wanted, ref yVelocity, climbSmoothTime, Mathf.Infinity, dt);
            return false;
        }

        // over it: settle onto its height
        yVelocity = 0.0f;
        pos.y = Mathf.MoveTowards(pos.y, target.y, 1.5f * dt);
        moving = true;

        return Mathf.Abs(pos.y - target.y) < 0.005f;
    }


    private void BeginDrink()
    {
        state = BeeState.Drink;
        stateTime = 0.0f;
        stateDuration = RandomIn(drinkDuration);

        TurnSide(flower != null ? ScreenX(flower.transform.position - pos) : 0.0f);
        RustleFlower(flower);
    }


    // hovering, with a dip or two into the flower
    private void UpdateDrink()
    {
        if (flower == null)
        {
            // gone from under it
            FinishVisit();
            return;
        }

        if (stateTime < stateDuration) return;

        FinishVisit();
    }


    private void FinishVisit()
    {
        if (flower != null)
        {
            lastFlower = flower;
            lastKind = KindOf(flower);
        }

        visitsLeft--;
        visitsThisTrip++;
        flower = null;

        Buzz();
        NextFlower(false);
    }


    private void BeginDance(Beehive hive)
    {
        state = BeeState.Dance;
        stateTime = 0.0f;

        danceCentre = DancePoint(hive);

        Vector3 offset = lastFlower.transform.position - hive.ExitPosition;
        offset.y = 0.0f;

        danceDirection = DanceDirection(offset);
        danceRun = Mathf.Lerp(waggleRunLength.x, waggleRunLength.y, Mathf.Clamp01(offset.magnitude / Mathf.Max(0.01f, forageRadius)));

        circuitsLeft = Late() > 0.9f ? 1 : Random.Range(danceCircuits.x, danceCircuits.y + 1);
        returnSide = leftHanded ? -1.0f : 1.0f;

        StartWaggle();
    }


    private void StartWaggle()
    {
        waggling = true;
        stateTime = 0.0f;
        stateDuration = danceRun / Mathf.Max(0.01f, waggleRunSpeed);

        FaceScreen(danceDirection.x, danceDirection.y);
        Buzz();
    }


    // A waggle run along the dance direction, shaking across it, then a half loop back to its start,
    // alternating sides, as real bees do. In the sprite's own plane
    private void UpdateDance(float dt)
    {
        Vector3 along = PlaneVector(danceDirection);
        Vector3 across = PlaneVector(new Vector2(-danceDirection.y, danceDirection.x));

        float half = danceRun * 0.5f;
        float t = Mathf.Clamp01(stateTime / stateDuration);

        moving = true;

        if (waggling)
        {
            float shake = Mathf.Sin(stateTime * waggleFrequency * TAU) * waggleAmplitude;
            pos = danceCentre + along * Mathf.Lerp(-half, half, t) + across * shake;

            if (t < 1.0f) return;

            waggling = false;
            stateTime = 0.0f;
            stateDuration = Mathf.PI * half / Mathf.Max(0.01f, returnSpeed);
            return;
        }

        // the loop back: from the run's end round to its start
        float angle = t * Mathf.PI;
        pos = danceCentre + along * (Mathf.Cos(angle) * half) + across * (Mathf.Sin(angle) * half * returnSide);

        float dx = Vector3.Dot(-along * Mathf.Sin(angle) + across * (Mathf.Cos(angle) * returnSide), cameraRight);
        FaceScreen(dx, 0.0f);

        if (t < 1.0f) return;

        returnSide = -returnSide;
        circuitsLeft--;

        if (circuitsLeft > 0)
        {
            StartWaggle();
            return;
        }

        Beehive hive = Home();
        if (hive != null && lastFlower != null) hive.Advertise(lastFlower);

        BeginEnter(hive);
    }


    private void BeginEnter(Beehive hive)
    {
        if (hive == null)
        {
            GoHome();
            return;
        }

        state = BeeState.Enter;
        stateTime = 0.0f;
        stateDuration = enterDuration;

        moveFrom = pos;
        moveTo = hive.ExitPosition;

        SetFacing(BeeFacing.Up);
    }


    private void UpdateEnter()
    {
        Beehive hive = Home();

        if (hive == null || hive.IsFlooded)
        {
            alpha = 1.0f;
            GoHome();
            return;
        }

        moveTo = hive.ExitPosition;

        float t = Mathf.Clamp01(stateTime / stateDuration);
        pos = Vector3.Lerp(moveFrom, moveTo, t * t);
        alpha = 1.0f - Mathf.Clamp01((stateTime - (stateDuration - fadeDuration)) / fadeDuration);
        moving = true;

        if (t < 1.0f) return;

        alpha = 1.0f;
        hive.Rustle();

        BeginInHive(RandomIn(hiveRest));
    }


    // clicked while inside: out to the doorstep for a look, then back in
    private void BeginPeek()
    {
        Beehive hive = Home();
        if (hive == null) return;

        state = BeeState.Peek;
        peekStage = 0;
        stateTime = 0.0f;
        stateDuration = 0.35f;

        moveFrom = pos;
        moveTo = hive.ExitPosition - cameraForward * peekOut;

        alpha = 0.0f;
        ShowSprite(true);
        SetFacing(BeeFacing.Down);
        hive.Rustle();
    }


    private void UpdatePeek()
    {
        Beehive hive = Home();

        if (hive == null)
        {
            BeginEmerge(pos);
            return;
        }

        float t = Mathf.Clamp01(stateTime / stateDuration);

        if (peekStage == 0)
        {
            pos = Vector3.Lerp(moveFrom, moveTo, 1.0f - (1.0f - t) * (1.0f - t));
            alpha = Mathf.Clamp01(stateTime / fadeDuration);
            moving = true;

            if (t < 1.0f) return;

            peekStage = 1;
            stateTime = 0.0f;
            stateDuration = peekHold;
            TurnSide(0.0f);
            return;
        }

        if (peekStage == 1)
        {
            alpha = 1.0f;
            if (t < 1.0f) return;

            peekStage = 2;
            stateTime = 0.0f;
            stateDuration = 0.35f;
            moveFrom = pos;
            SetFacing(BeeFacing.Up);
            return;
        }

        pos = Vector3.Lerp(moveFrom, hive.ExitPosition, t * t);
        alpha = 1.0f - t;
        moving = true;

        if (t < 1.0f) return;

        alpha = 1.0f;
        hive.Rustle();

        // straight back to milling, keeping what's left of its rest
        float rest = RandomIn(hiveRest) * 0.5f;
        BeginInHive(rest);
    }


    // a bee with no hive, on a flower (or wherever it had got to) for the night
    private void BeginSleep()
    {
        state = BeeState.Sleep;
        stateTime = 0.0f;

        ShowSprite(true);
        TurnSide(0.0f);
        frameClock = 0.0f;
    }


    private void UpdateSleep()
    {
        // morning: off foraging, and home tonight if a hive with room has turned up
        if (WantsOut()) BeginTrip(false);
    }


    // MARK: flowers


    // A flower from the list within reach of home (or of itself, with no home), not under the sea.
    // Nearer is likelier; the one just visited is out, the same kind as the last is less likely, and
    // the one last danced for at its hive pulls the first visit of a trip. Reservoir pick, so no list
    private bool PickFlower(bool fromHive, out EntityId picked, out Vector3 point)
    {
        picked = null;
        point = default;

        EntityManager entities = EntityManager.Instance;
        if (entities == null || flowerItems == null) return false;

        Beehive hive = Home();
        Vector3 centre = hive != null ? hive.ExitPosition : pos;
        float seaY = SeaSurfaceY();

        EntityId advertised = null;
        if (fromHive && hive != null) hive.TryGetAdvertised(out advertised);

        float total = 0.0f;

        for (int i = 0; i < flowerItems.Length; i++)
        {
            GameItem kind = flowerItems[i];
            if (kind == null) continue;

            entities.CollectEntitiesOf(kind, scratchIds);

            for (int j = 0; j < scratchIds.Count; j++)
            {
                EntityId candidate = scratchIds[j];
                if (candidate == null || candidate == lastFlower) continue;

                Vector3 at = candidate.transform.position;
                if (at.y < seaY) continue;

                float dx = at.x - centre.x;
                float dz = at.z - centre.z;
                if (dx * dx + dz * dz > forageRadius * forageRadius) continue;

                float fromHere = Vector3.Distance(at, pos);
                float weight = 1.0f / (1.0f + fromHere);

                if (kind == lastKind) weight *= sameKindWeight;
                if (candidate == advertised) weight *= recruitWeight;

                total += weight;
                if (Random.value * total > weight) continue;

                picked = candidate;
            }
        }

        if (picked == null) return false;

        point = DrinkPoint(picked);
        return true;
    }


    // at a blossom, a touch towards the camera so it draws in front of the flower
    private Vector3 DrinkPoint(EntityId target)
    {
        return NectarPoint(target) + Vector3.up * drinkHeight - cameraForward * drinkInFront;
    }


    // An authored nectar point if the flower has one, else somewhere in the blossom part of its sprite.
    // The sprite's box comes from its pixel rect and pivot, as AnimalGlow measures it, rather than
    // renderer bounds, so a stray pixel at the top of the cell can't lift it
    private Vector3 NectarPoint(EntityId target)
    {
        SpriteRenderer drawn = target.GetComponentInChildren<SpriteRenderer>();

        if (TryPickAuthoredNectar(target.transform, out Vector3 authored)) return authored;
        if (drawn != null && drawn.transform != target.transform && TryPickAuthoredNectar(drawn.transform, out authored)) return authored;

        if (drawn == null || drawn.sprite == null) return target.transform.position;

        Sprite sprite = drawn.sprite;
        float pixelsPerUnit = sprite.pixelsPerUnit;
        Vector2 min = -sprite.pivot / pixelsPerUnit;
        Vector2 size = sprite.rect.size / pixelsPerUnit;

        float x = min.x + size.x * RandomIn(nectarAcross);
        float y = min.y + size.y * RandomIn(nectarUp);

        return drawn.transform.TransformPoint(new Vector3(x, y, 0.0f));
    }


    // a random one of the direct children named "Nectar...", picked without keeping a list
    private static bool TryPickAuthoredNectar(Transform parent, out Vector3 point)
    {
        point = default;
        int seen = 0;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (!child.name.StartsWith("Nectar", System.StringComparison.Ordinal)) continue;

            seen++;
            if (Random.Range(0, seen) == 0) point = child.position;
        }

        return seen > 0;
    }


    private GameItem KindOf(EntityId target)
    {
        EntityManager entities = EntityManager.Instance;
        if (entities == null || target == null) return null;

        return entities.TryGetLive(target, out EntityManager.LiveEntity entity) ? entity.item : null;
    }


    // the flower's sprite child gives a little shake. the flower itself is never changed
    private static void RustleFlower(EntityId target, float angle, float duration)
    {
        if (target == null) return;

        SpriteRenderer drawn = target.GetComponentInChildren<SpriteRenderer>();
        if (drawn == null) return;

        Transform shaken = drawn.transform;
        if (shaken == target.transform) return;

        // two bees at one flower: finish the last shake first, so it springs back to where it rests
        shaken.DOComplete();
        shaken.DOPunchRotation(new Vector3(0.0f, 0.0f, Random.value < 0.5f ? angle : -angle), duration, 6, 0.5f)
            .SetLink(target.gameObject);
    }


    private void RustleFlower(EntityId target)
    {
        RustleFlower(target, flowerRustleAngle, flowerRustleDuration);
    }


    // somewhere near home to drift to when nothing is in bloom
    private Vector3 WanderPoint()
    {
        Beehive hive = Home();
        Vector3 centre = hive != null ? hive.ExitPosition : pos;

        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        Vector3 point = ClampToMap(new Vector3(centre.x + offset.x, 0.0f, centre.z + offset.y));
        point.y = CruiseY(point, Vector3.zero);

        return point;
    }


    // MARK: home


    // its hive, if it still has one. Adopts the nearest with room when its own is gone (not when it is
    // only flooded: it keeps that one, so the count stays honest)
    private Beehive Home()
    {
        string guid = HomeGuid;

        if (home != null && home.Guid == guid) return home;

        home = Beehive.Find(guid);
        if (home != null) return home;

        // before the world is ready its hive may simply not have spawned yet
        WorldEvents events = WorldEvents.Instance;
        if (events != null && !events.IsReady) return null;

        home = FindHiveToAdopt();
        if (home != null) HomeGuid = home.Guid;

        return home;
    }


    private Beehive FindHiveToAdopt()
    {
        Beehive best = null;
        float bestSqr = forageRadius * forageRadius * 4.0f;

        IReadOnlyList<Beehive> hives = Beehive.All;

        for (int i = 0; i < hives.Count; i++)
        {
            Beehive hive = hives[i];
            if (hive == null || string.IsNullOrEmpty(hive.Guid) || hive.IsFlooded || !hive.HasRoom) continue;

            float sqr = (hive.ExitPosition - pos).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            best = hive;
            bestSqr = sqr;
        }

        return best;
    }


    // in front of the exit, where it dances and from where it goes in
    private Vector3 DancePoint(Beehive hive)
    {
        return hive.ExitPosition - cameraForward * danceInFront + Vector3.up * danceAbove;
    }


    // The waggle run's direction in the sprite's plane (x across, y up). As the player sees it: up the
    // screen is away from the camera. From the sun: the angle from straight up is the flower's bearing
    // clockwise from the sun's, as a real bee dances it on the comb
    private Vector2 DanceDirection(Vector3 offset)
    {
        if (offset.sqrMagnitude < 0.0001f) return Vector2.up;

        if (danceFromSun && sun != null)
        {
            Vector3 toSun = -sun.forward;
            toSun.y = 0.0f;

            if (toSun.sqrMagnitude > 0.0001f)
            {
                float angle = Vector3.SignedAngle(toSun, offset, Vector3.up) * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            }
        }

        Vector2 screen = new Vector2(Vector3.Dot(offset, cameraRight), Vector3.Dot(offset, cameraForward));
        return screen.normalized;
    }


    // a direction in the sprite's own plane, from its right and up, read live since the billboard sets
    // them in its Start
    private Vector3 PlaneVector(Vector2 direction)
    {
        return transform.right * direction.x + transform.up * direction.y;
    }


    // MARK: the day


    // 0 at sunrise, 1 at sunset; above 1 all night. Read live, so it follows any day length
    private float Late()
    {
        WorldStateManager world = WorldStateManager.Instance;
        if (world == null || world.DayDuration <= 0.0f) return 0.5f;

        if (world.CycleTime >= world.DayDuration) return 2.0f;

        return world.CycleTime / world.DayDuration;
    }


    // between its own waking and home times
    private bool WantsOut()
    {
        float late = Late();
        return late >= wakeAt && late < homeAt;
    }


    private float Speed()
    {
        float speed = Late() >= hurryFrom ? hurrySpeed : flySpeed;
        return speed * speedScale;
    }


    // MARK: ground


    // the ground under a point, or the risen sea over it
    private float GroundY(Vector3 point)
    {
        float y = float.NegativeInfinity;

        int top = terrain.GetTopLevel(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.z));
        if (top >= 0) y = terrain.GetLevelHeight(top);

        y = Mathf.Max(y, SeaSurfaceY());

        // off the terrain and no sea: hold its height
        return float.IsNegativeInfinity(y) ? point.y - cruise : y;
    }


    // cruise height over here and a little ahead, so it climbs before a cliff rather than into it
    private float CruiseY(Vector3 point, Vector3 direction)
    {
        float ground = GroundY(point);
        if (direction.sqrMagnitude > 0.0f) ground = Mathf.Max(ground, GroundY(point + direction * lookAhead));

        return ground + cruise;
    }


    private Vector3 ClampToMap(Vector3 point)
    {
        point.x = Mathf.Clamp(point.x, mapMin.x + mapMargin, mapMax.x - mapMargin);
        point.z = Mathf.Clamp(point.z, mapMin.y + mapMargin, mapMax.y - mapMargin);
        return point;
    }


    // the sea's surface only counts once it has risen; before that it waits below the terrain
    private static float SeaSurfaceY()
    {
        SeaLevel sea = SeaLevel.Instance;
        if (sea == null || !sea.IsRisen) return float.NegativeInfinity;

        return sea.SurfaceY;
    }


    // MARK: facing and frames


    private void FindCameraAxes()
    {
        view = Camera.main;

        if (view == null)
        {
            Debug.LogError("BeeController: no camera tagged MainCamera, so the bee faces as if the camera looked along +Z", this);
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


    private float ScreenX(Vector3 direction)
    {
        return Vector3.Dot(direction, cameraRight);
    }


    private void FaceMovement(Vector3 direction)
    {
        direction.y = 0.0f;
        if (direction.sqrMagnitude < 0.000001f) return;

        direction.Normalize();

        FaceScreen(Vector3.Dot(direction, cameraRight), Vector3.Dot(direction, cameraForward));
    }


    // The closest of the four screen directions, with the sides favoured a little and a margin
    // before it changes its mind, so a diagonal doesn't flicker between two sprites
    private void FaceScreen(float x, float y)
    {
        float length = Mathf.Sqrt(x * x + y * y);
        if (length < 0.000001f) return;

        float sx = x / length * sideBias;
        float sy = y / length;

        BeeFacing best = BeeFacing.Right;

        for (int i = 0; i < 4; i++)
        {
            if (FacingScore((BeeFacing)i, sx, sy) > FacingScore(best, sx, sy)) best = (BeeFacing)i;
        }

        if (best == facing) return;
        if (FacingScore(best, sx, sy) < FacingScore(facing, sx, sy) + facingHysteresis) return;

        SetFacing(best);
    }


    private static float FacingScore(BeeFacing candidate, float sx, float sy)
    {
        switch (candidate)
        {
            case BeeFacing.Right: return sx;
            case BeeFacing.Left: return -sx;
            case BeeFacing.Up: return sy;
            default: return -sy;
        }
    }


    // never left hovering on the up or down sprite: turn to whichever side the hint leans, else stay on
    // the side it's on, else its own favourite side
    private void TurnSide(float screenX)
    {
        if (screenX > 0.05f)
        {
            SetFacing(BeeFacing.Right);
            return;
        }

        if (screenX < -0.05f)
        {
            SetFacing(BeeFacing.Left);
            return;
        }

        if (facing == BeeFacing.Up || facing == BeeFacing.Down) SetFacing(leftHanded ? BeeFacing.Left : BeeFacing.Right);
    }


    private void SetFacing(BeeFacing newFacing)
    {
        facing = newFacing;
        ShowFrame();
    }


    private void ShowFrame()
    {
        if (spriteRenderer == null) return;

        if (facing == BeeFacing.Up && upSprite != null)
        {
            spriteRenderer.sprite = upSprite;
            spriteRenderer.flipX = false;
            return;
        }

        if (facing == BeeFacing.Down && downSprite != null)
        {
            spriteRenderer.sprite = downSprite;
            spriteRenderer.flipX = false;
            return;
        }

        if (sideFrames.Length == 0) return;

        int index = Mathf.FloorToInt(frameClock) % sideFrames.Length;
        if (index < 0) index += sideFrames.Length;

        if (sideFrames[index] != null) spriteRenderer.sprite = sideFrames[index];

        // drawn pointing right
        spriteRenderer.flipX = facing == BeeFacing.Left;
    }


    // on or off for being inside; the glow keeps showing a starred one either way
    private void ShowSprite(bool shown)
    {
        if (spriteRenderer != null) spriteRenderer.enabled = shown;
    }


    // MARK: position


    // pos plus everything that only shows: the path wobble, the vibration on the up and down sprites,
    // the drinking dips, the sleeping breath, a loop-the-loop and a hop
    private void ApplyPosition()
    {
        float time = Time.time;
        Vector3 offset = Vector3.zero;

        if (state == BeeState.Fly)
        {
            float fade = Mathf.Clamp01(Vector3.Distance(pos, flyTarget) / 0.4f);
            Vector3 side = Vector3.Cross(Vector3.up, heading);
            float wobble = Mathf.Sin((time * wobbleFrequency + phase) * TAU);
            float lift = Mathf.Sin((time * wobbleFrequency * 1.7f + phase * 3.0f) * TAU);

            offset += (side * wobble + Vector3.up * (lift * 0.3f)) * (wobbleAmplitude * fade);
        }

        if (moving && (facing == BeeFacing.Up || facing == BeeFacing.Down))
        {
            float across = Mathf.Sin((time * vibrateFrequency + phase) * TAU) * vibrateAmplitude.x;
            float up = Mathf.Sin((time * vibrateFrequency * 1.3f + phase + 0.25f) * TAU) * vibrateAmplitude.y;

            offset += transform.right * across + transform.up * up;
        }

        if (state == BeeState.Drink && stateDuration > 0.0f)
        {
            float dip = Mathf.Sin(Mathf.Clamp01(stateTime / stateDuration) * dipsPerDrink * Mathf.PI);
            offset -= Vector3.up * (dip * dip * dipDepth);
        }

        if (state == BeeState.Sleep) offset += Vector3.up * (Mathf.Sin((time * 0.25f + phase) * TAU) * sleepBreath);

        if (loopTime >= 0.0f)
        {
            loopTime += Time.deltaTime;
            float angle = Mathf.Clamp01(loopTime / loopDuration) * TAU;
            float direction = facing == BeeFacing.Left ? -1.0f : 1.0f;

            offset += transform.right * (Mathf.Sin(angle) * loopRadius * direction) + transform.up * ((1.0f - Mathf.Cos(angle)) * loopRadius);

            if (loopTime >= loopDuration) loopTime = -1.0f;
        }

        transform.position = pos + offset + ReactionOffset;

        if (spriteRenderer != null)
        {
            Color shown = tint;
            shown.a = alpha;
            spriteRenderer.color = shown;
        }
    }


    // half the sprite's drawn width, so milling keeps the whole bee inside the hive
    private float HalfWidth()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null) return 0.0f;

        return spriteRenderer.sprite.bounds.extents.x * Mathf.Abs(transform.lossyScale.x);
    }


    // MARK: voice


    // shared between every bee, and only for one that can be seen, so voices are never used up
    private void Buzz()
    {
        if (buzzSound == null || AudioManager.Instance == null) return;
        if (spriteRenderer == null || !spriteRenderer.enabled) return;
        if (Time.time - lastBuzzTime < buzzGap) return;
        if (view != null && !IsOnScreen(transform.position)) return;

        lastBuzzTime = Time.time;

        AudioManager.Instance.PlaySFX(buzzSound, transform.position, buzzPitch, 1.0f);
    }


    private bool IsOnScreen(Vector3 point)
    {
        Vector3 viewport = view.WorldToViewportPoint(point);

        return viewport.z > 0.0f && viewport.x >= 0.0f && viewport.x <= 1.0f && viewport.y >= 0.0f && viewport.y <= 1.0f;
    }


    // MARK: traits


    private void ApplyTraits()
    {
        EntityId id = EntityId != null ? EntityId : GetComponent<EntityId>();
        seed = EntitySeed.FromGuid(id != null ? id.Guid : name);

        size = Mathf.Lerp(sizeRange.x, sizeRange.y, Trait01(SALT_SIZE));

        // before anything can hover it, so the hover grow rests on the grown size
        transform.localScale *= size;

        float temperature = Trait01(SALT_TEMPERATURE) * 2.0f - 1.0f;
        tint = temperature >= 0.0f
            ? Color.Lerp(Color.white, warmTint, temperature)
            : Color.Lerp(Color.white, coolTint, -temperature);
        tint *= Mathf.Lerp(brightnessRange.x, brightnessRange.y, Trait01(SALT_BRIGHTNESS));
        tint.a = 1.0f;

        speedScale = 1.0f + (Trait01(SALT_SPEED) * 2.0f - 1.0f) * speedVariation;
        phase = Trait01(SALT_PHASE);
        wakeAt = Mathf.Lerp(wakeRange.x, wakeRange.y, Trait01(SALT_WAKE));
        homeAt = Mathf.Lerp(homeRange.x, homeRange.y, Trait01(SALT_HOME));
        cruise = cruiseHeight * (1.0f + (Trait01(SALT_CRUISE) * 2.0f - 1.0f) * cruiseVariation);
        leftHanded = Trait01(SALT_HANDEDNESS) < 0.5f;

        // smaller bees buzz higher
        buzzPitch = 1.0f / Mathf.Sqrt(Mathf.Max(0.1f, size));
    }


    private float Trait01(uint salt)
    {
        return EntitySeed.Trait01(seed, salt);
    }


    // MARK: reactions


    // inside, it peeks out of the exit; out, it stops and turns side-on for a moment
    public override void OnFocused()
    {
        base.OnFocused();
        happyLineIndex = -1;

        if (state == BeeState.InHive)
        {
            BeginPeek();
            return;
        }

        if (state == BeeState.Fly || state == BeeState.Drink)
        {
            focusTimer = focusFreeze;
            TurnSide(0.0f);
        }
    }


    // a loop-the-loop in its own plane, with a buzz
    public override void OnRenamed()
    {
        base.OnRenamed();

        loopTime = 0.0f;
        Buzz();
    }


    // MARK: happiness


    public override float Happiness
    {
        get
        {
            if (terrain == null) return 1.0f;

            float happiness = baseHappiness;

            int flowers = CountFlowers(out int kinds);
            happiness += flowerBonus * Mathf.Clamp01((float)flowers / flowersForFull);

            // variety only counts if there is more than one kind it could have
            if (kinds >= 2 || KindsListed() < 2) happiness += varietyBonus;

            Beehive hive = Home();
            if (hive != null && !hive.IsFlooded) happiness += homeBonus;

            if (!CanHaveCompany() || HasHiveMates(hive)) happiness += companyBonus;

            return Mathf.Clamp01(happiness);
        }
    }


    public override string[] GetHappinessTips()
    {
        if (terrain == null) return base.GetHappinessTips();

        int flowers = CountFlowers(out int kinds);
        Beehive hive = Home();

        bool noFlowers = flowers == 0;
        bool oneKind = !noFlowers && kinds < 2 && KindsListed() >= 2;
        bool homeless = hive == null;
        bool flooded = hive != null && hive.IsFlooded;
        bool lonely = hive != null && CanHaveCompany() && !HasHiveMates(hive);

        int count = (noFlowers ? 1 : 0) + (oneKind ? 1 : 0) + (homeless ? 1 : 0) + (flooded ? 1 : 0) + (lonely ? 1 : 0);
        if (count == 0) return base.GetHappinessTips();

        // happy again later is a new mood, so a new line
        happyLineIndex = -1;

        string[] tips = new string[count];
        int at = 0;

        if (homeless) tips[at++] = noHomeTip;
        if (flooded) tips[at++] = floodedTip;
        if (noFlowers) tips[at++] = noFlowersTip;
        if (oneKind) tips[at++] = oneKindTip;
        if (lonely) tips[at++] = lonelyTip;

        return tips;
    }


    public override string GetHappyLine()
    {
        if (IsHome && Late() > 1.0f && !string.IsNullOrEmpty(homeAtNightLine)) return homeAtNightLine;

        if (happyLines == null || happyLines.Length == 0) return null;

        if (happyLineIndex < 0 || happyLineIndex >= happyLines.Length) happyLineIndex = Random.Range(0, happyLines.Length);

        return happyLines[happyLineIndex];
    }


    private bool CanHaveCompany()
    {
        return Animal != null && Animal.MobCap > 1;
    }


    private static bool HasHiveMates(Beehive hive)
    {
        return hive != null && hive.Residents >= 2;
    }


    private int KindsListed()
    {
        if (flowerItems == null) return 0;

        int kinds = 0;

        for (int i = 0; i < flowerItems.Length; i++)
        {
            if (flowerItems[i] != null) kinds++;
        }

        return kinds;
    }


    // the flowers within reach of home (or of itself), and how many kinds they are. only while its
    // panel asks
    private int CountFlowers(out int kinds)
    {
        kinds = 0;

        EntityManager entities = EntityManager.Instance;
        if (entities == null || flowerItems == null) return 0;

        Beehive hive = Home();
        Vector3 centre = hive != null ? hive.ExitPosition : pos;
        float seaY = SeaSurfaceY();

        int count = 0;

        for (int i = 0; i < flowerItems.Length; i++)
        {
            if (flowerItems[i] == null) continue;

            entities.CollectEntitiesOf(flowerItems[i], scratchIds);

            int ofKind = 0;

            for (int j = 0; j < scratchIds.Count; j++)
            {
                if (scratchIds[j] == null) continue;

                Vector3 at = scratchIds[j].transform.position;
                if (at.y < seaY) continue;

                float dx = at.x - centre.x;
                float dz = at.z - centre.z;

                if (dx * dx + dz * dz <= forageRadius * forageRadius) ofKind++;
            }

            if (ofKind > 0) kinds++;
            count += ofKind;
        }

        return count;
    }


    // MARK: internals


    private static float RandomIn(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }
}
