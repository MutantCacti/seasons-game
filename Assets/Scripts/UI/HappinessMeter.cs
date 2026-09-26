using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;


// A 0..1 meter: the fill is a gradient from sadColour on the left to happyColour on the right, drawn
// once into a small texture, and cut off at the value, so a fuller bar also ends in a happier colour.
// The fill is a Filled image rather than a Slider's stretched fill rect, which would squeeze the whole
// gradient into the filled part.
//
// The gradient runs through hue (the short way round) rather than straight between the two colours,
// so red to green passes through warm yellow instead of mud. Everything around the fill (background,
// frame) is plain art on sibling images.
public class HappinessMeter : MonoBehaviour
{
    [SerializeField] private Image fill;
    [SerializeField] private Color sadColour = new Color(210.0f / 255.0f, 87.0f / 255.0f, 63.0f / 255.0f, 1.0f);      // #D2573F, the fail ping's brick red
    [SerializeField] private Color happyColour = new Color(141.0f / 255.0f, 191.0f / 255.0f, 90.0f / 255.0f, 1.0f);   // #8DBF5A, the complete ping's leaf green
    [SerializeField] [Min(2)] private int gradientSteps = 64;
    [SerializeField] private float fillDuration = 0.35f;


    private Texture2D gradient;
    private Sprite gradientSprite;
    private float value;
    private Tween fillTween;


    public float Value { get { return value; } }


    private void Awake()
    {
        if (fill == null)
        {
            Debug.LogError("HappinessMeter: no fill Image, so happiness is never shown", this);
            return;
        }

        BuildGradient();

        fill.sprite = gradientSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.color = Color.white;
        fill.fillAmount = value;
    }


    private void OnDestroy()
    {
        if (fillTween != null) fillTween.Kill();

        if (gradientSprite != null) Destroy(gradientSprite);
        if (gradient != null) Destroy(gradient);
    }


    // animated eases the fill there; from sets where it starts, e.g. 0 so the bar fills up as it opens
    public void SetValue(float target, bool animated, float from = -1.0f)
    {
        target = Mathf.Clamp01(target);

        if (from >= 0.0f) Apply(Mathf.Clamp01(from));

        if (fillTween != null) fillTween.Kill();

        if (!animated)
        {
            Apply(target);
            return;
        }

        // unscaled: the panel works while the game is paused
        fillTween = DOTween.To(() => value, Apply, target, fillDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject);
    }


    private void Apply(float amount)
    {
        value = amount;
        if (fill != null) fill.fillAmount = amount;
    }


    // one row, gradientSteps wide, sRGB like any art, stretched over the fill with bilinear filtering
    private void BuildGradient()
    {
        gradient = new Texture2D(gradientSteps, 1, TextureFormat.RGBA32, false)
        {
            name = "Happiness Gradient",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        Color.RGBToHSV(sadColour, out float sadHue, out float sadSaturation, out float sadValue);
        Color.RGBToHSV(happyColour, out float happyHue, out float happySaturation, out float happyValue);

        // the short way round the hue circle
        if (happyHue - sadHue > 0.5f) sadHue += 1.0f;
        else if (sadHue - happyHue > 0.5f) happyHue += 1.0f;

        for (int i = 0; i < gradientSteps; i++)
        {
            float t = i / (float)(gradientSteps - 1);

            float hue = Mathf.Repeat(Mathf.Lerp(sadHue, happyHue, t), 1.0f);
            Color colour = Color.HSVToRGB(hue, Mathf.Lerp(sadSaturation, happySaturation, t), Mathf.Lerp(sadValue, happyValue, t));
            colour.a = Mathf.Lerp(sadColour.a, happyColour.a, t);

            gradient.SetPixel(i, 0, colour);
        }

        gradient.Apply(false, true);

        gradientSprite = Sprite.Create(gradient, new Rect(0.0f, 0.0f, gradientSteps, 1.0f), new Vector2(0.5f, 0.5f), 100.0f);
        gradientSprite.name = "Happiness Gradient";
    }
}
