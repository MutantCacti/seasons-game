using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


// Mirrors the world in the sea. A hidden helper camera renders the view reflected in the sea's plane
// into a half-res texture, which the Glass style of Seasons/Sea mixes in through its fresnel sheen:
// where the mirror sees something it reflects that, where it sees nothing it reflects the sky.
//
// Turning this component off stops the helper render entirely and the sea falls back to the sky
// sheen alone. The sea material's Reflection Strength tunes it, 0 being off as well.
//
// Only the game camera sees reflections: every other camera (the Scene view, the helper itself) gets
// _SeaReflectionOn = 0, because the texture is rendered from the game camera's point of view.
[DefaultExecutionOrder(1000)]   // after everything that moves the camera or the entities this frame
public class SeaReflection : MonoBehaviour
{
    private static SeaReflection instance;
    public static SeaReflection Instance { get { return instance; } }


    private static readonly int SeaReflectionTexId = Shader.PropertyToID("_SeaReflectionTex");
    private static readonly int SeaReflectionOnId = Shader.PropertyToID("_SeaReflectionOn");


    [Header("References")]
    [SerializeField] private Camera sourceCamera;   // the game camera; Camera.main if left empty


    [Header("Reflection")]
    // what the mirror shows. Untick Terrain to compare entities only. Water and UI are always left
    // out, so the book is mirrored but its world space pages are not
    [SerializeField] private LayerMask reflectedLayers = ~0;

    [SerializeField, Range(0.1f, 1.0f)] private float resolutionScale = 0.5f;

    // world units the mirror's near plane sits above the water, so the waterline doesn't flicker
    [SerializeField] private float clipPlaneOffset = 0.01f;

    // -1 for the pipeline's default renderer, whose SSAO and book glow passes run for this camera
    // too, so the glow outline is mirrored along with the book. A bare renderer with no features
    // skips both
    [SerializeField] private int rendererIndex = -1;

    [SerializeField] private bool renderShadows = false;


    private Camera reflectionCamera;
    private RenderTexture target;
    private readonly UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest();

