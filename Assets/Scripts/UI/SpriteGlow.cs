using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;


// Pulses a glow around the silhouette of the Image on its parent: the tab's shape, an arrow, anything
// a Simple Image draws. It only reflects state (Show/Hide), it never clears itself on hover. Sits on an
// empty child of the Image and sets itself up, so adding the component is the whole setup.
//
// The quad is the Image's drawn rect grown by glowRadius, with the sprite's UVs carried on past its
// edge; UI/SpriteGlow then samples the sprite's alpha around each pixel. That drawn rect and those UVs
// are worked out exactly as Image.GenerateSimpleSprite does (GetDrawingDimensions with the sprite's
// padding and preserveAspect, DataUtility.GetOuterUV), see com.unity.ugui Runtime/UGUI/UI/Core/Image.cs.
public class SpriteGlow : MaskableGraphic
{
    [SerializeField] private Shader glowShader;
    [SerializeField] private float glowRadius = 24.0f;       // rect units the quad grows by; the halo fades out at its edge
    [SerializeField] private float maxWidth = 6.0f;          // rect units the bright core pulses out to
    [SerializeField] private float pulseSpeed = 8.0f;        // same as UnseenHighlight and BookGlow, so everything pulses together
    [SerializeField] private float glowIntensity = 0.8f;

    private const string SHADER_NAME = "UI/SpriteGlow";

    private static readonly int UVRectId = Shader.PropertyToID("_UVRect");
    private static readonly int UVPerUnitId = Shader.PropertyToID("_UVPerUnit");
    private static readonly int MaxWidthId = Shader.PropertyToID("_MaxWidth");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int GlowRadiusId = Shader.PropertyToID("_GlowRadius");
    private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");


    private Image target;
    private Material glowMaterial;
    private bool warnedAboutTarget;
    private bool warnedAboutShader;
    private bool warnedAboutType;

    // The wanted state. Not serialized and never reset in Awake: the book binds while its canvas is
    // inactive, so Show can run before Awake. Hidden draws an empty mesh, so edit mode shows nothing.
    private bool showing;


    public bool IsShowing { get { return showing; } }


    // MARK: lifecycle


    protected override void Awake()
    {
        base.Awake();

        raycastTarget = false;

        // edit mode too (Graphic is ExecuteAlways), but only play mode moves the rect, so opening
        // the prefab never dirties it
        if (Application.isPlaying) FillParent();
    }


    protected override void OnEnable()
    {
        // before base.OnEnable, which is what registers this with the raycaster
        raycastTarget = false;

        base.OnEnable();
    }


    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (glowMaterial == null) return;

        if (Application.isPlaying) Destroy(glowMaterial);
        else DestroyImmediate(glowMaterial);

        glowMaterial = null;
    }


#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();

        glowShader = Shader.Find(SHADER_NAME);
        raycastTarget = false;
        FillParent();
    }


    protected override void OnValidate()
    {
        // the quad covers far more than its button (a tab's covers most of the page), so a raycast
        // target here would swallow clicks meant for the pages, the entries and the other tabs
        raycastTarget = false;

        if (glowShader == null) glowShader = Shader.Find(SHADER_NAME);

        base.OnValidate();

        ApplySettings();
    }
