using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


// The sea: a flat sheet at `level` over exactly the world's footprint, baked by TerrainBaker as the
// "Sea Level" child of the Grid. It is scene data, not world state, so nothing here is saved.
// The sea does nothing for gameplay; sea behaviour belongs to the seafloor tiles.
//
// In play mode there is no water surface until WorldCondition.SeaRisen starts: the sheet waits at
// skirtStartY, low enough that the terrain hides it, and then rises to `level` over riseDuration.
// The condition is saved in the world states, so a load or a checkpoint reset puts the sea back
// where the save had it. Edit mode always shows it at `level`.
//
// Nothing here ever disables the renderer. The sheet is hidden by position alone, and the shader
// collapses its quads while the surface sits at or below the lowest terrain top, so water nobody can
// see costs no fill. The skirt is deliberately left out of that: it is the diorama's edge, and since
// the sea rests at skirtStartY its top comes up to there, so the water column is already in view
// before the rise rather than appearing with it. Its base stays down on the terrain's void.
//
// NB: the trigger BoxCollider spans the baked mesh's bounds, so it also covers every hole and gap in
// the silhouette. Any future "is this over water" query must check tiles/cells, not the collider.
//
// ExecuteAlways so the sheet and the shader's globals are right in the Scene view without playing
[ExecuteAlways]
public class SeaLevel : MonoBehaviour
{
    private static SeaLevel instance;
    private static int searchedFrame = -1;

    private static readonly int SeaDaylightId = Shader.PropertyToID("_SeaDaylight");
    private static readonly int SeaSkyColourId = Shader.PropertyToID("_SeaSkyColour");
    private static readonly int SeaFloorYId = Shader.PropertyToID("_SeaFloorY");
    private static readonly int SeaLowestTopId = Shader.PropertyToID("_SeaLowestTop");

    // what the shader sees with no DayNightManager (edit mode, or a scene without one): noon
    private static readonly Color FallbackSky = new Color(169.0f / 255.0f, 207.0f / 255.0f, 220.0f / 255.0f, 1.0f);    // #A9CFDC


    // a caller that runs before our Awake, or while we're inactive, still finds the sea. The scene
    // search runs at most once a frame, so a scene with no sea doesn't pay for it on every call
    public static SeaLevel Instance
    {
        get
        {
            if (instance == null && searchedFrame != Time.frameCount)
            {
                searchedFrame = Time.frameCount;
                instance = FindAnyObjectByType<SeaLevel>(FindObjectsInactive.Include);
            }

            return instance;
        }
    }


    // world y of the sheet. Designer owned: a re-bake never writes it. Anything at or below the
    // lowest terrain level is hidden under the tops, since the sheet only covers the footprint
    [SerializeField] private float level = 0.25f;


    // world y the sea rests at until it rises. Designer owned. The skirt's top edge is the sheet's
    // plane, so this is what brings the diorama's water column into view before the rise: the skirt
    // runs from here down to the terrain's void (floorY). Keep it at or below lowestTopY, or the
    // water surface itself shows before it has risen
    [SerializeField] private float skirtStartY = -1.0f;


    // seconds for the live rise, skirtStartY to `level`. Designer owned. 0 snaps, which is also what
    // a load and a checkpoint reset always do, so restoring a save never replays the rise
    [SerializeField] private float riseDuration = 1.5f;

    // played at the camera when the live rise starts; a load or reset snaps silently. Optional
    [SerializeField] private SoundObject riseSound;


    // the rest is written by TerrainBaker
    [Header("Baked")]
    [SerializeField] private float floorY = -4.0f;  // the terrain's voidDepth, where the skirt bottoms out

    // the lowest terrain top: at or below it the sheet is under every top and cannot be seen, which
    // is where the shader collapses it. Defaults to the void, so a scene that has not been re-baked
    // is merely less efficient rather than hiding water that ought to be showing
    [SerializeField] private float lowestTopY = -4.0f;

