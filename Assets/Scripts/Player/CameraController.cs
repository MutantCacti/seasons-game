using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
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

    [Header("Audio listener")]
    // The listener rides the centre of the view so that 3D falloff is measured from near whatever the
    // player is looking at, rather than from a camera ~23 units above the terrain. This distance also
    // reads as the full volume radius, so a sound wants its minDistance set to about the same value.
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
    private float zoomOffset = 0.0f;
    private Vector2 direction = Vector2.zero;
    private bool holdingWasd = false;
    private bool dragStartedOnWorld = false;
    private Camera viewCamera;
    private bool warnedAboutMissingPointerPosition = false;

    // shake state; elapsed starts past any duration, which is what "not shaking" looks like here
    private const float SHAKE_HZ = 26.0f;
    private float shakeMagnitude;
    private float shakeDuration;
    private float shakeElapsed = float.PositiveInfinity;


    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
    private PointerEventData uiRaycastPointer;
    private EventSystem uiRaycastSystem;


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

        InputManager.Instance.onRightClickDown += DebugAddInventory;
        DebugAddInventory(false);
    }


    private void DebugAddInventory(bool down)
    {
        if (!down)
        {
            // DEBUG: Add water to inventory
            InventoryManager.Instance.Add(testGameItem, 2);

            // DEBUG: Add tree to inventory
            InventoryManager.Instance.Add(treeGameItem, 1);
        }
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
        InputManager.Instance.onRightClickDown -= DebugAddInventory; // DEBUG
    }


    public void OnLeftClickDown(bool newLeftClickDown)
    {
        leftClickDown = newLeftClickDown;

        if (!newLeftClickDown) return;

        dragStartedOnWorld = CanStartCameraDrag();
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

                Debug.LogWarning("CameraController: the Mouse Position action has not fired but we are in CanStartCameraDrag. This should be impossible, so I'm letting it happen anyways", this);
            }

            return true;
        }

        Vector2 screenPosition = input.PointerPosition;

        // The UI owns a press that lands on it, whatever world geometry is behind it
        if (IsPointerOverUI(screenPosition)) return false;

        Ray ray = viewCamera.ScreenPointToRay(screenPosition);

        // stays unmasked so that we can hit nothing, because hitting nothing is a
        // valid drag target
        if (!Physics.Raycast(ray, out RaycastHit hit, dragRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return (dragSurfaceMask.value & (1 << hit.collider.gameObject.layer)) != 0;
    }


    // Live UI hit test at a screen position
    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        // rebuilt only when the EventSystem changes, which means a scene load
        if (uiRaycastPointer == null || uiRaycastSystem != eventSystem)
        {
            uiRaycastSystem = eventSystem;
            uiRaycastPointer = new PointerEventData(eventSystem);
        }

        uiRaycastPointer.position = screenPosition;

        // RaycastAll clears the results list itself
        eventSystem.RaycastAll(uiRaycastPointer, uiRaycastResults);

        return uiRaycastResults.Count > 0;
    }


    public void OnMouseMove(Vector2 delta)
    {
        if (!leftClickDown || !dragStartedOnWorld) return;

        // delta is negative so that the camera moves the opposite direction of the mouse
        // otherwise dragging down would move the camera down, visually moving the map upwards
        this.TranslateCamera(-delta, movementSpeed);
    }


    public void OnScrollWheel(float delta)
    {
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

        currentPosition = Vector3.Lerp(currentPosition, targetPosition, frameSnappiness);
        transform.position = currentPosition + TickShake();
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

        targetPosition += movement;
    }
}
