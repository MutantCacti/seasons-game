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

    [SerializeField] private SoundObject testSO;        // DEBUG
    [SerializeField] private TerrainGrid terrainGrid;   // DEBUG
    [SerializeField] private GameItem testGameItem;     // DEBUG

    private bool leftClickDown = false;
    private Vector3 right;
    private Vector3 forward;
    private Vector3 targetPosition;
    private float zoomOffset = 0.0f;
    private Vector2 direction = Vector2.zero;
    private bool holdingWasd = false;
    private bool dragStartedOnWorld = false;
    private Camera viewCamera;
    private bool warnedAboutMissingPointerPosition = false;


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


        // DEBUG: play the intro song
        AudioManager.Instance.PlayMusic(testSO);

        // DEBUG: remap a random grass tile to water and save
        if (terrainGrid == null)
        {
            Debug.LogError("CameraController: terrainGrid is not assigned, so the terrain debug " +
                "remap is being skipped", this);
        }
        else if (terrainGrid.DebugRemapRandomTile("grass", "water", out Vector3Int remapped))
        {
            Debug.Log($"DEBUG: remapped grass to water at {remapped}");

            if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
        }

        // DEBUG: Add water to inventory
        InventoryManager.Instance.Add(testGameItem, 2);
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

        dragStartedOnWorld = CanStartCameraDrag();
    }


    // Decided once, at press time, and deliberately never re-evaluated: testing this per frame would
    // start panning the moment a placement drag left the inventory panel, and stop panning the
    // moment a camera drag crossed over it.
    private bool CanStartCameraDrag()
    {
        // the UI owns a press that lands on it, whatever world geometry is behind it
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        if (viewCamera == null) return false;

        InputManager input = InputManager.Instance;

        if (input == null || !input.HasPointerPosition)
        {
            if (!warnedAboutMissingPointerPosition)
            {
                warnedAboutMissingPointerPosition = true;

                // pans anyway, which is the behaviour from before world presses mattered, so a
                // missing binding degrades instead of freezing the camera
                Debug.LogWarning("CameraController: the Mouse Position action has not fired, so a press " +
                    "cannot be tested against the world and is being treated as a world drag. Either its " +
                    "binding is missing from the PlayerInput action events, or the mouse has not moved yet.", this);
            }

            return true;
        }

        Ray ray = viewCamera.ScreenPointToRay(input.PointerPosition);

        // Deliberately unmasked: a world object that wants the click has to be hit in order to block
        // the drag, so masking the ray down to the drag surfaces would defeat the allow-list.
        if (!Physics.Raycast(ray, out RaycastHit hit, dragRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return (dragSurfaceMask.value & (1 << hit.collider.gameObject.layer)) != 0;
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
        transform.position = Vector3.Lerp(transform.position, targetPosition, frameSnappiness);
    }


    private void TranslateCamera(Vector2 delta, float speed)
    {
        float normalizedZoomOffset = (zoomOffset + maxZoomOffset) / (maxZoomOffset * 2.0f);

        float zoomScaledMovementSpeed = speed * Mathf.Lerp(zoomMovementSpeedFactor, 1.0f / zoomMovementSpeedFactor, normalizedZoomOffset);

        Vector3 movement = (right * delta.x + forward * delta.y) * zoomScaledMovementSpeed;

        targetPosition += movement;
    }
}
