using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;


// A placed hive that bees live in. It can sit anywhere in a placed entity's prefab: on its root, or
// nested inside another item (a hive in a tree). Its identity is the entity's, found up the parents.
//
// Nothing about the bees is stored here: each bee saves its hive's guid (AnimalSave.homeGuid), and
// Residents counts the live bees that name this one, so the count rolls back with a checkpoint and
// can't drift. A growth or reagent replace keeps the guid, so bees follow a hive into whatever it
// becomes, as long as that has a Beehive too.
//
// The exit is read live, never cached: it sits under the billboarded sprite, which BillboardSprite
// only turns in its Start.
[DisallowMultipleComponent]
public class Beehive : MonoBehaviour
{
    // every live hive, for the spawn rule and for bees looking for one
    private static readonly List<Beehive> all = new List<Beehive>();


    // where bees come out and go in; a child of the billboarded sprite, so it follows its tilt
    [SerializeField] private Transform exit;

    // the drawn hive: bees mill about inside its bounds while home, and it rustles as they come and go
    [SerializeField] private SpriteRenderer body;

    [SerializeField] [Min(1)] private int capacity = 3;


    [Header("Rustle")]
    [SerializeField] private float rustleAngle = 5.0f;         // degrees
    [SerializeField] private float rustleDuration = 0.35f;


    [Header("Recruitment")]
    // how long a dance keeps its flower on offer to the next bee to leave, in seconds of game time
    [SerializeField] private float advertLifetime = 60.0f;


    private EntityId entityId;

    private EntityId advertisedFlower;
    private float advertisedAt = float.NegativeInfinity;


    public static IReadOnlyList<Beehive> All { get { return all; } }


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all.Clear();
    }


    private void Awake()
    {
        if (body == null) body = GetComponentInChildren<SpriteRenderer>();
        if (body == null) Debug.LogError("Beehive: no SpriteRenderer, so hidden bees have nowhere to mill about and the hive can't rustle", this);

        if (exit == null) Debug.LogError("Beehive: no exit transform, so bees come and go from the hive's base", this);
    }


    // a placement ghost has this pruned while inactive, so only real hives ever get here
    private void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
    }


    private void OnDisable()
    {
        all.Remove(this);
    }


    // MARK: identity


    // The entity it is part of. Looked up lazily: EntityManager adds the EntityId after the prefab's
    // Awake, and the guid is only assigned (a load, a replace) after that
    public EntityId Entity
    {
        get
        {
            if (entityId == null) entityId = GetComponentInParent<EntityId>();
            return entityId;
        }
    }


    public string Guid
    {
        get
        {
            EntityId entity = Entity;
            return entity != null ? entity.Guid : "";
        }
    }


    public static Beehive Find(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null && all[i].Guid == guid) return all[i];
        }

        return null;
    }


    // MARK: residents


    public Vector3 ExitPosition { get { return exit != null ? exit.position : transform.position; } }


    public int Capacity { get { return capacity; } }


    public int Residents { get { return AnimalManager.Instance != null ? AnimalManager.Instance.CountHomedAt(Guid) : 0; } }


    public bool HasRoom { get { return Residents < capacity; } }


    // the risen sea is over the base of whatever it is in (a hive up a tree floods with the tree): no
    // bee is born here and none goes in
    public bool IsFlooded
    {
        get
        {
            SeaLevel sea = SeaLevel.Instance;
            if (sea == null || !sea.IsRisen) return false;

            EntityId entity = Entity;
            Transform bottom = entity != null ? entity.transform : transform;

            return bottom.position.y < sea.SurfaceY;
        }
    }


    // A point inside the drawn hive, in the sprite's own plane: at is 0..1 across and up, and margin
    // keeps it that far (world units) in from the edge. The exit when there is no sprite
    public Vector3 InsidePoint(Vector2 at, float margin)
    {
        if (body == null || body.sprite == null) return ExitPosition;

        Bounds local = body.sprite.bounds;
        Vector3 scale = body.transform.lossyScale;

        float marginX = Mathf.Min(margin / Mathf.Max(1e-4f, Mathf.Abs(scale.x)), local.extents.x);
        float marginY = Mathf.Min(margin / Mathf.Max(1e-4f, Mathf.Abs(scale.y)), local.extents.y);

        float x = Mathf.Lerp(local.min.x + marginX, local.max.x - marginX, at.x);
        float y = Mathf.Lerp(local.min.y + marginY, local.max.y - marginY, at.y);

        return body.transform.TransformPoint(new Vector3(x, y, 0.0f));
    }


    // MARK: feel


    // a bee came or went: the sprite child gives a small shake. the root is left alone (growth animates
    // it), and BillboardSprite only sets the child's rotation once, in its Start
    public void Rustle()
    {
        if (body == null) return;

        Transform shaken = body.transform;
        if (shaken == transform) return;

        // two bees at once: finish the last shake first, so it springs back to where it rests
        shaken.DOComplete();
        shaken.DOPunchRotation(new Vector3(0.0f, 0.0f, Random.value < 0.5f ? rustleAngle : -rustleAngle), rustleDuration, 6, 0.5f)
            .SetLink(gameObject);
    }


    // MARK: recruitment


    // a bee danced for this flower, so the next one out may go there
    public void Advertise(EntityId flower)
    {
        if (flower == null) return;

        advertisedFlower = flower;
        advertisedAt = Time.time;
    }


    // the flower last danced for, while the dance is still fresh and the flower still there
    public bool TryGetAdvertised(out EntityId flower)
    {
        flower = advertisedFlower;
        return flower != null && Time.time - advertisedAt <= advertLifetime;
    }
}
