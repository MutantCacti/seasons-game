using UnityEngine;


// Keeps the object on the water surface: every frame its y follows SeaLevel's live surface, so it
// rides the sea up while it's still rising. It never sinks below the ground it was put on, so before
// the flood (sea waiting under the terrain) it just rests on its tile like any other entity, and the
// rising water lifts it off once it passes the tile top.
//
// Safe on a placement ghost too, which is why it's on PlacementController's ghost whitelist: no
// manager that can be missing, nothing saved, nothing registered. The ground is whatever y the object
// was last put at by someone else (a spawn at the tile top, the ghost's preview move), so nothing here
// needs the terrain either
public class FloatOnSea : MonoBehaviour
{
    // world units above the surface. The sea is depth offset towards the camera, so something sat
    // exactly on it gets its bottom pixel row drawn over by the water; one art pixel keeps it clear
    [SerializeField] private float lift = 1.0f / 32.0f;


    // a world y read back through a parent can differ from what was written in the last bits
    private const float MOVED_TOLERANCE = 0.0001f;


    private float groundY;
    private float lastWrittenY;


    // the ghost is moved and then reactivated on every preview move, and a spawn instantiates at the
    // tile top, so this is where a fresh ground is always read
    private void OnEnable()
    {
        groundY = transform.position.y;
        Snap();
    }


    // after Update, so it wins over anything that repositioned us this frame
    private void LateUpdate()
    {
        // not where we left it: someone moved us, and wherever they put us is the new ground
        float y = transform.position.y;
        if (Mathf.Abs(y - lastWrittenY) > MOVED_TOLERANCE) groundY = y;

        Snap();
    }


    private void Snap()
    {
        SeaLevel sea = SeaLevel.Instance;

        float y = groundY;
        if (sea != null && sea.isActiveAndEnabled) y = Mathf.Max(groundY, sea.SurfaceY + lift);

        Vector3 position = transform.position;
        if (position.y != y) transform.position = new Vector3(position.x, y, position.z);

        lastWrittenY = transform.position.y;
    }
}
