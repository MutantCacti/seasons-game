using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;


public enum CursorType
{
    Default,
    Disabled,
    Pointer,
}


// Non-Selectable UI that should still get the hand cursor (hotbar slots are drag sources, not buttons)
public interface ICursorHoverTarget
{
    bool WantsPointer { get; }
}


public class CursorManager : MonoBehaviour
{
    private static CursorManager instance;
    public static CursorManager Instance { get { return instance; } }


    [SerializeField] private Texture2D defaultCursor;
    [SerializeField] private Vector2 defaultHotSpot;
    [SerializeField] private Texture2D disabledCursor;
    [SerializeField] private Vector2 disabledHotSpot;
    [SerializeField] private Texture2D pointerCursor;
    // the fingertip is the top-left of cpointer32, pointing up-left
    [SerializeField] private Vector2 pointerHotSpot = new Vector2(2, 1);

    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;
    [SerializeField] private Vector2 fallbackHotSpot = Vector2.zero;


    private Dictionary<CursorType, Texture2D> cursors;
    private Dictionary<CursorType, Vector2> cursorHotSpots;
    private CursorType current;
    private bool applied;

    // a gameplay owner (a drag) beats hover, which beats default
    private CursorType overrideCursor;
    private bool hasOverride;

    // hover lookups are cached per hit object, so the parent walk only runs when the pointer
    // moves onto something new
    private GameObject hoveredObject;
    private Selectable hoveredSelectable;
    private ICursorHoverTarget hoveredTarget;


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
            { CursorType.Disabled, disabledCursor },
            { CursorType.Pointer, pointerCursor }
        };
        cursorHotSpots = new Dictionary<CursorType, Vector2>()
        {
            { CursorType.Default, defaultHotSpot },
            { CursorType.Disabled, disabledHotSpot },
            { CursorType.Pointer, pointerHotSpot }
        };

        if (pointerCursor == null)
        {
            Debug.LogError("CursorManager: no pointerCursor assigned, so buttons and slots keep the default cursor on hover", this);
        }

        // enough for the editor and standalone. web resets it right after, see Start
        Apply(CursorType.Default, false);
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;
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


    private void Update()
    {
        Apply(Resolve(), false);
    }


    // MARK: public api


    // Held until ClearOverride, and wins over hover. Cheap, so it can be called every frame
    public void SetOverride(CursorType cursorType)
    {
        overrideCursor = cursorType;
        hasOverride = true;

        Apply(cursorType, false);
    }


    public void ClearOverride()
    {
        hasOverride = false;

        Apply(Resolve(), false);
    }


    // kept for existing callers: same as an override
    public void SetCursor(CursorType cursorType)
    {
        SetOverride(cursorType);
    }


    // MARK: resolve


    private CursorType Resolve()
    {
        if (hasOverride) return overrideCursor;

        if (pointerCursor != null && IsHoveringInteractive()) return CursorType.Pointer;

        return CursorType.Default;
    }


    // reads the ui module's own raycast from this frame, so hovering costs no extra raycast
    private bool IsHoveringInteractive()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        InputSystemUIInputModule module = eventSystem.currentInputModule as InputSystemUIInputModule;
        if (module == null) return false;

        Mouse mouse = Mouse.current;
        if (mouse == null) return false;

        GameObject hit = module.GetLastRaycastResult(mouse.deviceId).gameObject;

        if (hit != hoveredObject)
        {
            hoveredObject = hit;
            hoveredSelectable = hit != null ? hit.GetComponentInParent<Selectable>() : null;
            hoveredTarget = hit != null ? hit.GetComponentInParent<ICursorHoverTarget>() : null;
        }

        if (hit == null) return false;

        // interactability is re-read every frame, a button can grey out while hovered
        if (hoveredSelectable != null && hoveredSelectable.IsInteractable()) return true;

        // unity's null check, the target may have been destroyed under the pointer
        if (hoveredTarget is Object targetObject && targetObject != null && hoveredTarget.WantsPointer) return true;

        return false;
    }


    // MARK: apply


    // Only submits a cursor change when the type actually changes, unless forced
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
