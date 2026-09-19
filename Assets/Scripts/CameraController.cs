using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private float movementSpeed = 0.004f;  // transform units per pointer delta piiel
    [SerializeField] private float snappiness = 0.5f;       // [0, 1] --- lerp value


    private bool leftClickDown = false;
    private Vector3 targetPosition;


    public void Start()
    {
        InputManager.Instance.onLeftClickDown += OnLeftClickDown;
        InputManager.Instance.onMouseMove += OnMouseMove;

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

        // delta is negative so that the camera moves the opposite direction of the mouse
        // otherwise dragging down would move the camera down, visuall moving the map upwards
        Vector3 movement = (right * -delta.x + forward * -delta.y) * movementSpeed;

        targetPosition += movement;
    }


    public void Update()
    {
        transform.position = Vector3.Lerp(transform.position, targetPosition, snappiness);
    }
}
