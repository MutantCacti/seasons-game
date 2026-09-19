using UnityEngine;

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


    private bool leftClickDown = false;
    private Vector3 right;
    private Vector3 forward;
    private Vector3 targetPosition;
    private float zoomOffset = 0.0f;
    private Vector2 direction = Vector2.zero;
    private bool holdingWasd = false;


    public void Start()
    {
        InputManager.Instance.onLeftClickDown += OnLeftClickDown;
        InputManager.Instance.onMouseMove += OnMouseMove;
        InputManager.Instance.onScrollWheel += OnScrollWheel;
        InputManager.Instance.onMovement += OnMovement;

        // the camera never rotates so the right and forward
        // direction can be computed once at start time
        right = transform.right;
        right.y = 0.0f;
        right.Normalize();

        forward = transform.forward;
        forward.y = 0.0f;
        forward.Normalize();

        targetPosition = transform.position;
    }


    public void OnDestroy()
    {
        UnsubscribeListeners();
    }


    private void UnsubscribeListeners()
    {
        if (InputManager.Instance == null) return;

        InputManager.Instance.onLeftClickDown -= OnLeftClickDown;
        InputManager.Instance.onMouseMove -= OnMouseMove;
        InputManager.Instance.onScrollWheel -= OnScrollWheel;
        InputManager.Instance.onMovement -= OnMovement;
    }


    public void OnLeftClickDown(bool newLeftClickDown)
    {
        leftClickDown = newLeftClickDown;
    }


    public void OnMouseMove(Vector2 delta)
    {
        if (!leftClickDown) return;

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
