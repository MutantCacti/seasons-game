using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


// One map-marker ping on the world space ping canvas. NotificationManager drives it through Tick.
//
// Two poses, both worked out in screen space each frame:
//   hover - the tip just above the target, facing the camera, bobbing
//   edge  - clinging to the edge of the usable screen, rolled so the tip points at the target
// and a blend between them, so the flight from one to the other follows the view as it moves.
// A ping whose target cannot be found is parked just above the inventory and opens when clicked.
[RequireComponent(typeof(RectTransform))]
public class NotificationPing : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Button button;
    [SerializeField] private Image body;
    [SerializeField] private Image icon;


    [Header("Feel")]
    [SerializeField] private float growOvershoot = 1.2f;
    [SerializeField] private float growDuration = 0.22f;
    [SerializeField] private float growSettleDuration = 0.12f;
    [SerializeField] private float pointerGrow = 1.1f;
    [SerializeField] private float pointerGrowDuration = 0.12f;
    [SerializeField] private float exitPunch = 1.25f;
    [SerializeField] private float exitPunchDuration = 0.08f;
    [SerializeField] private float exitDuration = 0.18f;


    // extra pixels a target must be inside the usable area before the ping flies in, so it cannot
    // flicker between poses right on the boundary
    private const float HOVER_HYSTERESIS = 12.0f;
    private const float RESOLVE_INTERVAL = 0.5f;


    private NotificationManager manager;
    private NotificationType type;
    private string targetGuid;
    private string puzzleId;

    private RectTransform rectTransform;

    private EntityId target;
    private Renderer[] renderers;
    private float nextResolveAt;

    private bool initialised;
    private bool onScreen;
    private float hoverBlend;           // 0 edge, 1 hover
    private Tween blendTween;

    private float presence;             // spawn and exit scale
    private Tween presenceTween;

    private float pointerScale = 1.0f;
    private Tween pointerTween;

    private bool exiting;
    private float bobPhase;

    private Vector2 smoothedEdge;
    private float smoothedRoll;


    public NotificationType Type { get { return type; } }
    public string PuzzleId { get { return puzzleId; } }
    public bool IsExiting { get { return exiting; } }
    public bool IsOnScreen { get { return onScreen; } }
    public bool HasTarget { get { return target != null; } }
    public bool IsParked { get { return target == null && !exiting; } }
    public Vector3 TargetPosition { get { return target != null ? Anchor() : transform.position; } }


    public void Init(NotificationManager owner, NotificationType notificationType, string entityGuid, string id,
        Sprite iconSprite, Color tint, float delay, SoundObject spawnSound)
    {
        manager = owner;
        type = notificationType;
        targetGuid = entityGuid;
        puzzleId = id;

        rectTransform = transform as RectTransform;
        bobPhase = Random.value * Mathf.PI * 2.0f;

        if (button == null) button = GetComponent<Button>();
        if (body == null) body = GetComponent<Image>();

        if (button != null) button.onClick.AddListener(OnClick);
        else Debug.LogError("NotificationPing: no Button, so this ping can never be opened", this);

        if (icon != null)
        {
            icon.sprite = iconSprite;
            icon.color = tint;
            icon.enabled = iconSprite != null;
        }
        else
        {
            Debug.LogError("NotificationPing: no icon Image, so the ping cannot show what kind it is", this);
        }

        TryResolve();

        // hidden until its grow starts, so a staggered ping does not sit there full size first
        presence = 0.0f;
        transform.localScale = Vector3.zero;

        Sequence grow = DOTween.Sequence();
        if (delay > 0.0f) grow.AppendInterval(delay);
        if (spawnSound != null) grow.AppendCallback(() => manager.PlaySound(spawnSound, this));
        grow.Append(DOTween.To(() => presence, value => presence = value, growOvershoot, growDuration).SetEase(Ease.OutQuad));
        grow.Append(DOTween.To(() => presence, value => presence = value, 1.0f, growSettleDuration).SetEase(Ease.InQuad));
        grow.SetLink(gameObject);

        presenceTween = grow;
    }


    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(OnClick);

        Unbind();

        if (manager != null) manager.Forget(this);
    }


    // MARK: click


    private void OnClick()
    {
        if (exiting || manager == null) return;

        // over its target (or parked, with nowhere to go) it opens; at the edge it takes you there
        if (target == null || hoverBlend > 0.5f)
        {
            manager.OnPingClicked(this);
            return;
        }

        manager.PanTo(target.transform);
    }


    public void OnPointerEnter(PointerEventData eventData)
    {
        TweenPointerScale(pointerGrow);
    }


    public void OnPointerExit(PointerEventData eventData)
    {
        TweenPointerScale(1.0f);
    }


    private void TweenPointerScale(float to)
    {
        if (exiting) return;

        if (pointerTween != null) pointerTween.Kill();

        pointerTween = DOTween.To(() => pointerScale, value => pointerScale = value, to, pointerGrowDuration)
            .SetEase(Ease.OutQuad)
            .SetLink(gameObject);
    }


    // A little pop and then gone
    public void Exit()
    {
        if (exiting) return;
        exiting = true;

        if (button != null) button.interactable = false;

        if (presenceTween != null) presenceTween.Kill();
        if (pointerTween != null) pointerTween.Kill();

        Sequence pop = DOTween.Sequence();
        pop.Append(DOTween.To(() => presence, value => presence = value, presence * exitPunch, exitPunchDuration).SetEase(Ease.OutQuad));
        pop.Append(DOTween.To(() => presence, value => presence = value, 0.0f, exitDuration).SetEase(Ease.InBack));
        pop.OnComplete(() => Destroy(gameObject));
        pop.SetLink(gameObject);

        presenceTween = pop;
    }


    // MARK: pose


    public void Tick()
    {
        if (manager == null) return;

        if (target == null && !exiting && Time.time >= nextResolveAt)
        {
            nextResolveAt = Time.time + RESOLVE_INTERVAL;
            TryResolve();
        }

        Camera viewCamera = manager.ViewCamera;
        if (viewCamera == null || rectTransform == null) return;

        float screenHeight = Screen.height;
        Vector2 centre = new Vector2(Screen.width * 0.5f, screenHeight * 0.5f);

        float aspect = rectTransform.rect.height > 0.0f ? rectTransform.rect.width / rectTransform.rect.height : 1.0f;
        float edgePixels = manager.EdgePingPixels;

        Vector2 edgePoint;
        float edgeRoll;

        Vector2 hoverPoint;
        float hoverDepth;
        float hoverPixels;
        bool wantHover = false;

        if (target != null)
        {
            Vector3 tip = Anchor() + Vector3.up * manager.HoverGap;
            Vector3 tipScreen = viewCamera.WorldToScreenPoint(tip);

            bool inFront = tipScreen.z > viewCamera.nearClipPlane;

            // the bob is in the world, so it reads as the ping floating over the thing
            float bob = Mathf.Sin(Time.time * manager.BobSpeed + bobPhase) * manager.BobAmplitude;
            Vector3 bobbedScreen = viewCamera.WorldToScreenPoint(tip + Vector3.up * bob);

            hoverPoint = bobbedScreen;
            hoverDepth = Mathf.Max(bobbedScreen.z, viewCamera.nearClipPlane);
            hoverPixels = PixelsForWorldHeight(viewCamera, manager.HoverWorldHeight, hoverDepth);

            if (inFront)
            {
                float shrink = onScreen ? 0.0f : HOVER_HYSTERESIS;
                wantHover = FitsHover(tipScreen, hoverPixels, hoverPixels * aspect, shrink);
            }

            // behind the camera the projection is mirrored, so mirror it back through the centre
            Vector2 toward = tipScreen;
            if (tipScreen.z < 0.0f) toward = centre - (toward - centre);

            Vector2 direction = toward - centre;
            if (direction.sqrMagnitude < 1.0f) direction = Vector2.down;
            direction.Normalize();

            edgePoint = manager.EdgePoint(centre, direction, edgePixels * aspect);

            // a small poke toward the target, like a finger tapping the glass
            float poke = (0.5f + 0.5f * Mathf.Sin(Time.time * manager.BobSpeed * 2.0f + bobPhase)) * manager.EdgePoke;
            edgePoint += direction * poke;

            // rolled so the tip, which is the local -Y, points along the direction
            edgeRoll = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90.0f;

            // no sensible hover pose, so the hover pose is the edge pose and the blend changes nothing
            if (!inFront)
            {
                hoverPoint = edgePoint;
                hoverDepth = manager.EdgeDistance;
                hoverPixels = edgePixels;
            }
        }
        else
        {
            // parked: nowhere to point, so it stands upright above the inventory
            edgePoint = manager.ParkedPoint(this);
            edgeRoll = 0.0f;

            hoverPoint = edgePoint;
            hoverDepth = manager.EdgeDistance;
            hoverPixels = edgePixels;
        }

        if (!initialised)
        {
            initialised = true;
            onScreen = wantHover;
            hoverBlend = wantHover ? 1.0f : 0.0f;
            smoothedEdge = edgePoint;
            smoothedRoll = edgeRoll;
        }
        else
        {
            if (wantHover != onScreen) SetOnScreen(wantHover);

            // the edge point can jump, e.g. across the screen when a target passes behind the camera or
            // down to the inventory when a target is lost, so it is followed rather than snapped to
            float follow = 1.0f - Mathf.Exp(-manager.EdgeFollow * Time.unscaledDeltaTime);
            smoothedEdge = Vector2.Lerp(smoothedEdge, edgePoint, follow);
            smoothedRoll = Mathf.LerpAngle(smoothedRoll, edgeRoll, follow);
        }

        Vector2 point = Vector2.Lerp(smoothedEdge, hoverPoint, hoverBlend);
        float pixels = Mathf.Lerp(edgePixels, hoverPixels, hoverBlend);
        float roll = Mathf.LerpAngle(smoothedRoll, 0.0f, hoverBlend);
        float depth = Mathf.Lerp(manager.EdgeDistance, hoverDepth, hoverBlend);

        Place(viewCamera, point, depth, pixels, roll);
    }


    private void SetOnScreen(bool value)
    {
        onScreen = value;

        if (blendTween != null) blendTween.Kill();

        blendTween = DOTween.To(() => hoverBlend, blend => hoverBlend = blend, value ? 1.0f : 0.0f, manager.BlendDuration)
            .SetEase(Ease.OutCubic)
            .SetLink(gameObject);
    }


    // Everything is in screen terms until here: the ping goes at that screen point and depth, sized
    // so it covers the given pixel height, and faces the camera fully
    private void Place(Camera viewCamera, Vector2 point, float depth, float pixels, float roll)
    {
        Transform cameraTransform = viewCamera.transform;

        Vector3 position = viewCamera.ScreenToWorldPoint(new Vector3(point.x, point.y, depth));

        transform.SetPositionAndRotation(position, cameraTransform.rotation * Quaternion.Euler(0.0f, 0.0f, roll));

        float rectHeight = rectTransform.rect.height;
        if (rectHeight <= 0.0f) return;

        float worldHeight = WorldHeightForPixels(viewCamera, pixels, depth);

        Transform parent = transform.parent;
        float parentScale = parent != null ? parent.lossyScale.y : 1.0f;
        if (Mathf.Approximately(parentScale, 0.0f)) parentScale = 1.0f;

        float scale = worldHeight / rectHeight / parentScale * presence * pointerScale;

        transform.localScale = new Vector3(scale, scale, scale);

        // the marker points, the symbol on it stays upright
        if (icon != null) icon.rectTransform.localRotation = Quaternion.Euler(0.0f, 0.0f, -roll);
    }


    // The tip is inside the usable area with room above it for the body, and the body is clear of the
    // inventory bar
    private bool FitsHover(Vector3 tipScreen, float pixels, float width, float shrink)
    {
        Rect usable = manager.UsableArea;

        float halfWidth = width * 0.5f;

        if (tipScreen.x < usable.xMin + halfWidth + shrink) return false;
        if (tipScreen.x > usable.xMax - halfWidth - shrink) return false;
        if (tipScreen.y < usable.yMin + shrink) return false;
        if (tipScreen.y > usable.yMax - pixels - shrink) return false;

        if (!manager.HasInventoryRect) return true;

        Rect footprint = new Rect(tipScreen.x - halfWidth - shrink, tipScreen.y - shrink, width + shrink * 2.0f, pixels + shrink * 2.0f);

        return !footprint.Overlaps(manager.InventoryRect);
    }


    private static float PixelsForWorldHeight(Camera viewCamera, float worldHeight, float depth)
    {
        float viewHeight = ViewHeightAt(viewCamera, depth);
        return viewHeight > 0.0f ? worldHeight / viewHeight * Screen.height : 0.0f;
    }


    private static float WorldHeightForPixels(Camera viewCamera, float pixels, float depth)
    {
        return pixels / Mathf.Max(1.0f, Screen.height) * ViewHeightAt(viewCamera, depth);
    }


    // how many world units tall the view is at this depth
    private static float ViewHeightAt(Camera viewCamera, float depth)
    {
        if (viewCamera.orthographic) return viewCamera.orthographicSize * 2.0f;

        return 2.0f * depth * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }


    // MARK: target


    // The top middle of whatever the target draws. Recomputed every frame, because a freshly placed
    // entity is still growing in.
    private Vector3 Anchor()
    {
        Vector3 fallback = target.transform.position;

        if (renderers == null) return fallback;

        bool any = false;
        Bounds bounds = new Bounds(fallback, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer candidate = renderers[i];
            if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;

            if (!any)
            {
                bounds = candidate.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(candidate.bounds);
            }
        }

        if (!any) return fallback;

        return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
    }


    private void TryResolve()
    {
        EntityId found = null;

        if (!string.IsNullOrEmpty(targetGuid) && EntityManager.Instance != null)
        {
            EntityManager.Instance.TryGetEntity(targetGuid, out found);
        }

        // There is only ever one compendium, and a soft lock is always about the book, so a fail ping
        // that has lost its target points at the book rather than parking
        if (found == null && type == NotificationType.FailPuzzle && BookAnimator.Instance != null)
        {
            found = BookAnimator.Instance.GetComponentInParent<EntityId>();
        }

        if (found == null) return;

        target = found;
        target.Destroyed += OnTargetDestroyed;
        renderers = target.GetComponentsInChildren<Renderer>(true);
    }


    // falls back to parked, or for a fail ping to the book, when the retry in Tick next runs
    private void OnTargetDestroyed(EntityId destroyed)
    {
        Unbind();
    }


    private void Unbind()
    {
        if (target != null) target.Destroyed -= OnTargetDestroyed;

        target = null;
        renderers = null;
    }
}
