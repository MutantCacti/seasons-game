using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Vector2 movementBoundsX = new Vector2(10.0f, 20.0f);
    [SerializeField] private Vector2 movementBoundsZ = new Vector2(-20.0f, -10.0f);

    [SerializeField] private float movementSpeed = 0.004f;  // camera movement speed; units per pointer delta pixel
    [SerializeField] private float keyboardMovementSpeed = 8.0f;  // camera movement speed; units per second
    [SerializeField] private float zoomSpeed = 1.5f;        // camera zoom in/out speed; units per scroll
    [SerializeField] private float maxZoomOffset = 7.0f;    // maximum zoom offset (bidirectional i.e. [-maxZoomOffset, maxZoomOffset]); units
    [SerializeField] private float zoomMovementSpeedFactor = 1.4f;  // movement speed multiplier based on zoom level;
    // at maxZoomOffset, movement speed is movementSpeed * zoomMovementSpeedFactor;
    // at -maxZoomOffset, speed is movementSpeed * (1 / zoomMovementSpeedFactor)
    [SerializeField] [Range(0.0f, 1.0f)] private float snappiness = 0.2f;  // per-frame lerp value, at REFERENCE_FRAMERATE

    private const float REFERENCE_FRAMERATE = 60.0f;  // framerate the authored snappiness value is tuned against

    [SerializeField] private LayerMask dragSurfaceMask;
    [SerializeField] private float dragRayDistance = 1000.0f;

    [Header("World bounds")]
    [SerializeField] private float boundsBuffer = 5.0f;     // how far past the world's XZ edge the view centre may settle; units
    [SerializeField] private float boundsOvershoot = 1.5f;  // extra drag stretch past the buffered bounds before the rubber band bottoms out; units
    [SerializeField] [Range(0.0f, 1.0f)] private float boundsReturnSnappiness = 0.2f;  // per-reference-frame ease when springing back inside, framerate independent

    [Header("Audio listener")]
    [SerializeField] private Transform audioListener;
    [SerializeField] private float listenerDistance = 5.0f;

    [SerializeField] private SoundObject testSO;        // DEBUG
    [SerializeField] private GameItem testGameItem;     // DEBUG
    [SerializeField] private GameItem treeGameItem;     // DEBUG

    private bool leftClickDown = false;
    private Vector3 right;
    private Vector3 forward;
    private Vector3 targetPosition;
    private Vector3 currentPosition;
    private Vector3 panOrigin;
    private float zoomOffset = 0.0f;
    private Vector2 direction = Vector2.zero;
    private bool holdingWasd = false;
    private bool dragStartedOnWorld = false;
    private Camera viewCamera;
    private bool warnedAboutMissingPointerPosition = false;

    // world bounds, resolved once from TerrainManager; the map never changes shape at runtime
    private bool worldBoundsResolved = false;
    private Vector2 worldBoundsMin;
    private Vector2 worldBoundsMax;
    private float groundPlaneY = 0.0f;


    // shake state; elapsed starts past any duration, which is what "not shaking" looks like here
    private const float SHAKE_HZ = 26.0f;
    private float shakeMagnitude;
    private float shakeDuration;
    private float shakeElapsed = float.PositiveInfinity;

    // pan-to-target state; followed every frame so a moving target stays centred
    private const float PAN_ARRIVE_SQR = 0.0025f;
    private const float MIN_FORWARD_Y = 0.0001f;
    private Transform panTarget;


    public bool IsPanningToTarget { get { return panTarget != null; } }


    public void Start()
    {
        InputManager.Instance.onLeftClickDown += OnLeftClickDown;
        InputManager.Instance.onMouseDelta += OnMouseMove;
        InputManager.Instance.onScrollWheel += OnScrollWheel;
        InputManager.Instance.onMovement += OnMovement;

        viewCamera = GetComponent<Camera>();
        if (viewCamera == null) viewCamera = Camera.main;

        if (viewCamera == null)
        {
            Debug.LogError("CameraController: no Camera on this GameObject and none in the scene, add it!!!", this);
        }

        if (dragSurfaceMask.value == 0)
        {
            Debug.LogError("CameraController: dragSurfaceMask is empty, so no layer counts as a drag", this);
        }

        // the camera never rotates so the right and forward
        // direction can be computed once at start time
        right = transform.right;
        right.y = 0.0f;
        right.Normalize();

        forward = transform.forward;
        forward.y = 0.0f;
        forward.Normalize();

        targetPosition = transform.position;
        currentPosition = transform.position;

        ResolveAudioListener();


        // DEBUG: play the intro song
        AudioManager.Instance.PlayMusic(testSO);
    }


    public void OnDestroy()
    {
        UnsubscribeListeners();
    }


    private void UnsubscribeListeners()
    {
        if (InputManager.Instance == null) return;

        InputManager.Instance.onLeftClickDown -= OnLeftClickDown;
        InputManager.Instance.onMouseDelta -= OnMouseMove;
        InputManager.Instance.onScrollWheel -= OnScrollWheel;
        InputManager.Instance.onMovement -= OnMovement;
    }


    public void OnLeftClickDown(bool newLeftClickDown)
    {
        leftClickDown = newLeftClickDown;

        if (!newLeftClickDown) return;

        panOrigin = targetPosition;
        dragStartedOnWorld = CanStartCameraDrag();
    }


    public void CancelPan()
    {
        if (!dragStartedOnWorld) return;

        dragStartedOnWorld = false;
        targetPosition = panOrigin;
    }


    private bool CanStartCameraDrag()
    {
        if (viewCamera == null) return false;

        InputManager input = InputManager.Instance;

        if (input == null || !input.HasPointerPosition)
        {
            if (!warnedAboutMissingPointerPosition)
            {
                warnedAboutMissingPointerPosition = true;

                Debug.LogWarning("CameraController: the Mouse Position action has never fired but we are in CanStartCameraDrag. This should be impossible, so I'm letting it happen anyways", this);
            }

            return true;
        }

        Vector2 screenPosition = input.PointerPosition;

        // The UI owns a press that lands on it, whatever world geometry is behind it
        if (PointerRaycastManager.IsPointerOverUI(screenPosition)) return false;

        // stays unmasked so that we can hit nothing, because hitting nothing is a
        // valid drag target
        if (!PointerRaycastManager.TryGetHit(screenPosition, Physics.DefaultRaycastLayers, dragRayDistance, QueryTriggerInteraction.Ignore, out RaycastHit hit))
        {
            return true;
        }

        return (dragSurfaceMask.value & (1 << hit.collider.gameObject.layer)) != 0;
    }


    public void OnMouseMove(Vector2 delta)
    {
        if (!leftClickDown || !dragStartedOnWorld) return;

        // the player grabbing the map always wins over a pan they asked for earlier
        StopPanningToTarget();

        // delta is negative so that the camera moves the opposite direction of the mouse
        // otherwise dragging down would move the camera down, visually moving the map upwards
        this.TranslateCamera(-delta, movementSpeed);
    }


    public void OnScrollWheel(float delta)
    {
        StopPanningToTarget();

        Vector3 forward = transform.forward;
        forward.Normalize();

        float deltaOffset = delta * zoomSpeed;

        Vector3 movement = forward * deltaOffset;
        zoomOffset = Mathf.Clamp(zoomOffset + deltaOffset, -maxZoomOffset, maxZoomOffset);

        if (zoomOffset < maxZoomOffset && -maxZoomOffset < zoomOffset)
        {
            targetPosition += movement;
        }
    }


    public void OnMovement(Vector2 newDirection)
    {
        direction = newDirection;
        holdingWasd = direction != Vector2.zero;

        if (holdingWasd) StopPanningToTarget();
    }


    public void Update()
    {
        if (holdingWasd)
        {
            // wasd input uses velocity so needs scaling by deltaTime
            // to keep speed framerate independent
            this.TranslateCamera(direction, keyboardMovementSpeed * Time.deltaTime);
        }
        // decay frame snappines by the number of elapsed reference frames
        // that snappiness is relative to, keeping lerp framerate independent
        float frameSnappiness = 1.0f - Mathf.Pow(1.0f - snappiness, Time.deltaTime * REFERENCE_FRAMERATE);

        TickPanToTarget();

        ClampToWorldBounds();

        currentPosition = Vector3.Lerp(currentPosition, targetPosition, frameSnappiness);
        transform.position = currentPosition + TickShake();
    }


    // MARK: world bounds


    private bool IsDragging { get { return leftClickDown && dragStartedOnWorld; } }


    // Resolves the world's XZ footprint and the ground plane once; the map never changes shape.
    private bool ResolveWorldBounds()
    {
        if (worldBoundsResolved) return true;

        TerrainManager terrain = TerrainManager.Instance;
        if (terrain == null) return false;

        terrain.GetWorldXZBounds(out worldBoundsMin, out worldBoundsMax);

        // the lowest terrain level is the ground plane; 0 is just the field's default fallback
        groundPlaneY = terrain.LevelCount > 0 ? terrain.GetLevelHeight(0) : 0.0f;

        worldBoundsResolved = true;
        return true;
    }


    private void GetBufferedBounds(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = worldBoundsMin.x - boundsBuffer;
        maxX = worldBoundsMax.x + boundsBuffer;
        minZ = worldBoundsMin.y - boundsBuffer;
        maxZ = worldBoundsMax.y + boundsBuffer;
    }


    // The ground point under the view centre: where the view ray meets the ground plane. Only the
    // XZ matters for the bounds, so the height is dropped.
    private bool TryGetGroundPoint(out Vector2 ground)
    {
        Vector3 viewForward = transform.forward;

        // a level or upward view never brings the ray down onto the ground
        if (viewForward.y > -MIN_FORWARD_Y)
        {
            ground = Vector2.zero;
            return false;
        }

        float distance = (groundPlaneY - targetPosition.y) / viewForward.y;
        ground = new Vector2(targetPosition.x + viewForward.x * distance, targetPosition.z + viewForward.z * distance);
        return true;
    }


    // Eases the view centre back inside the buffered bounds whenever nothing is dragging it out.
    // Rebuilds targetPosition at the same height, so the zoom is untouched.
    private void ClampToWorldBounds()
    {
        // the rubber band owns the edge while dragging; this only handles release/keys/zoom/pings
        if (IsDragging) return;

        if (!ResolveWorldBounds()) return;
        if (!TryGetGroundPoint(out Vector2 ground)) return;

        GetBufferedBounds(out float minX, out float maxX, out float minZ, out float maxZ);

        Vector2 clamped = new Vector2(Mathf.Clamp(ground.x, minX, maxX), Mathf.Clamp(ground.y, minZ, maxZ));

        // inside the buffered bounds: leave targetPosition alone, so there's zero drift and zero change
        if (ground == clamped) return;

        // framerate-independent ease, the same decay shape as the camera lerp
        float t = 1.0f - Mathf.Pow(1.0f - boundsReturnSnappiness, Time.deltaTime * REFERENCE_FRAMERATE);
        Vector2 eased = Vector2.Lerp(ground, clamped, t);

        Vector3 viewForward = transform.forward;
        float distance = (groundPlaneY - targetPosition.y) / viewForward.y;
        targetPosition.x = eased.x - viewForward.x * distance;
        targetPosition.z = eased.y - viewForward.z * distance;
    }


    // Rubber-band the drag: once the view centre is past the buffered bounds, shrink the part of
    // the move that pushes it further out, bottoming out at the overshoot. Applied to the delta
    // rather than re-clamping the accumulated position, so the resistance never compounds.
    private Vector3 ApplyDragResistance(Vector3 movement)
    {
        if (!ResolveWorldBounds()) return movement;
        if (!TryGetGroundPoint(out Vector2 ground)) return movement;

        GetBufferedBounds(out float minX, out float maxX, out float minZ, out float maxZ);

        movement.x *= AxisResistance(ground.x, minX, maxX, movement.x);
        movement.z *= AxisResistance(ground.y, minZ, maxZ, movement.z);

        return movement;
    }


    private float AxisResistance(float position, float min, float max, float delta)
    {
        if (delta < 0.0f && position < min)
        {
            float past = min - position;
            return boundsOvershoot > 0.0f ? Mathf.Clamp01(1.0f - past / boundsOvershoot) : 0.0f;
        }

        if (delta > 0.0f && position > max)
        {
            float past = position - max;
            return boundsOvershoot > 0.0f ? Mathf.Clamp01(1.0f - past / boundsOvershoot) : 0.0f;
        }

        return 1.0f;
    }


    // MARK: pan to target


    // Glides the view centre onto target and keeps it there while it moves, at the current height so
    // the zoom is untouched. Ends on arrival, or as soon as the player drags, scrolls or uses WASD.
    public void PanToTransform(Transform target)
    {
        panTarget = target;
    }


    public void StopPanningToTarget()
    {
        panTarget = null;
    }


    private void TickPanToTarget()
    {
        // Unity-null covers a target destroyed mid pan
        if (panTarget == null)
        {
            panTarget = null;
            return;
        }

        Vector3 viewForward = transform.forward;

        // a camera looking level would never bring the ray down onto the target
        if (viewForward.y > -MIN_FORWARD_Y)
        {
            panTarget = null;
            return;
        }

        // step back along the view ray from the target until we are at the current height
        Vector3 point = panTarget.position;
        float distance = (point.y - targetPosition.y) / viewForward.y;

        targetPosition = point - viewForward * distance;

        if ((currentPosition - targetPosition).sqrMagnitude < PAN_ARRIVE_SQR) panTarget = null;
    }


    // used to give a placement weight. One axis only
    public void Shake(float magnitude, float duration)
    {
        shakeMagnitude = magnitude;
        shakeDuration = duration;
        shakeElapsed = 0.0f;
    }


    private Vector3 TickShake()
    {
        if (shakeElapsed >= shakeDuration) return Vector3.zero;

        shakeElapsed += Time.deltaTime;

        float progress = Mathf.Clamp01(shakeElapsed / shakeDuration);

        float falloff = (1.0f - progress) * (1.0f - progress);

        float wobble = -Mathf.Sin(shakeElapsed * SHAKE_HZ * Mathf.PI * 2.0f);

        return Vector3.up * (shakeMagnitude * falloff * wobble);
    }


    // LateUpdate so the camera has already settled for this frame
    private void LateUpdate()
    {
        if (audioListener == null) return;

        Ray ray = new Ray(currentPosition, transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, dragRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            // nothing under the view centre, so leave the listener where it was
            return;
        }

        // move back along the ray so the listener stays a fixed distance above the ground
        float distance = Mathf.Min(listenerDistance, hit.distance);

        audioListener.position = hit.point - ray.direction * distance;
    }


    private void ResolveAudioListener()
    {
        AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);

            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i].gameObject == gameObject) continue;

                audioListener = listeners[i].transform;
                break;
            }

        if (audioListener == null)
        {
            GameObject listenerGO = new GameObject("Audio Listener");
            listenerGO.transform.SetParent(transform, false);

            audioListener = listenerGO.transform;
            listenerGO.AddComponent<AudioListener>();
        }
    }


    private void TranslateCamera(Vector2 delta, float speed)
    {
        float normalizedZoomOffset = (zoomOffset + maxZoomOffset) / (maxZoomOffset * 2.0f);

        float zoomScaledMovementSpeed = speed * Mathf.Lerp(zoomMovementSpeedFactor, 1.0f / zoomMovementSpeedFactor, normalizedZoomOffset);

        Vector3 movement = (right * delta.x + forward * delta.y) * zoomScaledMovementSpeed;

        // while dragging, resist the outward part of the move so the view centre stretches past
        // the buffered bounds like a rubber band instead of sliding off
        if (IsDragging) movement = ApplyDragResistance(movement);

        targetPosition += movement;

        targetPosition.x = Mathf.Clamp(targetPosition.x, movementBoundsX.x, movementBoundsX.y);
        targetPosition.z = Mathf.Clamp(targetPosition.z, movementBoundsZ.x, movementBoundsZ.y);
    }
}
