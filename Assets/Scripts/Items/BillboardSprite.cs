using UnityEngine;


[RequireComponent(typeof(SpriteRenderer))]
public class BillboardSprite : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;

    [SerializeField] private float yawOffset = 0.0f;    // in case the sprite doesn't face +Z, like if its flipped to backface


    private const float MIN_FLAT_FORWARD_SQR = 0.000001f;


    private void Start()
    {
        FaceCamera();
    }


    public void FaceCamera()
    {
        Camera viewCamera = targetCamera != null ? targetCamera : Camera.main;

        if (viewCamera == null)
        {
            Debug.LogWarning("BillboardSprite: no camera assigned and no camera tagged MainCamera", this);
            return;
        }

        Vector3 cameraForward = viewCamera.transform.forward;
        cameraForward.y = 0.0f;

        if (cameraForward.sqrMagnitude < MIN_FLAT_FORWARD_SQR)
        {
            Debug.LogWarning("BillboardSprite: the camera looks straight down, so it has no " +
                "horizontal forward direction to align the sprite with", this);
            return;
        }

        Quaternion facing = Quaternion.LookRotation(cameraForward, Vector3.up);

        if (yawOffset != 0.0f) facing *= Quaternion.Euler(0.0f, yawOffset, 0.0f);

        transform.rotation = facing;
    }
}
