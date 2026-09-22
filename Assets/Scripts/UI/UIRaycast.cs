using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;


// Live UI hit testing to check whether the pointer is over a menu
// raycast state is static because this runs every frame from several unrelated callers
// optimise by using the same PointerEventData in the frame
public static class UIRaycast
{
    private static readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
    private static PointerEventData raycastPointer;
    private static EventSystem raycastSystem;


    // True when a raycast target UI element sits under this screen position
    public static bool IsPointerOverUI(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        // rebuilt only when the EventSystem changes, which means a scene load
        if (raycastPointer == null || raycastSystem != eventSystem)
        {
            raycastSystem = eventSystem;
            raycastPointer = new PointerEventData(eventSystem);
        }

        raycastPointer.position = screenPosition;

        // RaycastAll clears the results list itself
        eventSystem.RaycastAll(raycastPointer, raycastResults);

        return raycastResults.Count > 0;
    }
}
