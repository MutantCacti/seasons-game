using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private float movementSpeed = 0.004f;  // camera movement speed; units per pointer delta pixel
    [SerializeField] private float zoomSpeed = 1.5f;        // camera zoom in/out speed; units per scroll
    [SerializeField] private float maxZoomOffset = 7.0f;    // maximum zoom offset (bidirectional i.e. [-maxZoomOffset, maxZoomOffset]); units
    [SerializeField] private float zoomMovementSpeedFactor = 1.4f;  // movement speed multiplier based on zoom level;
    // at maxZoomOffset, movement speed is movementSpeed * zoomMovementSpeedFactor;
    // at -maxZoomOffset, speed is movementSpeed * (1 / zoomMovementSpeedFactor)
    [SerializeField] [Range(0.0f, 1.0f)] private float snappiness = 0.02f;  // per-frame lerp value


    private bool leftClickDown = false;
    private Vector3 targetPosition;
    private float zoomOffset = 0.0f;


    public void Start()
    {
        InputManager.Instance.onLeftClickDown += OnLeftClickDown;
        InputManager.Instance.onMouseMove += OnMouseMove;
        InputManager.Instance.onScrollWheel += OnScrollWheel;

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
    }


    public void OnLeftClickDown(bool newLeftClickDown)
    {
        leftClickDown = newLeftClickDown;
    }


    public void OnMouseMove(Vector2 delta)
    {
        if (!leftClickDown) return;

        Vector3 right = transform.right;
        right.y = 0.0f;
        right.Normalize();

        Vector3 forward = transform.forward;
        forward.y = 0.0f;
        forward.Normalize();

        float normalizedZoomOffset = (zoomOffset + maxZoomOffset) / (maxZoomOffset * 2.0f);
        Debug.Log(normalizedZoomOffset);
        float zoomScaledMovementSpeed = movementSpeed * Mathf.Lerp(zoomMovementSpeedFactor, 1.0f / zoomMovementSpeedFactor, normalizedZoomOffset);
        // delta is negative so that the camera moves the opposite direction of the mouse
        // otherwise dragging down would move the camera down, visually moving the map upwards
        Debug.Log(zoomScaledMovementSpeed);
        Vector3 movement = (right * -delta.x + forward * -delta.y) * zoomScaledMovementSpeed;

        targetPosition += movement;
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


    public void Update()
    {
        transform.position = Vector3.Lerp(transform.position, targetPosition, snappiness);
    }
}
