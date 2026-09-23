using System;
using UnityEngine;


// Identifies one *instance* of one entity. GameItem.Id names the type and still identifies which prefab
// to instantiate; this names the thing that came out of that prefab.
//
// The guid is deliberately runtime only: it is never serialized, so a prefab asset can never carry one
// and two instances of the same prefab can never share one. It is generated on first read, which is
// always after Instantiate, and EntityManager.Assign overrides it when the instance came off disk.
[DisallowMultipleComponent]
public class EntityId : MonoBehaviour
{
    [NonSerialized] private string guid;


    // How EntityId tells its owner that it is going away, so the owner can drop it from its live list
    public event Action<EntityId> Destroyed;


    public string Guid
    {
        get
        {
            if (string.IsNullOrEmpty(guid)) guid = System.Guid.NewGuid().ToString("N");
            return guid;
        }
    }


    // True once a guid exists, without generating one as a side effect
    public bool HasGuid { get { return !string.IsNullOrEmpty(guid); } }


    // Used by EntityManager when an entity is loaded from the save, so it keeps the identity anything
    // else already refers to
    public void Assign(string newGuid)
    {
        if (string.IsNullOrEmpty(newGuid))
        {
            Debug.LogError("EntityId: asked to assign an empty guid, so this entity keeps its own - " +
                "anything referring to the saved guid will not find it", this);
            return;
        }

        if (!string.IsNullOrEmpty(guid) && guid != newGuid)
        {
            Debug.LogError($"EntityId: this entity already generated guid '{guid}', so the saved guid " +
                $"'{newGuid}' was not applied - anything referring to guid '{newGuid}' will not find it", this);
            return;
        }

        guid = newGuid;
    }


    // EntityManager calls this so a destroyed entity cannot keep answering lookups through a stale
    // callback. Safe to call more than once.
    public void Unbind()
    {
        Destroyed = null;
    }


    private void OnDestroy()
    {
        // taken first so a handler that unsubscribes itself cannot change what gets called
        Action<EntityId> handlers = Destroyed;

        Destroyed = null;

        handlers?.Invoke(this);
    }
}
