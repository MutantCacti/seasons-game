using System;
using System.Collections.Generic;
using UnityEngine;


// One Game-scene singleton owns every pointer raycast in a frame. Each distinct ray is
// computed lazily, at most once per frame: the first caller that needs a given
// (position, mask, distance, triggers) generates it and everyone after reuses it.
// The UI hit test is cached the same way, on top of the raw uncached UIRaycast.
public class PointerRaycastManager : MonoBehaviour
{
    private static PointerRaycastManager instance;
    public static PointerRaycastManager Instance { get { return instance; } }


    // Hashes the whole raycast query so the Dictionary keys a cached hit and a cached
    // "no hit" alike. IEquatable keeps the default comparer from boxing to object.Equals
    private struct RayKey : IEquatable<RayKey>
    {
        public Vector2 position;
        public int mask;
        public float maxDistance;
        public int triggers;


        public RayKey(Vector2 position, LayerMask mask, float maxDistance, QueryTriggerInteraction triggers)
        {
            this.position = position;
            this.mask = mask.value;
            this.maxDistance = maxDistance;
            this.triggers = (int)triggers;
        }


        public bool Equals(RayKey other)
        {
            return position == other.position
                && mask == other.mask
                && maxDistance == other.maxDistance
                && triggers == other.triggers;
        }


        public override bool Equals(object obj)
        {
            return obj is RayKey other && Equals(other);
        }


        public override int GetHashCode()
        {
            unchecked
            {
                int hash = position.GetHashCode();
                hash = (hash * 397) ^ mask;
                hash = (hash * 397) ^ maxDistance.GetHashCode();
                hash = (hash * 397) ^ triggers;
                return hash;
            }
        }
    }


    // A miss is cached just like a hit, so a caller that lands on nothing still pays
    // for the raycast only once in the frame
    private struct RayResult
    {
        public bool hasHit;
        public RaycastHit hit;
    }


    private readonly Dictionary<RayKey, RayResult> rays = new Dictionary<RayKey, RayResult>();
    private readonly Dictionary<Vector2, bool> uiHits = new Dictionary<Vector2, bool>();

    private int cachedFrame = -1;
    private Camera viewCamera;
    private bool warnedAboutCamera;

    private static bool warnedAboutMissingManager;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("PointerRaycastManager: Found more than one PointerRaycastManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        viewCamera = Camera.main;
        if (viewCamera == null) WarnMissingCamera();
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }


    // MARK: static entry points


    // Callers use these rather than Instance, so a manager missing from a scene degrades
    // to an uncached raycast instead of losing the feature. The missing wiring is still
    // loud because the error is logged once.
    public static bool IsPointerOverUI(Vector2 screenPosition)
    {
        PointerRaycastManager manager = instance;
        if (manager == null)
        {
            WarnMissingManager();
            return UIRaycast.IsPointerOverUI(screenPosition);
        }

        return manager.IsPointerOverUICached(screenPosition);
    }


    public static bool TryGetHit(Vector2 screenPosition, LayerMask mask, float maxDistance, QueryTriggerInteraction triggers, out RaycastHit hit)
    {
        PointerRaycastManager manager = instance;
        if (manager == null)
        {
            WarnMissingManager();

            Camera camera = Camera.main;
            if (camera == null)
            {
                hit = default;
                return false;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            return Physics.Raycast(ray, out hit, maxDistance, mask, triggers);
        }

        return manager.TryGetHitCached(screenPosition, mask, maxDistance, triggers, out hit);
    }


    private static void WarnMissingManager()
    {
        if (warnedAboutMissingManager) return;

        warnedAboutMissingManager = true;
        Debug.LogError("PointerRaycastManager: no PointerRaycastManager in the scene, so pointer rays are computed uncached");
    }


    // MARK: cached queries


    private bool IsPointerOverUICached(Vector2 screenPosition)
    {
        EnsureFrame();

        if (uiHits.TryGetValue(screenPosition, out bool cached)) return cached;

        bool over = UIRaycast.IsPointerOverUI(screenPosition);
        uiHits[screenPosition] = over;
        return over;
    }


    private bool TryGetHitCached(Vector2 screenPosition, LayerMask mask, float maxDistance, QueryTriggerInteraction triggers, out RaycastHit hit)
    {
        EnsureFrame();

        RayKey key = new RayKey(screenPosition, mask, maxDistance, triggers);

        if (rays.TryGetValue(key, out RayResult cached))
        {
            hit = cached.hit;
            return cached.hasHit;
        }

        ResolveCamera();
        if (viewCamera == null)
        {
            hit = default;
            rays[key] = new RayResult { hasHit = false, hit = default };
            return false;
        }

        // the ray (and any hit) reflects the camera as of the first query for this key
        // in the frame. We do not invalidate when the camera moves later in the frame:
        // today each key has a single caller, so this reproduces that caller's existing
        // timing exactly.
        Ray ray = viewCamera.ScreenPointToRay(screenPosition);

        if (Physics.Raycast(ray, out RaycastHit result, maxDistance, mask, triggers))
        {
            hit = result;
            rays[key] = new RayResult { hasHit = true, hit = result };
            return true;
        }

        hit = default;
        rays[key] = new RayResult { hasHit = false, hit = default };
        return false;
    }


    // MARK: frame + camera


    // Cleared lazily so a frame with no pointer query pays nothing
    private void EnsureFrame()
    {
        if (cachedFrame == Time.frameCount) return;

        cachedFrame = Time.frameCount;
        rays.Clear();
        uiHits.Clear();
    }


    // Camera.main is cached so it is not a tag lookup on every query, but re-resolved if
    // it ever goes away, e.g. across a scene transition
    private void ResolveCamera()
    {
        if (viewCamera != null) return;

        viewCamera = Camera.main;
        if (viewCamera == null) WarnMissingCamera();
    }


    private void WarnMissingCamera()
    {
        if (warnedAboutCamera) return;

        warnedAboutCamera = true;
        Debug.LogError("PointerRaycastManager: no Camera.main, so pointer rays cannot be computed", this);
    }
}
