using UnityEngine;


public enum WormState
{
    Crawling,
    EnterBurrow,
    Burrowed,
    LeaveBurrow,
}


// A worm: crawls very slowly over the top surface, drifting towards preferred tiles, then burrows,
// travels a little way underground and resurfaces somewhere else.
//
// Tiles:
//   forbidden  - never entered, crawling or burrowing
//   preferred  - drifted towards, crawling and resurfacing
//   the rest   - allowed but not preferred; worms drift off them
//
// Crawling never changes level: a sprite sliding up a cliff face reads wrong. A burrow may start from
// any tile it can stand on and come up on any level within burrowRadius, which is how worms get past
// cliffs, and off a plateau with nothing preferred on it.
//
// Everything below works on the worm's feet, a point on the top surface. The transform stands above
// them by the sprite's lowest vertex, so the sprite sits on the ground whatever its pivot, and is
// re-measured whenever the sprite changes.
//
// Nothing here is saved. AnimalManager writes the position through every LateUpdate, and a loaded
// worm snaps back onto the surface and starts crawling again.
[RequireComponent(typeof(SpriteRenderer))]
public class WormController : AnimalController
{
    private static readonly Vector2Int[] STEP_OFFSETS =
    {
        new Vector2Int(0, 0),       // staying put is a choice too, so a worm on a preferred tile lingers
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, -1),
    };

    // targets stay this far inside their cell, so a worm never sits right on a tile's edge
    private const float CELL_MARGIN = 0.15f;


    [Header("Tiles")]
    [SerializeField] private TerrainTile[] forbiddenTiles = new TerrainTile[0];
    [SerializeField] private TerrainTile[] preferredTiles = new TerrainTile[0];
    [SerializeField] [Min(1.0f)] private float preferredWeight = 4.0f;


    [Header("Movement")]
    [SerializeField] private float crawlSpeed = 0.08f;                      // world units a second
    [SerializeField] private Vector2 stepPause = new Vector2(0.5f, 2.0f);   // seconds between steps
    [SerializeField] [Min(1)] private int burrowRadius = 3;                 // cells


    [Header("Timing")]
    [SerializeField] private Vector2 crawlDuration = new Vector2(10.0f, 25.0f);
    [SerializeField] private Vector2 burrowDuration = new Vector2(4.0f, 10.0f);
    [SerializeField] private Vector2 flipInterval = new Vector2(0.25f, 0.7f);

    // going into and coming out of a burrow, each: the transition sprite mirrors left to right every
    // digFlipInterval, faster than the crawl wiggle, so it reads as digging
    [SerializeField] private float digDuration = 1.2f;

    // renamed: a quick burst of the wiggle, as if it heard its new name
    [SerializeField] private float renameWiggleDuration = 0.6f;
    [SerializeField] private Vector2 renameWiggleInterval = new Vector2(0.05f, 0.08f);
    [SerializeField] private Vector2 digFlipInterval = new Vector2(0.12f, 0.2f);


    [Header("Look")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite transitionSprite;   // shown going into and coming out of a burrow


    [Header("Happiness")]
    [SerializeField] [Min(1)] private int happinessRadius = 2;          // cells looked at around it, each way
    [SerializeField] [Range(0.0f, 1.0f)] private float baseHappiness = 0.6f;   // worms are almost always pretty happy
    // Worms are simple, and they sort out their own ground by crawling, so the one tip is the one
    // thing that is obviously passing. A tip the player can't or shouldn't act on only sends them
    // off rearranging the world for nothing
    [SerializeField] private string offPreferredTip = "Wriggling off to find softer soil";

    // one of these, picked each time it is looked at, while nothing is bothering it
    [SerializeField] private string[] happyLines =
    {
        "Peristalsis is going swimmingly",
        "Can't see a thing, and loving it",
        "The earth here is soft as cake",
        "Crumbly ground, perfect for tunnelling",
        "All five hearts are content",
        "Breathing happily through its skin",
        "Squeezing along, one segment at a time",
        "Doing the soil a world of good",
    };


    [Header("Sound")]
    [SerializeField] private SoundObject crawlSound;
    [SerializeField] private Vector2 crawlSoundInterval = new Vector2(4.0f, 9.0f);
    [SerializeField] private SoundObject burrowSound;
    [SerializeField] private SoundObject surfaceSound;


    [Header("Spreading")]
    // coming up, it turns ground next to where it surfaced from spreadFrom into spreadTo, as long as
    // that ground already touches one of spreadNextTo (see TerrainSpread)
    [SerializeField] private TerrainTile spreadFrom;    // wasteland
    [SerializeField] private TerrainTile spreadTo;      // dirt
    [Tooltip("the ground a spread creeps out from: whatever the worm lives on (dirt and grass), since it " +
        "avoids wasteland and there's rarely any dirt left by the time it arrives. empty means Spread To")]
    [SerializeField] private TerrainTile[] spreadNextTo = new TerrainTile[0];
    [Tooltip("cells converted per whole day and night. it only converts on resurfacing (about once a " +
        "day with the default timings), up to Credit Cap at once, so rates much above that are capped")]
    [SerializeField] [Min(0.0f)] private float tilesPerDay = 1.0f;
    [SerializeField] [Min(1.0f)] private float creditCap = 2.0f;
    [Tooltip("extra pull, while it has a whole credit, on burrow exits it could spread at, so it goes " +
        "and churns the edge of the patch instead of waiting to luck into it. 0 is pure luck")]
    [SerializeField] [Min(0.0f)] private float seekWeight = 8.0f;
    [Tooltip("cells each way it looks for somewhere to spread when there's nowhere within Burrow Radius. " +
        "it then heads for the nearest by burrowing towards it")]
    [SerializeField] [Min(1)] private int seekRadius = 8;
    [SerializeField] private SoundObject spreadSound;


    private TerrainManager terrain;
    private Sprite crawlSprite;

    private Vector3 feet;           // on the top surface; the transform is this plus the lift
    private float spriteBottom;     // the sprite's lowest vertex below its pivot, in sprite units (so negative)

    private float renameWiggleTimer;

    private int happyLineIndex = -1;    // -1 until one is picked; picked afresh each focus and after a problem

    private WormState state;
    private float stateTimer;       // counts down in every state but Burrowed
    private float flipTimer;        // the crawl wiggle, or the dig flips while going down or coming up
    private float soundTimer;

    // crawling
    private int level;
    private bool hasTarget;
    private Vector3 target;
    private float pauseTimer;

    // burrowing
    private Vector3 burrowFrom;
    private Vector3 burrowTo;
    private float burrowElapsed;
    private float burrowTime;

    // scratch for the weighted step pick; sized to STEP_OFFSETS so picking never allocates
    private readonly Vector2Int[] candidates = new Vector2Int[9];
    private readonly float[] candidateWeights = new float[9];

    // spreading: credit earned towards the next conversion, and scratch for the cells around a burrow
    private float spreadCredit;
    private readonly Vector3Int[] spreadCandidates = new Vector3Int[9];

    // how one resurface pick seeks, fixed for both of its passes: nearby spreads, or heading for the
    // nearest far one (seekGoal, which the burrow start was seekGoalDistance from)
    private bool seekNear;
    private bool seekFar;
    private Vector2 seekGoal;
    private float seekGoalDistance;


    // MARK: read-only state


    public WormState State { get { return state; } }


    // true while the sprite is hidden underground. The transform still tracks where the worm is
    public bool IsBurrowed { get { return state == WormState.Burrowed; } }


    // MARK: lifecycle


    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null) Debug.LogError("WormController: no SpriteRenderer, so the worm cannot wiggle, hide or show its burrow sprite", this);
        else crawlSprite = spriteRenderer.sprite;

        MeasureLift();
    }


    private void Start()
    {
        terrain = TerrainManager.Instance;

        if (terrain == null)
        {
            Debug.LogError("WormController: no TerrainManager, so the worm cannot find the ground and stays still", this);
            enabled = false;
            return;
        }

        if (spreadFrom == null || spreadTo == null)
        {
            Debug.LogError("WormController: no spread tiles assigned, so the worm never turns wasteland into dirt", this);
        }

        SnapToSurface();
        BeginCrawling();
    }


    private void Update()
    {
        float dt = Time.deltaTime;   // scaled, so a paused game freezes the worm
        if (dt <= 0.0f) return;

        spreadCredit = TerrainSpread.Accrue(spreadCredit, tilesPerDay, creditCap, dt);

        switch (state)
        {
            case WormState.Crawling: UpdateCrawling(dt); break;
            case WormState.EnterBurrow: UpdateEnterBurrow(dt); break;
            case WormState.Burrowed: UpdateBurrowed(dt); break;
            case WormState.LeaveBurrow: UpdateLeaveBurrow(dt); break;
        }

        ApplyPosition();
    }


    // MARK: states


    private void BeginCrawling()
    {
        state = WormState.Crawling;
        stateTimer = RandomIn(crawlDuration);
        flipTimer = RandomIn(flipInterval);
        soundTimer = RandomIn(crawlSoundInterval);
        hasTarget = false;
        pauseTimer = RandomIn(stepPause);

        SetSprite(crawlSprite);
        SetVisible(true);
    }


    private void UpdateCrawling(float dt)
    {
        flipTimer -= dt;
        renameWiggleTimer -= dt;

        if (flipTimer <= 0.0f)
        {
            flipTimer = RandomIn(renameWiggleTimer > 0.0f ? renameWiggleInterval : flipInterval);
            if (spriteRenderer != null) spriteRenderer.flipX = !spriteRenderer.flipX;
        }

        soundTimer -= dt;

        if (soundTimer <= 0.0f)
        {
            soundTimer = RandomIn(crawlSoundInterval);
            PlaySound(crawlSound);
        }

        Crawl(dt);

        // burrowing is its only way down a cliff, so any ground it may stand on will do. Only a
        // forbidden tile (it loaded onto one) keeps it crawling until it's off
        stateTimer -= dt;
        if (stateTimer <= 0.0f && !IsForbidden(TileUnder(feet))) BeginEnterBurrow();
    }


    private void BeginEnterBurrow()
    {
        state = WormState.EnterBurrow;
        stateTimer = digDuration;
        flipTimer = RandomIn(digFlipInterval);

        if (transitionSprite != null) SetSprite(transitionSprite);
        PlaySound(burrowSound);
    }


    private void UpdateEnterBurrow(float dt)
    {
        Dig(dt);

        stateTimer -= dt;
        if (stateTimer > 0.0f) return;

        state = WormState.Burrowed;
        SetVisible(false);

        // underground it is just a worm again: its own sprite, wiggling, which a starred worm's glow shows
        SetSprite(crawlSprite);
        flipTimer = RandomIn(flipInterval);

        burrowFrom = feet;
        burrowTo = PickResurfacePoint(burrowFrom);
        burrowElapsed = 0.0f;
        burrowTime = Mathf.Max(0.01f, RandomIn(burrowDuration));
    }


    // silent, and moving the whole time, so anything following the transform follows it underground
    private void UpdateBurrowed(float dt)
    {
        flipTimer -= dt;

        if (flipTimer <= 0.0f)
        {
            flipTimer = RandomIn(flipInterval);
            if (spriteRenderer != null) spriteRenderer.flipX = !spriteRenderer.flipX;
        }

        burrowElapsed += dt;

        float t = Mathf.Clamp01(burrowElapsed / burrowTime);
        feet = Vector3.Lerp(burrowFrom, burrowTo, t);

        if (t < 1.0f) return;

        level = LevelAt(burrowTo);

        state = WormState.LeaveBurrow;
        stateTimer = digDuration;
        flipTimer = RandomIn(digFlipInterval);

        if (transitionSprite != null) SetSprite(transitionSprite);
        SetVisible(true);
        PlaySound(surfaceSound);

        SpreadAround(burrowTo);
    }


    private void UpdateLeaveBurrow(float dt)
    {
        Dig(dt);

        stateTimer -= dt;
        if (stateTimer <= 0.0f) BeginCrawling();
    }


    // mirrored back and forth, a few times, while the worm goes into or comes out of the ground
    private void Dig(float dt)
    {
        flipTimer -= dt;
        if (flipTimer > 0.0f) return;

        flipTimer = RandomIn(digFlipInterval);
        if (spriteRenderer != null) spriteRenderer.flipX = !spriteRenderer.flipX;
    }


    // MARK: crawling


    private void Crawl(float dt)
    {
        if (!hasTarget)
        {
            pauseTimer -= dt;
            if (pauseTimer > 0.0f) return;

            hasTarget = TryPickStep(out target);

            // boxed in: wait and look again
            if (!hasTarget)
            {
                pauseTimer = RandomIn(stepPause);
                return;
            }
        }

        feet = Vector3.MoveTowards(feet, target, crawlSpeed * dt);

        if ((feet - target).sqrMagnitude > 0.000001f) return;

        hasTarget = false;
        pauseTimer = RandomIn(stepPause);
    }


    // A weighted pick from this column and its 8 neighbours on the same level. A diagonal only counts
    // when both orthogonals it cuts between are open too, so a straight line to it never crosses a
    // forbidden corner or the edge of a cliff
    private bool TryPickStep(out Vector3 picked)
    {
        picked = default;

        int x = Mathf.FloorToInt(feet.x);
        int z = Mathf.FloorToInt(feet.z);

        int count = 0;
        float total = 0.0f;

        for (int i = 0; i < STEP_OFFSETS.Length; i++)
        {
            Vector2Int offset = STEP_OFFSETS[i];

            if (!IsCrawlable(x + offset.x, z + offset.y, out TerrainTile tile)) continue;

            if (offset.x != 0 && offset.y != 0)
            {
                if (!IsCrawlable(x + offset.x, z, out TerrainTile _)) continue;
                if (!IsCrawlable(x, z + offset.y, out TerrainTile _)) continue;
            }

            float weight = IsPreferred(tile) ? preferredWeight : 1.0f;

            candidates[count] = new Vector2Int(x + offset.x, z + offset.y);
            candidateWeights[count] = weight;
            count++;
            total += weight;
        }

        if (count == 0) return false;

        float roll = Random.value * total;
        int chosen = count - 1;

        for (int i = 0; i < count; i++)
        {
            roll -= candidateWeights[i];
            if (roll > 0.0f) continue;

            chosen = i;
            break;
        }

        picked = PointIn(candidates[chosen].x, candidates[chosen].y, level);
        return true;
    }


    // a column the worm can crawl into: its top is on the worm's level and it isn't forbidden
    private bool IsCrawlable(int x, int z, out TerrainTile tile)
    {
        tile = null;

        if (terrain.GetTopLevel(x, z) != level) return false;

        tile = terrain.GetTile(new Vector3Int(x, level, z));

        return tile != null && !IsForbidden(tile);
    }


    // MARK: burrowing


    // A weighted pick from every column within burrowRadius on any level, not counting the one it
    // went down in, pulled towards the edge of the patch while it has a spread to spend. Two passes
    // over at most (2r+1)^2 columns, plus the seek set-up, once per burrow, and no allocation
    private Vector3 PickResurfacePoint(Vector3 from)
    {
        int x = Mathf.FloorToInt(from.x);
        int z = Mathf.FloorToInt(from.z);

        // decided once, so both passes weigh every column the same
        PlanSeek(x, z);

        float total = 0.0f;

        for (int dz = -burrowRadius; dz <= burrowRadius; dz++)
        {
            for (int dx = -burrowRadius; dx <= burrowRadius; dx++)
            {
                total += ResurfaceWeight(x, z, dx, dz);
            }
        }

        // nowhere else to go, so it comes back up where it went down
        if (total <= 0.0f) return from;

        float roll = Random.value * total;

        for (int dz = -burrowRadius; dz <= burrowRadius; dz++)
        {
            for (int dx = -burrowRadius; dx <= burrowRadius; dx++)
            {
                float weight = ResurfaceWeight(x, z, dx, dz);
                if (weight <= 0.0f) continue;

                roll -= weight;
                if (roll > 0.0f) continue;

                int top = terrain.GetTopLevel(x + dx, z + dz);
                return PointIn(x + dx, z + dz, top);
            }
        }

        return from;
    }


    // seeking adds seekWeight on columns it could spread at, or, heading for a far one, up to
    // seekWeight on columns by how much closer they bring it. never on one it can't come up in at all
    private float ResurfaceWeight(int x, int z, int dx, int dz)
    {
        if (dx == 0 && dz == 0) return 0.0f;

        int top = terrain.GetTopLevel(x + dx, z + dz);
        if (top < 0) return 0.0f;

        TerrainTile tile = terrain.GetTile(new Vector3Int(x + dx, top, z + dz));
        if (tile == null || IsForbidden(tile)) return 0.0f;

        float weight = IsPreferred(tile) ? preferredWeight : 1.0f;

        if (seekNear && CanSpreadAround(x + dx, z + dz)) weight += seekWeight;
        if (seekFar) weight += seekWeight * CloserBy(x + dx, z + dz);

        return weight;
    }


    // 0 for no closer to the far goal than where it went down, 1 for a whole burrowRadius closer
    private float CloserBy(int x, int z)
    {
        float distance = Vector2.Distance(new Vector2(x + 0.5f, z + 0.5f), seekGoal);

        return Mathf.Clamp01((seekGoalDistance - distance) / burrowRadius);
    }


    // With a spread to spend: somewhere within burrowRadius to spread at, else the nearest spreadable
    // cell out to seekRadius to head for. That scan is one CanSpread per column outside the burrow
    // radius, at most (2 * seekRadius + 1)^2, and only when nothing was near
    private void PlanSeek(int x, int z)
    {
        seekNear = false;
        seekFar = false;

        if (!IsSeekingSpread) return;

        for (int dz = -burrowRadius; dz <= burrowRadius && !seekNear; dz++)
        {
            for (int dx = -burrowRadius; dx <= burrowRadius; dx++)
            {
                if ((dx != 0 || dz != 0) && CanSpreadAround(x + dx, z + dz))
                {
                    seekNear = true;
                    break;
                }
            }
        }

        if (seekNear) return;

        int best = int.MaxValue;

        for (int dz = -seekRadius; dz <= seekRadius; dz++)
        {
            for (int dx = -seekRadius; dx <= seekRadius; dx++)
            {
                // already looked at, around every column in the burrow radius
                if (Mathf.Abs(dx) <= burrowRadius && Mathf.Abs(dz) <= burrowRadius) continue;

                int distance = dx * dx + dz * dz;
                if (distance >= best) continue;

                if (!TerrainSpread.CanSpread(TopCell(x + dx, z + dz), spreadFrom, spreadTo, spreadNextTo)) continue;

                best = distance;
                seekGoal = new Vector2(x + dx + 0.5f, z + dz + 0.5f);
            }
        }

        if (best == int.MaxValue) return;

        seekFar = true;
        seekGoalDistance = Mathf.Sqrt(best);
    }


    // MARK: spreading


    // Coming up churns the ground around it: the column it surfaced in or one of the eight around it,
    // at random, per whole credit. Picked afresh after each one, since the dirt just made can open up
    // more frontier
    private void SpreadAround(Vector3 point)
    {
        if (spreadFrom == null || spreadTo == null) return;

        int x = Mathf.FloorToInt(point.x);
        int z = Mathf.FloorToInt(point.z);

        while (spreadCredit >= 1.0f)
        {
            int count = 0;

            for (int i = 0; i < STEP_OFFSETS.Length; i++)
            {
                Vector3Int cell = TopCell(x + STEP_OFFSETS[i].x, z + STEP_OFFSETS[i].y);
                if (TerrainSpread.CanSpread(cell, spreadFrom, spreadTo, spreadNextTo)) spreadCandidates[count++] = cell;
            }

            if (count == 0) return;

            if (!TerrainSpread.TrySpread(spreadCandidates[Random.Range(0, count)], spreadFrom, spreadTo, spreadNextTo, spreadSound)) return;

            spreadCredit -= 1.0f;
        }
    }


    // whether coming up in this column would find something to spread: it or a column around it.
    // Only asked while seeking, so the nine checks per column cost nothing the rest of the time
    private bool CanSpreadAround(int x, int z)
    {
        for (int i = 0; i < STEP_OFFSETS.Length; i++)
        {
            Vector3Int cell = TopCell(x + STEP_OFFSETS[i].x, z + STEP_OFFSETS[i].y);
            if (TerrainSpread.CanSpread(cell, spreadFrom, spreadTo, spreadNextTo)) return true;
        }

        return false;
    }


    // it only goes looking with a whole credit to spend, so between spreads it wanders as it always did
    private bool IsSeekingSpread
    {
        get { return seekWeight > 0.0f && spreadCredit >= 1.0f && spreadFrom != null && spreadTo != null; }
    }


    private Vector3Int TopCell(int x, int z)
    {
        return new Vector3Int(x, terrain.GetTopLevel(x, z), z);
    }


    // MARK: surface


    // A worm saved mid-burrow can be saved between levels, so it goes back onto the top of the column
    // it is over. If that column is forbidden it may still crawl off it, it just won't choose one
    //
    // the transform may already be lifted (a saved worm), so the height comes from the terrain alone
    private void SnapToSurface()
    {
        Vector3 position = transform.position;

        int top = terrain.GetTopLevel(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));

        if (top < 0)
        {
            Debug.LogWarning("WormController: the worm is not over the terrain, so it stays where it is", this);
            level = LevelAt(position);
            feet = position;
            return;
        }

        level = top;
        feet = new Vector3(position.x, terrain.GetLevelHeight(top), position.z);

        ApplyPosition();
    }


    // measured per call rather than cached, so the lift follows the hover grow and the sprite still
    // stands on the ground while it is bigger
    private void ApplyPosition()
    {
        float lift = -spriteBottom * transform.lossyScale.y;
        transform.position = feet + Vector3.up * lift + ReactionOffset;
    }


    public override void OnRenamed()
    {
        base.OnRenamed();

        // only a crawling worm is on show to wiggle
        if (state != WormState.Crawling) return;

        renameWiggleTimer = renameWiggleDuration;
        flipTimer = 0.0f;
    }


    // The sprite's lowest vertex relative to its pivot. Vertices rather than bounds, so a tight sprite
    // mesh stands on its visible bottom instead of its rect's. Kept in sprite units: ApplyPosition
    // scales it, so it stays right while the worm is grown under the pointer
    private void MeasureLift()
    {
        spriteBottom = 0.0f;

        Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        if (sprite == null) return;

        Vector2[] vertices = sprite.vertices;
        if (vertices == null || vertices.Length == 0) return;

        float lowest = vertices[0].y;
        for (int i = 1; i < vertices.Length; i++) lowest = Mathf.Min(lowest, vertices[i].y);

        spriteBottom = lowest;
    }


    private int LevelAt(Vector3 position)
    {
        int top = terrain.GetTopLevel(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
        return top >= 0 ? top : Mathf.Max(0, terrain.GetLevelBelow(position.y));
    }


    private TerrainTile TileUnder(Vector3 position)
    {
        return terrain.GetTile(new Vector3Int(Mathf.FloorToInt(position.x), level, Mathf.FloorToInt(position.z)));
    }


    // a random point inside the cell, on the top of the given level
    private Vector3 PointIn(int x, int z, int onLevel)
    {
        return new Vector3(
            x + Random.Range(CELL_MARGIN, 1.0f - CELL_MARGIN),
            terrain.GetLevelHeight(onLevel),
            z + Random.Range(CELL_MARGIN, 1.0f - CELL_MARGIN));
    }


    // MARK: happiness


    // base plus the rest for the share of preferred tiles around it: always fairly happy, happiest
    // somewhere lush. Only shown while it has a tip; with none the panel shows it content
    public override float Happiness
    {
        get
        {
            if (terrain == null) return 1.0f;

            return Mathf.Clamp01(baseHappiness + (1.0f - baseHappiness) * PreferredShare());
        }
    }


    // off its preferred ground, and on its way back to it
    public override string[] GetHappinessTips()
    {
        if (terrain == null || IsPreferred(TileUnder(feet))) return base.GetHappinessTips();

        // happy again later is a new mood, so a new line
        happyLineIndex = -1;

        return new[] { offPreferredTip };
    }


    public override string GetHappyLine()
    {
        if (happyLines == null || happyLines.Length == 0) return null;

        if (happyLineIndex < 0 || happyLineIndex >= happyLines.Length) happyLineIndex = Random.Range(0, happyLines.Length);

        return happyLines[happyLineIndex];
    }


    // a fresh happy line each time it is looked at
    public override void OnFocused()
    {
        base.OnFocused();
        happyLineIndex = -1;
    }


    // The share of columns within happinessRadius whose top is a preferred tile. A (2r+1)^2 look,
    // only while its panel is open and asking
    private float PreferredShare()
    {
        int x = Mathf.FloorToInt(feet.x);
        int z = Mathf.FloorToInt(feet.z);

        int total = 0;
        int preferred = 0;

        for (int dz = -happinessRadius; dz <= happinessRadius; dz++)
        {
            for (int dx = -happinessRadius; dx <= happinessRadius; dx++)
            {
                int top = terrain.GetTopLevel(x + dx, z + dz);
                if (top < 0) continue;

                total++;

                if (IsPreferred(terrain.GetTile(new Vector3Int(x + dx, top, z + dz)))) preferred++;
            }
        }

        return total > 0 ? (float)preferred / total : 0.0f;
    }


    // A birth lands where this worm would crawl: never on a forbidden tile, and pulled towards the
    // same preferred ground it drifts over
    public override float SpawnWeightOn(TerrainTile tile)
    {
        if (tile == null || IsForbidden(tile)) return 0.0f;

        return IsPreferred(tile) ? preferredWeight : 1.0f;
    }


    // MARK: internals


    private bool IsForbidden(TerrainTile tile)
    {
        return Contains(forbiddenTiles, tile);
    }


    private bool IsPreferred(TerrainTile tile)
    {
        return Contains(preferredTiles, tile);
    }


    private static bool Contains(TerrainTile[] tiles, TerrainTile tile)
    {
        if (tiles == null || tile == null) return false;

        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] == tile) return true;
        }

        return false;
    }


    private void SetSprite(Sprite sprite)
    {
        if (spriteRenderer != null && sprite != null) spriteRenderer.sprite = sprite;

        MeasureLift();
    }


    private void SetVisible(bool visible)
    {
        if (spriteRenderer != null) spriteRenderer.enabled = visible;
    }


    private void PlaySound(SoundObject sound)
    {
        if (sound == null || AudioManager.Instance == null) return;

        AudioManager.Instance.PlaySFX(sound, transform.position);
    }


    private static float RandomIn(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }
}
