using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;


// A highlighted animal's glow: a quad on a child of its sprite, drawn with Seasons/SpriteGlow, grown
// past the sprite's rect so the halo has room, with the sprite's UVs carried on past its edge so the
// glow traces the drawn pixels. It faces the camera with the sprite (it is a child of it), follows its
// wiggle flips and sprite swaps, and keeps glowing while the sprite is hidden: then the silhouette
// fills in and it draws through the ground, so a burrowed worm still shows where it is.
//
// AnimalSelection adds and drives it; it has its own material, since a hidden and a shown glow need a
// different ZTest and that is render state, not something a property block can change.
public class AnimalGlow : MonoBehaviour
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int MaxWidthId = Shader.PropertyToID("_MaxWidth");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int GlowRadiusId = Shader.PropertyToID("_GlowRadius");
    private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
    private static readonly int UVRectId = Shader.PropertyToID("_UVRect");
    private static readonly int UVPerUnitId = Shader.PropertyToID("_UVPerUnit");
    private static readonly int HiddenId = Shader.PropertyToID("_Hidden");
    private static readonly int HiddenFillId = Shader.PropertyToID("_HiddenFill");
    private static readonly int FlareId = Shader.PropertyToID("_Flare");
    private static readonly int FlareWidthId = Shader.PropertyToID("_FlareWidth");
    private static readonly int ZTestId = Shader.PropertyToID("_ZTest");


    public struct Settings
    {
        public Shader shader;
        public Color colour;
        public float radius;        // world units the halo reaches past the sprite
        public float width;         // world units the bright core pulses out to
        public float intensity;
        public float pulseSpeed;
        public float hiddenFill;    // how strongly a hidden animal's own shape glows
        public float flareWidth;    // world units the core bursts out by when switched on
        public float flareDuration;
    }


    private Settings settings;
    private SpriteRenderer source;

    private Transform glowTransform;
    private MeshRenderer glowRenderer;
    private Mesh mesh;
    private Material material;

    private Sprite builtSprite;
    private bool builtFlip;
    private bool builtHidden;
    private bool built;

    private float flare;
    private Tween flareTween;

    private readonly Vector3[] vertices = new Vector3[4];
    private readonly Vector2[] uvs = new Vector2[4];
    private static readonly int[] TRIANGLES = { 0, 2, 1, 1, 2, 3 };


    public bool IsShowing { get { return glowRenderer != null && glowRenderer.enabled; } }


    // false when there is nothing to glow around or nothing to draw it with; the caller removes it
    public bool Init(Settings glowSettings)
    {
        settings = glowSettings;
        source = GetComponentInChildren<SpriteRenderer>(true);

        if (source == null)
        {
            Debug.LogError($"AnimalGlow: '{name}' has no SpriteRenderer, so it has no outline to glow around", this);
            return false;
        }

        if (settings.shader == null)
        {
            Debug.LogError("AnimalGlow: no glow shader, so highlighted animals cannot glow", this);
            return false;
        }

        GameObject holder = new GameObject("Highlight Glow");
        holder.layer = source.gameObject.layer;

        glowTransform = holder.transform;
        glowTransform.SetParent(source.transform, false);

        mesh = new Mesh { name = "Highlight Glow" };
        mesh.MarkDynamic();

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;

        glowRenderer = holder.AddComponent<MeshRenderer>();
        glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;
        glowRenderer.enabled = false;

        material = new Material(settings.shader) { name = "Highlight Glow" };
        material.SetColor(ColorId, settings.colour);
        material.SetFloat(PulseSpeedId, settings.pulseSpeed);
        material.SetFloat(GlowIntensityId, settings.intensity);
        material.SetFloat(HiddenFillId, settings.hiddenFill);

        glowRenderer.sharedMaterial = material;

        return true;
    }


    private void OnDestroy()
    {
        if (flareTween != null) flareTween.Kill();

        if (glowTransform != null) Destroy(glowTransform.gameObject);
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }


    // MARK: show and hide


    // burst: the highlight was just switched on, so the core runs out from the outline and settles
    public void Show(bool burst)
    {
        if (glowRenderer == null) return;

        glowRenderer.enabled = true;
        built = false;

        if (flareTween != null) flareTween.Kill();

        flare = burst ? 1.0f : 0.0f;

        if (burst)
        {
            flareTween = DOTween.To(() => flare, value => flare = value, 0.0f, settings.flareDuration)
                .SetEase(Ease.OutCubic)
                .SetLink(gameObject);
        }

        Sync();
    }


    public void Hide()
    {
        if (glowRenderer == null) return;

        if (flareTween != null) flareTween.Kill();
        glowRenderer.enabled = false;
    }


    private void LateUpdate()
    {
        if (IsShowing) Sync();
    }


    // MARK: sync


    private void Sync()
    {
        if (source == null) return;

        Sprite sprite = source.sprite;
        if (sprite == null) return;

        bool hidden = !source.enabled || !source.gameObject.activeInHierarchy;

        if (!built || sprite != builtSprite || source.flipX != builtFlip) Build(sprite, source.flipX);

        if (!built || hidden != builtHidden)
        {
            material.SetFloat(HiddenId, hidden ? 1.0f : 0.0f);

            // hidden underground, it draws through the ground so it shows where the animal is
            material.SetFloat(ZTestId, (float)(hidden ? CompareFunction.Always : CompareFunction.LessEqual));

            builtHidden = hidden;
        }

        material.SetFloat(FlareId, flare);

        built = true;
    }


    // The sprite's rect in its local units (from its pixel rect, pivot and pixels per unit, which
    // match the outer UVs even for a tight mesh), grown by the radius, with the UVs carried on
    private void Build(Sprite sprite, bool flip)
    {
        float scale = Mathf.Max(1e-4f, Mathf.Abs(source.transform.lossyScale.x));
        float radius = settings.radius / scale;

        float pixelsPerUnit = sprite.pixelsPerUnit;
        Vector2 min = -sprite.pivot / pixelsPerUnit;
        Vector2 size = sprite.rect.size / pixelsPerUnit;

        Vector4 outer = DataUtility.GetOuterUV(sprite);
        Vector2 uvPerUnit = new Vector2((outer.z - outer.x) / Mathf.Max(size.x, 1e-4f), (outer.w - outer.y) / Mathf.Max(size.y, 1e-4f));

        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = new Vector2(
                (i & 1) == 0 ? min.x - radius : min.x + size.x + radius,
                (i & 2) == 0 ? min.y - radius : min.y + size.y + radius);

            uvs[i] = new Vector2(outer.x + (corner.x - min.x) * uvPerUnit.x, outer.y + (corner.y - min.y) * uvPerUnit.y);

            // flipX mirrors the drawing through the pivot; the quad is two sided, so the winding can flip
            vertices[i] = new Vector3(flip ? -corner.x : corner.x, corner.y, 0.0f);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = TRIANGLES;
        mesh.RecalculateBounds();

        material.SetTexture(MainTexId, sprite.texture);
        material.SetVector(UVRectId, outer);
        material.SetVector(UVPerUnitId, new Vector4(uvPerUnit.x, uvPerUnit.y, 0.0f, 0.0f));
        material.SetFloat(GlowRadiusId, radius);
        material.SetFloat(MaxWidthId, settings.width / scale);
        material.SetFloat(FlareWidthId, settings.flareWidth / scale);

        builtSprite = sprite;
        builtFlip = flip;
    }
}