    // the terrain's pixel grid, so the rise steps the way the art does. 32 matches the baker's
    // pixelsPerTile
    [SerializeField] private float pixelsPerUnit = 32.0f;


    private MeshRenderer sheet;
    private WorldStateManager worldStates;
    private bool risen;

    // the live rise. `risen` latches the moment it starts, so this only says whether the transform is
    // still being animated, which is what stops Apply() from yanking the sheet to `level` mid-flight
    private bool rising;
    private float riseElapsed;
    private float riseFrom;

    // true only while the sticky ConditionChanged is replaying the saved states at subscribe time
    private bool replaying;


    // the designer's level, where the sea is once risen. Readable any time, including before Awake
    // and while inactive, because it is the serialized value
    public float Y { get { return level; } }


    // edit mode always has a sea, so the sheet can be seen and tuned
    public bool IsRisen { get { return risen || !Application.isPlaying; } }


    // where the sheet actually is right now
    public float SurfaceY { get { return transform.position.y; } }


    // whether the sheet is high enough that any of it can be seen at all. At or below the lowest
    // terrain top the terrain covers the whole footprint, which is the same test the shader uses to
    // collapse the sheet, so this is the one place "is there water to look at" is decided
    public bool IsSurfaceVisible { get { return SurfaceY > lowestTopY; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            // edit mode Awakes too, and Destroy isn't allowed there
            if (Application.isPlaying)
            {
                Debug.LogWarning("SeaLevel: Found more than one SeaLevel in the scene - destroying newest!!");
                Destroy(gameObject);
            }

            return;
        }

        instance = this;

        sheet = GetComponent<MeshRenderer>();
        if (sheet == null) Debug.LogError("SeaLevel: no MeshRenderer, so the sea will not draw at all", this);

        // note: the sheet is never hidden by disabling the renderer. It waits down at the void, under
        // the terrain, and the shader collapses its quads there, so the skirt keeps drawing throughout
        Apply();
    }


    private void Start()
    {
        if (!Application.isPlaying) return;

        worldStates = WorldStateManager.Instance;

        if (worldStates == null)
        {
            // nothing could ever raise it, so a scene without one just gets its sea
            Debug.LogError("SeaLevel: no WorldStateManager, so SeaRisen is never heard - showing the sea straight away", this);
            Snap();
            return;
        }

        // sticky: a save with the sea risen is replayed straight away, inside this +=
        replaying = true;
        worldStates.ConditionChanged += OnConditionChanged;
        replaying = false;
    }


    private void OnEnable()
    {
        PushGlobals();
    }


    private void OnDestroy()
    {
        if (worldStates != null) worldStates.ConditionChanged -= OnConditionChanged;

        if (instance == this) instance = null;
    }


    private void OnDisable()
    {
        // the rise is not resumable: one interrupted by going inactive finishes instead of leaving
        // the sheet hanging halfway up with no water under it
        if (Application.isPlaying && rising) Snap();
    }


    private void Update()
    {
        if (!rising) return;

        riseElapsed += Time.deltaTime;

        float t = riseDuration <= 0.0f ? 1.0f : Mathf.Clamp01(riseElapsed / riseDuration);
        float y = Mathf.SmoothStep(riseFrom, level, t);

        // intermediate rows land on the grid the art steps on, so the water climbs band by band
        // rather than sliding between them; the finish lands exactly on `level`, so the last frame and
        // Apply() agree even if a designer sets an off-grid level
        SetY(t >= 1.0f ? level : StepToPixel(y));

        if (t >= 1.0f)
        {
            rising = false;
            Apply();
        }
    }


    private void LateUpdate()
    {
        PushGlobals();
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        // wait a tick rather than move the transform from inside OnValidate, which Unity restricts
        EditorApplication.delayCall += ApplyIfAlive;
    }


    private void ApplyIfAlive()
    {
        if (this == null) return;

        Apply();
        PushGlobals();
    }
#endif


    // MARK: rising