#endif


    // The last word on clicks: even if something turns raycastTarget back on, a hit never lands here
    public override bool Raycast(Vector2 sp, Camera eventCamera)
    {
        return false;
    }


    // MARK: state


    public void Show(Color colour)
    {
        if (showing && color == colour) return;

        showing = true;
        color = colour;
        SetVerticesDirty();
    }


    public void Hide()
    {
        if (!showing) return;

        showing = false;
        SetVerticesDirty();
    }


    // MARK: drawing


    // the sprite's texture, so the shader can read its alpha; spriteless Images draw white
    public override Texture mainTexture
    {
        get
        {
            Image image = Target;
            Sprite sprite = image != null ? image.overrideSprite : null;

            return sprite != null ? sprite.texture : Texture2D.whiteTexture;
        }
    }


    // a per-instance material handed out as the default, so nothing is ever serialized into m_Material
    public override Material defaultMaterial
    {
        get
        {
            if (glowMaterial == null && glowShader != null)
            {
                glowMaterial = new Material(glowShader);
                glowMaterial.hideFlags = HideFlags.DontSave;
                ApplySettings();
            }

            return glowMaterial != null ? glowMaterial : base.defaultMaterial;
        }
    }


    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (!showing) return;

        Image image = Target;
        if (image == null) return;

        if (glowShader == null)
        {
            if (!warnedAboutShader)
            {
                warnedAboutShader = true;
                Debug.LogError("SpriteGlow: no glow shader, so nothing can glow - re-add the component or assign UI/SpriteGlow", this);
            }

            return;
        }

        if (image.type != Image.Type.Simple && !warnedAboutType)
        {
            warnedAboutType = true;
            Debug.LogWarning($"SpriteGlow: '{image.name}' is not a Simple Image, so the glow traces the sprite as if it were stretched", this);
        }

        Vector4 drawn = DrawnRect(image);
        Vector4 uv = OuterUV(image);

        float width = drawn.z - drawn.x;
        float height = drawn.w - drawn.y;
        if (width <= 0.0f || height <= 0.0f) return;

        Vector2 uvPerUnit = new Vector2((uv.z - uv.x) / width, (uv.w - uv.y) / height);

        Material glow = defaultMaterial;
        if (glow != null)
        {
            glow.SetVector(UVRectId, uv);
            glow.SetVector(UVPerUnitId, new Vector4(uvPerUnit.x, uvPerUnit.y, 0.0f, 0.0f));
        }

        // Positions are in the Image's local space, which is this rect's too: FillParent keeps the
        // pivot, anchors and offsets matched. The UVs just keep going past the sprite's edge.
        float pad = Mathf.Max(glowRadius, 0.0f);
        float left = drawn.x - pad;
        float bottom = drawn.y - pad;
        float right = drawn.z + pad;
        float top = drawn.w + pad;

        Color32 colour32 = color;

        vh.AddVert(new Vector3(left, bottom), colour32, new Vector2(uv.x - pad * uvPerUnit.x, uv.y - pad * uvPerUnit.y));
        vh.AddVert(new Vector3(left, top), colour32, new Vector2(uv.x - pad * uvPerUnit.x, uv.w + pad * uvPerUnit.y));
        vh.AddVert(new Vector3(right, top), colour32, new Vector2(uv.z + pad * uvPerUnit.x, uv.w + pad * uvPerUnit.y));
        vh.AddVert(new Vector3(right, bottom), colour32, new Vector2(uv.z + pad * uvPerUnit.x, uv.y - pad * uvPerUnit.y));

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }


    // Image.GetDrawingDimensions, which is private: x = left, y = bottom, z = right, w = top
    private static Vector4 DrawnRect(Image image)
    {
        Rect r = image.GetPixelAdjustedRect();
        Sprite sprite = image.overrideSprite;

        // a spriteless Image fills its whole rect
        if (sprite == null) return new Vector4(r.xMin, r.yMin, r.xMax, r.yMax);

        Vector4 padding = DataUtility.GetPadding(sprite);
        Vector2 size = new Vector2(sprite.rect.width, sprite.rect.height);

        int spriteW = Mathf.RoundToInt(size.x);
        int spriteH = Mathf.RoundToInt(size.y);
        if (spriteW <= 0 || spriteH <= 0) return new Vector4(r.xMin, r.yMin, r.xMax, r.yMax);

        Vector4 v = new Vector4(
            padding.x / spriteW,
            padding.y / spriteH,
            (spriteW - padding.z) / spriteW,
            (spriteH - padding.w) / spriteH);

        if (image.preserveAspect && image.type == Image.Type.Simple) PreserveAspect(ref r, size, image.rectTransform.pivot);

        return new Vector4(
            r.x + r.width * v.x,
            r.y + r.height * v.y,
            r.x + r.width * v.z,
            r.y + r.height * v.w);
    }


    // Image.PreserveSpriteAspectRatio, also private
    private static void PreserveAspect(ref Rect rect, Vector2 spriteSize, Vector2 pivot)
    {
        float spriteRatio = spriteSize.x / spriteSize.y;
        float rectRatio = rect.width / rect.height;

        if (spriteRatio > rectRatio)
        {
            float oldHeight = rect.height;
            rect.height = rect.width * (1.0f / spriteRatio);
            rect.y += (oldHeight - rect.height) * pivot.y;
        }
        else
        {
            float oldWidth = rect.width;
            rect.width = rect.height * spriteRatio;
            rect.x += (oldWidth - rect.width) * pivot.x;
        }
    }


    // where the sprite sits in its texture (or atlas page); a spriteless Image spans the white texture
    private static Vector4 OuterUV(Image image)
    {
        Sprite sprite = image.overrideSprite;
        return sprite != null ? DataUtility.GetOuterUV(sprite) : new Vector4(0.0f, 0.0f, 1.0f, 1.0f);
    }


    private void ApplySettings()
    {
        if (glowMaterial == null) return;

        glowMaterial.SetFloat(MaxWidthId, maxWidth);
        glowMaterial.SetFloat(PulseSpeedId, pulseSpeed);
        glowMaterial.SetFloat(GlowRadiusId, Mathf.Max(glowRadius, 0.0f));
        glowMaterial.SetFloat(GlowIntensityId, glowIntensity);
    }


    // MARK: setup


    // The Image on the parent. Looked up lazily so a Show before Awake still finds it.
    private Image Target
    {
        get
        {
            if (target != null) return target;

            Transform parent = transform.parent;
            target = parent != null ? parent.GetComponent<Image>() : null;

            if (target == null && !warnedAboutTarget)
            {
                warnedAboutTarget = true;
                Debug.LogError("SpriteGlow: no Image on the parent, so there is no silhouette to glow around", this);
            }

            return target;
        }
    }


    // Stretched over the parent with the parent's pivot, so both rects share one local space and the
    // quad (worked out in the Image's space) lands where the Image draws. Growing past the rect is
    // done by the mesh, not the rect.
    private void FillParent()
    {
        RectTransform rect = rectTransform;
        RectTransform parent = rect.parent as RectTransform;
        if (parent == null) return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = parent.pivot;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        Vector3 position = rect.localPosition;
        rect.localPosition = new Vector3(position.x, position.y, 0.0f);
    }


    protected override void OnTransformParentChanged()
    {
        target = null;
        warnedAboutTarget = false;

        base.OnTransformParentChanged();
    }
}