    private bool warnedAboutMissingCamera;
    private bool warnedAboutUnsupported;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            // just the component: it lives on the game camera, which must survive
            Debug.LogWarning("Found more than one SeaReflection in the scene - destroying newest!!");
            Destroy(this);
            return;
        }

        instance = this;
    }


    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }


    // zero cost while off: nothing renders, and the sea falls back to the sky sheen
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        Shader.SetGlobalFloat(SeaReflectionOnId, 0.0f);

        ReleaseTarget();
    }


    private void OnDestroy()
    {
        if (reflectionCamera != null) Destroy(reflectionCamera.gameObject);
        reflectionCamera = null;

        if (instance == this) instance = null;
    }


    // MARK: render


    private void LateUpdate()
    {
        Camera source = ResolveSource();
        if (source == null) return;

        SeaLevel sea = SeaLevel.Instance;
        if (sea == null || !sea.isActiveAndEnabled) return;

        EnsureCamera();
        if (!EnsureTarget(source)) return;

        request.destination = target;

        if (!RenderPipeline.SupportsRenderRequest(reflectionCamera, request))
        {
            if (!warnedAboutUnsupported)
            {
                warnedAboutUnsupported = true;
                Debug.LogError("SeaReflection: the render pipeline refused the reflection render request, so the sea only shows the sky sheen", this);
            }

            return;
        }

        // the sheet sits at the transform's y, which is what the shader measures from too
        Place(source, sea.transform.position.y);

        RenderPipeline.SubmitRenderRequest(reflectionCamera, request);

        Shader.SetGlobalTexture(SeaReflectionTexId, target);
    }


    // Per camera, and through the camera's own context, so the Scene view drawn in the same frame
    // sees the sky sheen alone rather than a mirror that doesn't match its view
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera rendering)
    {
        bool on = target != null && rendering == ResolveSource();

        CommandBuffer cmd = CommandBufferPool.Get();
        cmd.SetGlobalFloat(SeaReflectionOnId, on ? 1.0f : 0.0f);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }


    // The source camera's view mirrored in the plane y = seaY, with an oblique near plane on the water
    // so nothing under it is reflected.
    //
    // Mirroring flips the triangle winding, which would cull the terrain's front faces. Rather than
    // rely on GL.invertCulling, the projection is flipped vertically as well, which flips the winding
    // back; the sea samples the texture with v flipped to match.
    private void Place(Camera source, float seaY)
    {
        Matrix4x4 mirror = MirrorMatrix(seaY);

        Transform view = source.transform;
        Vector3 forward = Vector3.Reflect(view.forward, Vector3.up);
        Vector3 up = Vector3.Reflect(view.up, Vector3.up);

        // URP takes the camera position from the transform, and the view from the matrix below
        reflectionCamera.transform.SetPositionAndRotation(mirror.MultiplyPoint(view.position), Quaternion.LookRotation(forward, up));

        reflectionCamera.fieldOfView = source.fieldOfView;
        reflectionCamera.orthographic = source.orthographic;
        reflectionCamera.orthographicSize = source.orthographicSize;
        reflectionCamera.nearClipPlane = source.nearClipPlane;
        reflectionCamera.farClipPlane = source.farClipPlane;
        reflectionCamera.aspect = source.aspect;
        reflectionCamera.cullingMask = CullingMask();

        // alpha 0 marks where the mirror saw nothing, which the sea fills with the live sky colour
        Color clear = source.backgroundColor;
        clear.a = 0.0f;
        reflectionCamera.backgroundColor = clear;

        Matrix4x4 worldToCamera = source.worldToCameraMatrix * mirror;
        reflectionCamera.worldToCameraMatrix = worldToCamera;

        Vector4 clipPlane = CameraSpacePlane(worldToCamera, new Vector3(0.0f, seaY + clipPlaneOffset, 0.0f), Vector3.up);
        Matrix4x4 projection = source.CalculateObliqueMatrix(clipPlane);

        reflectionCamera.projectionMatrix = Matrix4x4.Scale(new Vector3(1.0f, -1.0f, 1.0f)) * projection;

        // culled with the plain frustum: the oblique one's far plane is skewed and would cull things
        // that are really in view
        reflectionCamera.cullingMatrix = source.projectionMatrix * worldToCamera;
    }


    private int CullingMask()
    {
        int mask = reflectedLayers.value;

        mask &= ~LayerBit("Water");
        mask &= ~LayerBit("UI");

        return mask;
    }


    private static int LayerBit(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? 1 << layer : 0;
    }


    // reflects through the horizontal plane y = seaY
    private static Matrix4x4 MirrorMatrix(float seaY)
    {
        Matrix4x4 mirror = Matrix4x4.identity;
        mirror.m11 = -1.0f;
        mirror.m13 = 2.0f * seaY;
        return mirror;
    }


    // a world plane through point with this normal, in the camera space of worldToCamera, as (n, d)
    private static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 point, Vector3 normal)
    {
        Vector3 cameraPoint = worldToCamera.MultiplyPoint(point);
        Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized;

        return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(cameraPoint, cameraNormal));
    }


    // MARK: internals


    private Camera ResolveSource()
    {
        if (sourceCamera == null) sourceCamera = Camera.main;

        if (sourceCamera == null && !warnedAboutMissingCamera)
        {
            warnedAboutMissingCamera = true;
            Debug.LogError("SeaReflection: no sourceCamera and no camera tagged MainCamera, so the sea reflects nothing", this);
        }

        return sourceCamera;
    }


    // never enabled: it only ever renders when asked to, from LateUpdate
    private void EnsureCamera()
    {
        if (reflectionCamera != null) return;

        GameObject holder = new GameObject("Sea Reflection Camera");
        holder.hideFlags = HideFlags.HideAndDontSave;

        reflectionCamera = holder.AddComponent<Camera>();
        reflectionCamera.enabled = false;
        reflectionCamera.clearFlags = CameraClearFlags.SolidColor;
        reflectionCamera.allowHDR = false;
        reflectionCamera.allowMSAA = false;
        reflectionCamera.useOcclusionCulling = false;

        UniversalAdditionalCameraData data = holder.AddComponent<UniversalAdditionalCameraData>();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.renderShadows = renderShadows;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.antialiasing = AntialiasingMode.None;
        if (rendererIndex >= 0) data.SetRenderer(rendererIndex);
    }


    private bool EnsureTarget(Camera source)
    {
        int width = Mathf.Max(1, Mathf.RoundToInt(source.pixelWidth * resolutionScale));
        int height = Mathf.Max(1, Mathf.RoundToInt(source.pixelHeight * resolutionScale));

        if (target != null && target.width == width && target.height == height) return true;

        ReleaseTarget();

        // alpha is the mirror's coverage, and point filtering keeps it stepped like the pixel art
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.name = "Sea Reflection";
        target.filterMode = FilterMode.Point;
        target.wrapMode = TextureWrapMode.Clamp;

        return target.Create();
    }


    private void ReleaseTarget()
    {
        if (target == null) return;

        if (reflectionCamera != null) reflectionCamera.targetTexture = null;

        target.Release();
        Destroy(target);
        target = null;
    }
}