    // an ended SeaRisen still counts: the sea doesn't drain away
    private void OnConditionChanged(WorldCondition condition, ConditionPhase phase)
    {
        if (condition != WorldCondition.SeaRisen || phase == ConditionPhase.NotStarted) return;
        if (risen) return;

        // a load snaps, only a live start gets to rise
        if (replaying) Snap();
        else Rise();
    }


    // the live rise, the moment SeaRisen starts: from wherever the sheet is waiting, which is the
    // void, up to `level` over riseDuration. Update() carries it from here
    private void Rise()
    {
        risen = true;

        PlayRiseSound();

        if (riseDuration <= 0.0f)
        {
            Apply();
            return;
        }

        rising = true;
        riseElapsed = 0.0f;
        riseFrom = SurfaceY;
    }


    // the sea belongs to the whole view, not a spot on it, so it plays where the listener is
    private void PlayRiseSound()
    {
        if (riseSound == null || AudioManager.Instance == null) return;

        Camera viewCamera = Camera.main;
        Vector3 position = viewCamera != null ? viewCamera.transform.position : transform.position;

        AudioManager.Instance.PlaySFX(riseSound, position);
    }


    // the instant version, for a load and for a rise with no duration
    private void Snap()
    {
        rising = false;
        risen = true;
        Apply();
    }


    // MARK: level


    public void SetLevel(float newLevel)
    {
        level = newLevel;
        Apply();
    }


    // down at skirtStartY until risen: under every top, where the shader collapses the sheet, while
    // the skirt still runs from here down to the terrain's void
    private float TargetY { get { return IsRisen ? level : skirtStartY; } }


    public void Apply()
    {
        // the rise owns the transform while it runs, so a SetLevel or an inspector tweak cannot yank
        // the sheet to `level` mid-flight
        if (rising) return;

        SetY(TargetY);
    }


    private void SetY(float y)
    {
        Vector3 position = transform.position;
        if (position.y == y) return;

        transform.position = new Vector3(position.x, y, position.z);
    }


    // one row of the terrain's pixel grid, the same 1/32 the shader steps the water and the skirt's
    // bands on
    private float StepToPixel(float y)
    {
        if (pixelsPerUnit <= 0.0f) return y;

        return Mathf.Round(y * pixelsPerUnit) / pixelsPerUnit;
    }


    // MARK: shader globals


    // globals rather than material properties, so any Seasons/Sea material dropped on the renderer
    // just works. There is only ever one sea
    private void PushGlobals()
    {
        DayNightManager dayNight = DayNightManager.Instance;
        Shader.SetGlobalFloat(SeaDaylightId, dayNight != null ? dayNight.Daylight01 : 1.0f);

        // DayNightManager already lerps the camera's clear colour between its sky colours (in Update,
        // so this is the same frame's), so the camera is the one place that always has the sky as drawn
        Camera sky = Camera.main;
        Color skyColour = sky != null ? sky.backgroundColor : FallbackSky;

        // the conversion URP makes for the clear itself (UniversalRenderPipeline, via
        // CoreUtils.ConvertSRGBToActiveColorSpace), sent as a vector so nothing converts it again:
        // the skirt's bottom has to be exactly the clear colour
        if (QualitySettings.activeColorSpace == ColorSpace.Linear) skyColour = skyColour.linear;
        Shader.SetGlobalVector(SeaSkyColourId, skyColour);

        Shader.SetGlobalFloat(SeaFloorYId, floorY);

        // the sheet is collapsed at or below this, so the hidden water costs no fill
        Shader.SetGlobalFloat(SeaLowestTopId, lowestTopY);
    }


    // MARK: bake


    // TerrainBaker hands over everything the shader needs to place the sea against the terrain:
    // the void the skirt bottoms out at, the grid to step the rise on, and the lowest top, which is
    // the height the sheet has to clear before any of it can be seen
    public void SetBake(float voidDepth, float pixelsPerTile, float lowestTop)
    {
        floorY = voidDepth;
        if (pixelsPerTile > 0.0f) pixelsPerUnit = pixelsPerTile;
        lowestTopY = lowestTop;
        PushGlobals();
    }
}
