using System.Collections.Generic;
using UnityEngine;


public enum CursorType
{
    Default,
    Disabled,
}


public class CursorManager : MonoBehaviour
{
    private static CursorManager instance;
    public static CursorManager Instance { get { return instance; } }


    [SerializeField] private Texture2D defaultCursor;
    [SerializeField] private Vector2 defaultHotSpot;
    [SerializeField] private Texture2D disabledCursor;
    [SerializeField] private Vector2 disabledHotSpot;

    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;
    [SerializeField] private Vector2 fallbackHotSpot = Vector2.zero;


    private Dictionary<CursorType, Texture2D> cursors;
    private Dictionary<CursorType, Vector2> cursorHotSpots;
    private CursorType current;
    private bool applied;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one CursorManager in the scene - destroying newest !!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        cursors = new Dictionary<CursorType, Texture2D>()
        {
            { CursorType.Default, defaultCursor },
            { CursorType.Disabled, disabledCursor }
        };
        cursorHotSpots = new Dictionary<CursorType, Vector2>()
        {
            { CursorType.Default, defaultHotSpot },
            { CursorType.Disabled, disabledHotSpot }
        };

        SetCursor(CursorType.Default);
    }


    // Only submits a cursor change when the type actuall changes, so cheap
    // and can be called every frame
    public void SetCursor(CursorType cursorType)
    {
        if (applied && current == cursorType) return;

        current = cursorType;
        applied = true;

        if (!cursors.TryGetValue(cursorType, out Texture2D texture))
        {
            Debug.LogError($"CursorManager: no texture entry in place for cursor '{cursorType}'", this);
            return;
        }

        if (texture == null)
        {
            Debug.LogError($"CursorManager: cursor '{cursorType}' has no texture assigned", this);
            return;
        }

        if (!cursorHotSpots.TryGetValue(cursorType, out Vector2 hotSpot))
        {
            Debug.LogError($"CursorManager: no hotspot entry in place for cursor '{cursorType}'", this);
            hotSpot = fallbackHotSpot;
        }

        Cursor.SetCursor(texture, hotSpot, cursorMode);
    }
}