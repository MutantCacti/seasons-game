using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif


// The sea: a flat sheet at `level` over exactly the world's footprint, baked by TerrainBaker as the
// "Sea Level" child of the Grid. It is scene data, not world state, so nothing here is saved.
// The sea does nothing for gameplay; sea behaviour belongs to the seafloor tiles.
//
// NB: the trigger BoxCollider spans the baked mesh's bounds, so it also covers every hole and gap in
// the silhouette. Any future "is this over water" query must check tiles/cells, not the collider.
//
// ExecuteAlways so the sheet and the shader's globals are right in the Scene view without playing
[ExecuteAlways]
public class SeaLevel : MonoBehaviour
{
    private static SeaLevel instance;
    private static int searchedFrame = -1;


    // a caller that runs before our Awake, or while we're inactive, still finds the sea. The scene
    // search runs at most once a frame, so a scene with no sea doesn't pay for it on every call
    public static SeaLevel Instance
    {
        get
        {
            if (instance == null && searchedFrame != Time.frameCount)
            {
                searchedFrame = Time.frameCount;
                instance = FindAnyObjectByType<SeaLevel>(FindObjectsInactive.Include);
            }

            return instance;
        }
    }


    // world y of the sheet. Designer owned: a re-bake never writes it. Anything at or below the
    // lowest terrain level is hidden under the tops, since the sheet only covers the footprint
    [SerializeField] private float level = 0.25f;


    // the rest is written by TerrainBaker
    [Header("Baked")]
    [SerializeField] private float floorY = -4.0f;  // the terrain's voidDepth, where the skirt bottoms out


    // readable any time, including before Awake and while inactive, because it is the serialized value
    public float Y { get { return level; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            // edit mode Awakes too, and Destroy isn't allowed there
            if (Application.isPlaying)
            {
                Debug.LogWarning("SeaLevel: Found more than one SeaLevel in the scene - destroying newest!!");
                Destroy(gameObject);
            }

            return;
        }

        instance = this;
        Apply();
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        // wait a tick rather than move the transform from inside OnValidate, which Unity restricts
        EditorApplication.delayCall += ApplyIfAlive;
    }


    private void ApplyIfAlive()
    {
        if (this == null) return;

        Apply();
    }
#endif


    // MARK: level


    public void SetLevel(float newLevel)
    {
        level = newLevel;
        Apply();
    }


    public void Apply()
    {
        Vector3 position = transform.position;
        if (position.y == level) return;

        transform.position = new Vector3(position.x, level, position.z);
    }


    // MARK: bake


    public void SetFloor(float voidDepth)
    {
        floorY = voidDepth;
    }
}
