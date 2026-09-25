using System;
using UnityEngine;


// Drives the sun's rotation and intensity over a saved day/night cycle and tracks play time. The
// on-screen clock reads Day / DayPhase01 / DialAngleDegrees from here. Time is written through to
// SaveData.world.time every frame (in memory only), so saves and checkpoint captures see it.
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


    [Tooltip("Duration in seconds")]
    [SerializeField] private float dayDuration = 10f;
    [SerializeField] private float nightDuration = 2f;


    // the camera clears to a solid colour, which follows the sun's light from night to day. the
    // defaults match the clock's dial, so the sky and the clock agree
    [Header("Sky")]
    [SerializeField] private Camera skyCamera;  // falls back to Camera.main
    [SerializeField] private Color skyDayColour = new Color(169.0f / 255.0f, 207.0f / 255.0f, 220.0f / 255.0f, 1.0f);    // #A9CFDC
    [SerializeField] private Color skyNightColour = new Color(60.0f / 255.0f, 65.0f / 255.0f, 112.0f / 255.0f, 1.0f);    // #3C4170


    // MARK: public time API


    public int Day { get; private set; }


    // Normalised day phase: 0 at sunrise, 0.5 at noon, 1 at sunset. Holds 1 throughout the night.
    public float DayPhase01 { get; private set; }


    // The clock dial's rotation in degrees, counter-clockwise positive. 0 is noon (sun centred).
    public float DialAngleDegrees { get; private set; }


    // How much daylight there is: 0 at night, 1 at noon, on the same eased curve as the sun's
    // intensity. The sky colour follows it directly; the clock's border switches on it.
    public float Daylight01 { get; private set; }


    // Raised each time the cycle wraps into a new day (a new sunrise).
    public event Action DayStarted;


    // MARK: internals


    private Light sunLight;
    private Transform sunTransform;

    // cached the way AnimalManager caches world: only a new game or a checkpoint restore swaps
    // SaveData.world out, and both reload this scene, so the cache lives for the scene's whole life
    private SaveData save;
    private WorldState world;

    private float cycleTime;        // seconds since sunrise, [0, dayDuration + nightDuration)
    private double playRemainder;   // sub-millisecond leftover, so no ms is lost per frame


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one DayNightManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        Day = 1;

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

        if (dayDuration <= 0f || nightDuration <= 0f)
        {
            Debug.LogError("DayNightManager: dayDuration and nightDuration must be positive, so the day/night cycle cannot run", this);
            enabled = false;
            return;
        }

        if (skyCamera == null) skyCamera = Camera.main;
        if (skyCamera == null) Debug.LogWarning("DayNightManager: no sky camera and no Camera.main, so the sky colour stays as it is", this);

        ResolveWorld();

        RestoreFromSave();

        ApplySunState();

        RefreshTimeApi();
    }


    private void ResolveWorld()
    {
        SaveDataManager manager = SaveDataManager.Instance;
        save = manager != null ? manager.SaveData : null;
        world = save != null ? save.world : null;

        if (world == null)
        {
            Debug.LogWarning("DayNightManager: no SaveDataManager, so the time of day and play time will not be saved", this);
            world = new WorldState();
        }

        world.EnsureSections();
    }


    private void RestoreFromSave()
    {
        TimeSaveData time = world.time;
        if (time == null)
        {
            time = new TimeSaveData();
            world.time = time;
        }

        Day = Mathf.Max(1, time.day);

        float total = dayDuration + nightDuration;
        cycleTime = time.elapsedInCycle;
        if (float.IsNaN(cycleTime) || cycleTime < 0f) cycleTime = 0f;
        cycleTime %= total;
    }


    private void Update()
    {
        float dt = Time.deltaTime;   // scaled, so a paused game freezes the cycle like the old tweens

        if (dt > 0f)
        {
            float total = dayDuration + nightDuration;
            cycleTime += dt;
            while (cycleTime >= total)
            {
                cycleTime -= total;
                Day++;
                DayStarted?.Invoke();
            }
        }

        // play time counts in real time, but only while the game is not paused
        if (Time.timeScale > 0f && save != null)
        {
            playRemainder += Time.unscaledDeltaTime * 1000.0;
            long whole = (long)playRemainder;
            if (whole > 0)
            {
                save.playTimeElapsed += whole;
                playRemainder -= whole;
            }
        }

        ApplySunState();

        RefreshTimeApi();
    }


    private void LateUpdate()
    {
        if (world == null || world.time == null) return;

        world.time.day = Day;
        world.time.elapsedInCycle = cycleTime;
    }


    private void RefreshTimeApi()
    {
        DayPhase01 = cycleTime < dayDuration ? cycleTime / dayDuration : 1f;
        DialAngleDegrees = cycleTime < dayDuration
            ? -90f + 180f * (cycleTime / dayDuration)
            : 90f + 180f * ((cycleTime - dayDuration) / nightDuration);
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
