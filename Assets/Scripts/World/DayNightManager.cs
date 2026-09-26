using UnityEngine;


// Draws the day/night cycle: the sun's rotation and intensity, and the sky colour. The clock itself
// (the day count, the time of day, and saving them) is WorldStateManager's, which this only reads.
public class DayNightManager : MonoBehaviour
{
    private static DayNightManager instance;
    public static DayNightManager Instance { get { return instance; } }


    [SerializeField] private GameObject sunGO;
    [SerializeField] private Vector3 sunRiseRotation = new Vector3(-2f, -135f, 0f);
    [SerializeField] private float sunNoonAngle = 60f;
    private Vector3 sunNoonRotation;
    [SerializeField] private Vector3 sunSetRotation = new Vector3(-2f, 55f, 0f);
    [SerializeField] private float noonIntensity = 1f;
    [SerializeField] private float nightIntensity = 0f;


    // the camera clears to a solid colour, which follows the sun's light from night to day. the
    // defaults match the clock's dial, so the sky and the clock agree
    [Header("Sky")]
    [SerializeField] private Camera skyCamera;  // falls back to Camera.main
    [SerializeField] private Color skyDayColour = new Color(169.0f / 255.0f, 207.0f / 255.0f, 220.0f / 255.0f, 1.0f);    // #A9CFDC
    [SerializeField] private Color skyNightColour = new Color(60.0f / 255.0f, 65.0f / 255.0f, 112.0f / 255.0f, 1.0f);    // #3C4170


    // MARK: public API


    // How much daylight there is: 0 at night, 1 at noon, on the same eased curve as the sun's
    // intensity. The sky colour follows it directly; the clock's border switches on it.
    public float Daylight01 { get; private set; }


    // MARK: internals


    private Light sunLight;
    private Transform sunTransform;

    private bool warnedAboutMissingClock;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one DayNightManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        sunNoonRotation = new Vector3(sunNoonAngle, (sunRiseRotation.y + sunSetRotation.y) * 0.5f, 0f);

        if (sunGO == null)
        {
            Debug.LogError("DayNightManager: no sunGO, so the day/night cycle cannot run", this);
            enabled = false;
            return;
        }

        sunLight = sunGO.GetComponent<Light>();
        sunTransform = sunGO.transform;

        if (sunLight == null)
        {
            Debug.LogError("DayNightManager: sunGO has no Light, so its intensity cannot be driven", this);
            enabled = false;
            return;
        }

        if (skyCamera == null) skyCamera = Camera.main;
        if (skyCamera == null) Debug.LogWarning("DayNightManager: no sky camera and no Camera.main, so the sky colour stays as it is", this);
    }


    // Start rather than Awake, so the clock has had its own Awake and the first frame is already right
    private void Start()
    {
        ApplySunState();
    }


    private void Update()
    {
        ApplySunState();
    }


    // MARK: sun state


    // Reproduces the old DOTween chain: sunrise -> noon -> sunset over dayDuration with OutSine up
    // and InSine down, then night over nightDuration with DOTween's default ease (InOutQuad). Each
    // Euler axis is interpolated by the shortest angle (Mathf.LerpAngle), which is the manager's
    // understanding of DOLocalRotate(Vector3) in RotateMode.Fast: DOTween ships as a DLL here, so
    // this cannot be read from source.
    private void ApplySunState()
    {
        if (sunTransform == null || sunLight == null) return;

        WorldStateManager clock = WorldStateManager.Instance;

        if (clock == null)
        {
            if (!warnedAboutMissingClock)
            {
                warnedAboutMissingClock = true;
                Debug.LogError("DayNightManager: no WorldStateManager in the scene, so the sun and sky stand still", this);
            }

            return;
        }

        float cycleTime = clock.CycleTime;
        float dayDuration = clock.DayDuration;
        float nightDuration = clock.NightDuration;

        if (cycleTime < dayDuration)
        {
            float dayProgress = cycleTime / dayDuration;

            if (dayProgress < 0.5f)
            {
                float e = EaseOutSine(dayProgress / 0.5f);
                sunTransform.localEulerAngles = LerpEuler(sunRiseRotation, sunNoonRotation, e);
                sunLight.intensity = Mathf.Lerp(nightIntensity, noonIntensity, e);
                ApplySky(e);
            }
            else
            {
                float e = EaseInSine((dayProgress - 0.5f) / 0.5f);
                sunTransform.localEulerAngles = LerpEuler(sunNoonRotation, sunSetRotation, e);
                sunLight.intensity = Mathf.Lerp(noonIntensity, nightIntensity, e);
                ApplySky(1f - e);
            }
        }
        else
        {
            float e = EaseInOutQuad((cycleTime - dayDuration) / nightDuration);
            sunTransform.localEulerAngles = LerpEuler(sunSetRotation, sunRiseRotation, e);
            sunLight.intensity = nightIntensity;
            ApplySky(0f);
        }
    }


    // daylight is 0 at night and 1 at noon, on the same eased curve as the sun's intensity
    private void ApplySky(float daylight)
    {
        Daylight01 = daylight;

        if (skyCamera == null) return;

        skyCamera.backgroundColor = Color.Lerp(skyNightColour, skyDayColour, daylight);
    }


    private static Vector3 LerpEuler(Vector3 a, Vector3 b, float t)
    {
        return new Vector3(
            Mathf.LerpAngle(a.x, b.x, t),
            Mathf.LerpAngle(a.y, b.y, t),
            Mathf.LerpAngle(a.z, b.z, t));
    }


    // DOTween's Ease.OutSine / InSine / InOutQuad formulas, so the sun moves exactly as before
    private static float EaseOutSine(float t) { return Mathf.Sin(t * Mathf.PI * 0.5f); }


    private static float EaseInSine(float t) { return 1f - Mathf.Cos(t * Mathf.PI * 0.5f); }


    private static float EaseInOutQuad(float t)
    {
        if (t < 0.5f) return 2f * t * t;
        return -2f * t * t + 4f * t - 1f;
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
