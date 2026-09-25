using TMPro;
using UnityEngine;
using UnityEngine.UI;


// Lays out and drives the on-screen clock from the frame's rect and DayNightManager's time API.
// The human only nests the objects and drags references; every position and size is computed here,
// so none of them need to be typed in the Inspector.
public class Clock : MonoBehaviour
{
    [SerializeField] private RectTransform frame;   // falls back to this transform when null
    [SerializeField] private Image frameFill;       // the white inset, falls back to the frame's own Image
    [SerializeField] private Image border;          // the ring around the frame, drawn above the window
    [SerializeField] private RectTransform window;  // the Mask that clips the dial to the circle
    [SerializeField] private RectTransform dial;
    [SerializeField] private Image daylight;
    [SerializeField] private Image nightMask;
    [SerializeField] private Image starsClouds;
    [SerializeField] private Image sun;
    [SerializeField] private Image moon;
    [SerializeField] private TextMeshProUGUI dayText;

    // muted to sit with the rest of the UI (the text is the options menu's dark grey). the sun is a
    // deep amber: the compendium's gold had the same luminance as the sky and vanished into it
    [SerializeField] private Color daylightColour = new Color(169.0f / 255.0f, 207.0f / 255.0f, 220.0f / 255.0f, 1.0f);  // #A9CFDC
    [SerializeField] private Color nightColour = new Color(60.0f / 255.0f, 65.0f / 255.0f, 112.0f / 255.0f, 1.0f);       // #3C4170
    [SerializeField] private Color starsColour = new Color(255.0f / 255.0f, 247.0f / 255.0f, 230.0f / 255.0f, 1.0f);     // #FFF7E6
    [SerializeField] private Color sunColour = new Color(217.0f / 255.0f, 140.0f / 255.0f, 30.0f / 255.0f, 1.0f);        // #D98C1E
    [SerializeField] private Color moonColour = new Color(243.0f / 255.0f, 236.0f / 255.0f, 214.0f / 255.0f, 1.0f);      // #F3ECD6
    [SerializeField] private Color textColour = new Color(50.0f / 255.0f, 50.0f / 255.0f, 50.0f / 255.0f, 1.0f);         // #323232

    [SerializeField] private Color fillColour = Color.white;

    // the border flips between these as the sky passes its midpoint, so it always contrasts with it.
    // the flip is centred on borderSwitchAt of DayNightManager.Daylight01 and takes borderSwitchWidth
    // of it, which is a couple of seconds of the sky's much slower blend
    [SerializeField] private Color borderDayColour = new Color(50.0f / 255.0f, 50.0f / 255.0f, 50.0f / 255.0f, 1.0f);      // #323232
    [SerializeField] private Color borderNightColour = new Color(242.0f / 255.0f, 242.0f / 255.0f, 242.0f / 255.0f, 1.0f);  // #F2F2F2
    [SerializeField] [Range(0.0f, 1.0f)] private float borderSwitchAt = 0.5f;
    [SerializeField] [Range(0.01f, 1.0f)] private float borderSwitchWidth = 0.2f;

    // the sun and moon sprites, as a fraction of the frame height. the sun art fills almost its
    // whole canvas, so at 1 it covered the entire window
    [SerializeField] [Range(0.1f, 1.0f)] private float celestialSize = 0.4f;


    private DayNightManager dayNight;
    private bool validationLogged;


    private void Awake()
    {
        if (frameFill == null) frameFill = GetComponent<Image>();

        ValidateHierarchy();
        ApplyLayout();
    }


    private void OnEnable()
    {
        ApplyLayout();
    }


    // Pitfall 2: a layout-driven rect is 0x0 at bind time, so re-measure whenever the frame resizes.
    private void OnRectTransformDimensionsChange()
    {
        ApplyLayout();
    }


