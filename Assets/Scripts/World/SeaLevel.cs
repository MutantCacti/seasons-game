using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


// The sea: a flat sheet at `level` over exactly the world's footprint, baked by TerrainBaker as the
// "Sea Level" child of the Grid. It is scene data, not world state, so nothing here is saved.
// The sea does nothing for gameplay; sea behaviour belongs to the seafloor tiles.
//
// In play mode there is no sea until WorldCondition.SeaRisen starts: the sheet waits down at the
// void with its renderer off. The condition is saved in the world states, so a load or a
// checkpoint reset puts the sea back where the save had it. Edit mode always shows it at `level`.
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


    // the rest is written by TerrainBaker
    [Header("Baked")]
    [SerializeField] private float floorY = -4.0f;  // the terrain's voidDepth, where the skirt bottoms out


    private MeshRenderer sheet;
    private WorldStateManager worldStates;
    private bool risen;

    // true only while the sticky ConditionChanged is replaying the saved states at subscribe time
    private bool replaying;


    // the designer's level, where the sea is once risen. Readable any time, including before Awake
    // and while inactive, because it is the serialized value
    public float Y { get { return level; } }


    // edit mode always has a sea, so the sheet can be seen and tuned
    public bool IsRisen { get { return risen || !Application.isPlaying; } }


    // where the sheet actually is right now
    public float SurfaceY { get { return transform.position.y; } }


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
        if (sheet == null) Debug.LogError("SeaLevel: no MeshRenderer, so the sea can't be hidden before it rises", this);

        // hidden before anything renders, then Start finds out if the save already has it risen
        if (Application.isPlaying) SetSheetVisible(false);

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


    // the live rise, the moment SeaRisen starts. just a snap for now; the slow rise goes here, from
    // the void up to `level`, and Apply() mustn't pull the sheet back to `level` while it runs
    private void Rise()
    {
        Snap();
    }


    private void Snap()
    {
        risen = true;
        SetSheetVisible(true);
        Apply();
    }


    private void SetSheetVisible(bool visible)
    {
        if (sheet != null) sheet.enabled = visible;
    }


    // MARK: level


    public void SetLevel(float newLevel)
    {
        level = newLevel;
        Apply();
    }


    // down at the void until risen: under every top, and the skirt collapses to nothing there
    private float TargetY { get { return IsRisen ? level : floorY; } }


    public void Apply()
    {
        float y = TargetY;

        Vector3 position = transform.position;
        if (position.y == y) return;

        transform.position = new Vector3(position.x, y, position.z);
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
    }


    // MARK: bake


    public void SetFloor(float voidDepth)
    {
        floorY = voidDepth;
        PushGlobals();
    }
}
