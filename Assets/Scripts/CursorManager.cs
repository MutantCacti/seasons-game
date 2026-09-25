using System.Collections;
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

        // enough for the editor and standalone. web resets it right after, see Start
        Apply(CursorType.Default, false);
    }


    // on web the cursor is a css cursor on the canvas, and the engine's own startup calls
    // JS_Cursor_SetShow, which overwrites it with plain "default" (see WebGLSupport lib/Cursor.js).
    // the guard in Apply would then skip every later call with the same type, so re-submit
    // once things have settled
    private IEnumerator Start()
    {
        Apply(current, true);

        yield return null;

        Apply(current, true);
    }


    // the browser can drop the canvas cursor across focus changes too
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && applied) Apply(current, true);
    }


    // Only submits a cursor change when the type actually changes, so cheap
    // and can be called every frame
    public void SetCursor(CursorType cursorType)
    {
        Apply(cursorType, false);
    }


    private void Apply(CursorType cursorType, bool force)
    {
        if (!force && applied && current == cursorType) return;

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