    private void Start()
    {
        dayNight = DayNightManager.Instance;

        if (dayNight == null)
        {
            Debug.LogError("Clock: no DayNightManager in the scene, so the clock cannot run", this);
            enabled = false;
            return;
        }

        ApplyColours();

        if (dayText != null) dayText.text = $"Day {dayNight.Day}";

        dayNight.DayStarted += OnDayStarted;
    }


    private void OnDestroy()
    {
        if (dayNight != null) dayNight.DayStarted -= OnDayStarted;
    }


    private void Update()
    {
        if (dayNight == null || dial == null) return;

        // positive Z is counter-clockwise in screen space
        dial.localRotation = Quaternion.Euler(0f, 0f, dayNight.DialAngleDegrees);

        ApplyBorderColour();
    }


    private void ApplyBorderColour()
    {
        if (border == null) return;

        float half = borderSwitchWidth * 0.5f;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(borderSwitchAt - half, borderSwitchAt + half, dayNight.Daylight01));

        border.color = Color.Lerp(borderNightColour, borderDayColour, t);
    }


    private void OnDayStarted()
    {
        if (dayText != null) dayText.text = $"Day {dayNight.Day}";
    }


    // MARK: layout


    // Sizes and positions the dial, sun and moon from the frame's rect using the art's measured
    // ratios, measured from the frame's bottom-left:
    //   - the dial's centre sits at (128/512, 16/256) of the frame = (128, 240) top-left at native
    //   - the dial is a 512/256 x 512/256 square (twice the frame height), so it always covers the
    //     window circle (whose farthest point is 224/256 of the frame height from the pivot)
    //   - the sun and moon sit at +/-112/256 of the frame height on the dial's local y, and are
    //     sized celestialSize of the frame height
    private void ApplyLayout()
    {
        RectTransform frameRt = frame != null ? frame : transform as RectTransform;
        if (frameRt == null) return;

        float w = frameRt.rect.width;
        float h = frameRt.rect.height;
        if (w <= 0f || h <= 0f) return;

        if (dial != null)
        {
            dial.anchorMin = dial.anchorMax = new Vector2(0.5f, 0.5f);
            dial.pivot = new Vector2(0.5f, 0.5f);
            dial.sizeDelta = new Vector2(2f * h, 2f * h);
            dial.anchoredPosition = new Vector2(-0.25f * w, -0.4375f * h);
        }

        float sunMoonOffset = 112f / 256f * h;
        float celestial = celestialSize * h;

        if (sun != null)
        {
            sun.rectTransform.anchorMin = sun.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sun.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            sun.rectTransform.sizeDelta = new Vector2(celestial, celestial);
            sun.rectTransform.anchoredPosition = new Vector2(0f, sunMoonOffset);
        }

        if (moon != null)
        {
            moon.rectTransform.anchorMin = moon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            moon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            moon.rectTransform.sizeDelta = new Vector2(celestial, celestial);
            moon.rectTransform.anchoredPosition = new Vector2(0f, -sunMoonOffset);
        }
    }


    private void ValidateHierarchy()
    {
        if (validationLogged) return;
        validationLogged = true;

        if (border == null)
        {
            Debug.LogError("Clock: no Border image, so the frame has no outline", this);
        }

        if (window == null)
        {
            Debug.LogError("Clock: no Window referenced, so the dial is not masked to the circle", this);
            return;
        }

        if (window.GetComponent<Mask>() == null)
        {
            Debug.LogError("Clock: the Window has no Mask, so the dial is not clipped to the circle", this);
        }

        if (dial != null && dial.parent != window)
        {
            Debug.LogError("Clock: the dial is not a child of the Window, so the mask cannot clip it", this);
        }
    }


    private void ApplyColours()
    {
        if (frameFill != null) frameFill.color = fillColour;
        ApplyBorderColour();
        if (daylight != null) daylight.color = daylightColour;
        if (nightMask != null) nightMask.color = nightColour;
        if (starsClouds != null) starsClouds.color = starsColour;
        if (sun != null) sun.color = sunColour;
        if (moon != null) moon.color = moonColour;
        if (dayText != null) dayText.color = textColour;
    }
}
