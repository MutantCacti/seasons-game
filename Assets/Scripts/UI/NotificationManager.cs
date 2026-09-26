using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;


// Owns the pending notifications (a section of the world save) and the pings that show them.
// A notification is (type, target guid, puzzle id) and nothing else: what opening one does lives in
// the one switch in Handle, so a notification restored from disk behaves exactly like a fresh one.
public class NotificationManager : MonoBehaviour
{
    private static NotificationManager instance;
    public static NotificationManager Instance { get { return instance; } }


    [Header("References")]
    [SerializeField] private NotificationPing pingPrefab;

    // world space, with a GraphicRaycaster and the main camera as its event camera
    [SerializeField] private Canvas pingCanvas;

    // on the ping canvas; hides every ping at once while the book is read or the game is paused
    [SerializeField] private CanvasGroup pingCanvasGroup;

    // the inventory bar, which no ping may cover
    [SerializeField] private RectTransform inventoryPanel;

    [SerializeField] private CameraController cameraController;


    [Header("Icons")]
    [SerializeField] private Sprite completeIcon;
    [SerializeField] private Color completeTint = new Color(0.553f, 0.749f, 0.353f);   // #8DBF5A, warm leaf green
    [SerializeField] private Sprite failIcon;
    [SerializeField] private Color failTint = new Color(0.824f, 0.341f, 0.247f);       // #D2573F, warm brick red


    // one pair per type, all optional; the complete pair keeps what the old shared pair was set to
    [Header("Sound")]
    [FormerlySerializedAs("spawnSound")] [SerializeField] private SoundObject completeSpawnSound;
    [FormerlySerializedAs("openSound")] [SerializeField] private SoundObject completeOpenSound;
    [SerializeField] private SoundObject failSpawnSound;
    [SerializeField] private SoundObject failOpenSound;


    [Header("Layout")]
    [SerializeField] private float edgeMargin = 0.04f;          // fraction of screen height kept clear at every edge
    [SerializeField] private float edgePingHeight = 0.09f;      // fraction of screen height an edge ping stands
    [SerializeField] private float edgeDistance = 1.0f;         // world units in front of the camera an edge ping sits
    [SerializeField] private float hoverWorldHeight = 0.9f;     // world units a hovering ping stands
    [SerializeField] private float hoverGap = 0.15f;            // world units between the target's top and the tip
    [SerializeField] private float inventoryPadding = 12.0f;    // pixels kept clear around the inventory bar
    [SerializeField] private float parkedSpacing = 0.07f;       // fraction of screen height between parked pings
    [SerializeField] private float parkedGap = 8.0f;            // pixels between the inventory bar and a parked tip


    [Header("Feel")]
    [SerializeField] private float blendDuration = 0.35f;       // edge <-> hover flight
    [SerializeField] private float bobAmplitude = 0.06f;        // world units, while hovering
    [SerializeField] private float bobSpeed = 2.2f;             // radians per second
    [SerializeField] private float edgePoke = 6.0f;             // pixels an edge ping nudges toward its target
    [SerializeField] private float edgeFollow = 18.0f;          // how tightly an edge ping follows its edge point
    [SerializeField] private float spawnStagger = 0.08f;        // seconds between pings restored together
    [SerializeField] private float suppressFadeDuration = 0.15f;


    private NotificationSaveData saved;

    private readonly List<NotificationPing> pings = new List<NotificationPing>();

    // false until the pings have been rebuilt from the save; a Submit before then only writes the save
    private bool restored;

    private Camera viewCamera;
    private Transform listener;

    private readonly Vector3[] corners = new Vector3[4];
    private Rect usableArea;
    private Rect inventoryRect;
    private bool hasInventoryRect;

    private bool suppressed;
    private Tween suppressTween;

    private bool warnedAboutMissingPrefab;
    private bool warnedAboutMissingCamera;


    // MARK: frame data for the pings


    public bool IsSuppressed { get { return suppressed; } }
    public Camera ViewCamera { get { return viewCamera; } }
    public Rect UsableArea { get { return usableArea; } }
    public Rect InventoryRect { get { return inventoryRect; } }
    public bool HasInventoryRect { get { return hasInventoryRect; } }

    public float EdgeDistance { get { return edgeDistance; } }
    public float EdgePingPixels { get { return edgePingHeight * Screen.height; } }
    public float HoverWorldHeight { get { return hoverWorldHeight; } }
    public float HoverGap { get { return hoverGap; } }
    public float BlendDuration { get { return blendDuration; } }
    public float BobAmplitude { get { return bobAmplitude; } }
    public float BobSpeed { get { return bobSpeed; } }
    public float EdgePoke { get { return edgePoke; } }
    public float EdgeFollow { get { return edgeFollow; } }


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one NotificationManager in the scene - destroying newest!!");
            Destroy(gameObject);
            return;
        }

        instance = this;

        ResolveSave();

        if (pingCanvasGroup == null && pingCanvas != null) pingCanvasGroup = pingCanvas.GetComponent<CanvasGroup>();
    }


    private void Start()
    {
        if (pingPrefab == null) Debug.LogError("NotificationManager: no pingPrefab, so no notification can ever be shown or opened", this);
        if (pingCanvas == null) Debug.LogError("NotificationManager: no pingCanvas, so pings have nowhere to live", this);
        if (pingCanvasGroup == null) Debug.LogError("NotificationManager: no CanvasGroup on the ping canvas, so pings stay clickable while the book is open", this);
        if (inventoryPanel == null) Debug.LogError("NotificationManager: no inventoryPanel, so edge pings may sit on top of the inventory bar", this);

        if (cameraController == null) cameraController = FindAnyObjectByType<CameraController>();
        if (cameraController == null) Debug.LogError("NotificationManager: no CameraController, so clicking an edge ping cannot pan to it", this);

        if (edgeDistance <= 0.0f)
        {
            Debug.LogError("NotificationManager: edgeDistance must be positive, so it has been reset to 1", this);
            edgeDistance = 1.0f;
        }

        ResolveCamera();

        if (PuzzleManager.Instance == null)
        {
            Debug.LogError("NotificationManager: no PuzzleManager, so saved notifications are restored without being checked", this);
            RestorePings();
            return;
        }

        // sticky, so this runs now if puzzles have already loaded
        PuzzleManager.Instance.OnPuzzlesLoaded += RestorePings;
    }


    private void OnDestroy()
    {
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.OnPuzzlesLoaded -= RestorePings;

        if (suppressTween != null) suppressTween.Kill();

        if (instance == this) instance = null;
    }


    // The camera has moved in its Update, so everything here sees this frame's view. Pings are driven
    // from here rather than from their own LateUpdate so they always read this frame's layout.
    private void LateUpdate()
    {
        ResolveCamera();
        UpdateSuppressed();
        RefreshLayout();

        // backwards, because a ping that finishes exiting drops out of the list
        for (int i = pings.Count - 1; i >= 0; i--)
        {
            if (pings[i] == null)
            {
                pings.RemoveAt(i);
                continue;
            }

            pings[i].Tick();
        }
    }


    // MARK: submission


    // The one way to raise a notification. (type, puzzle) is its identity, so a repeat is absorbed:
    // soft locks are re-announced on every load, and the restored notification already covers them.
    public void Submit(NotificationType type, EntityId target, Puzzle puzzle)
    {
        Submit(type, target != null ? target.Guid : null, puzzle);
    }


    // The same, by guid, for a target that may not be in the world yet: a completion replayed on load
    // can arrive before its entity has registered, and the ping resolves the guid when it can
    public void Submit(NotificationType type, string targetGuid, Puzzle puzzle)
    {
        if (puzzle == null)
        {
            Debug.LogError($"NotificationManager: a {type} notification was submitted with no puzzle, so it was dropped", this);
            return;
        }

        if (!TryGetPuzzleId(puzzle, out string puzzleId)) return;

        if (saved.IndexOf(type, puzzleId) >= 0) return;

        string guid = targetGuid ?? "";

        if (string.IsNullOrEmpty(guid)) Debug.LogWarning($"NotificationManager: the {type} notification for '{puzzle.name}' has no target, " +
            $"so its ping will wait above the inventory instead", this);

        NotificationSave entry = new NotificationSave
        {
            type = type,
            targetEntityGuid = guid,
            puzzleId = puzzleId,
        };

        saved.pending.Add(entry);
        Save();

        if (restored) SpawnPing(entry, 0.0f, SpawnSoundFor(type));
    }


    // Takes a notification back without opening it, e.g. a soft lock that has since been completed
    public void Withdraw(NotificationType type, Puzzle puzzle)
    {
        if (puzzle == null) return;
        if (!TryGetPuzzleId(puzzle, out string puzzleId)) return;

        int index = saved.IndexOf(type, puzzleId);
        if (index < 0) return;

        saved.pending.RemoveAt(index);
        Save();

        NotificationPing ping = FindPing(type, puzzleId);
        if (ping != null) ping.Exit();
    }


    // MARK: opening


    // Clear and save first, handle second: the unlock a CompletePuzzle handler grants takes a
    // checkpoint, and that checkpoint must not still hold the notification that was just used up.
    //
    // A fail ping is never used up by a click: it stays for as long as its puzzle is soft locked, and
    // only Withdraw (the puzzle completed) or a reload of a checkpoint without it takes it away.
    public void OnPingClicked(NotificationPing ping)
    {
        if (suppressed) return;
        if (ping == null || ping.IsExiting) return;

        if (ping.Type == NotificationType.FailPuzzle)
        {
            PlaySound(OpenSoundFor(ping.Type), ping);
            Handle(ping.Type, ping.PuzzleId);
            return;
        }

        int index = saved.IndexOf(ping.Type, ping.PuzzleId);

        if (index >= 0)
        {
            saved.pending.RemoveAt(index);
            Save();
        }

        PlaySound(OpenSoundFor(ping.Type), ping);

        ping.Exit();

        Handle(ping.Type, ping.PuzzleId);
    }


    private void Handle(NotificationType type, string puzzleId)
    {
        switch (type)
        {
            case NotificationType.CompletePuzzle:
            {
                PuzzleManager puzzles = PuzzleManager.Instance;

                if (puzzles == null || puzzles.Registry == null)
                {
                    Debug.LogError($"NotificationManager: no PuzzleManager or registry, so opening the completion of " +
                        $"'{puzzleId}' unlocked nothing", this);
                    return;
                }

                if (!puzzles.Registry.TryGetById(puzzleId, out Puzzle puzzle))
                {
                    Debug.LogError($"NotificationManager: no puzzle has id '{puzzleId}', so opening its completion " +
                        $"unlocked nothing", this);
                    return;
                }

                puzzles.GrantUnlocks(puzzle);
                break;
            }

            case NotificationType.FailPuzzle:
            {
                if (BookAnimator.Instance == null)
                {
                    Debug.LogError("NotificationManager: no BookAnimator, so a soft lock notification cannot open the book", this);
                    return;
                }

                // this ping's puzzle, not just the first soft locked one, is the page the book opens on
                PuzzleTabManager.RequestPuzzle(puzzleId);

                BookAnimator.Instance.Open();
                break;
            }

            default:
                Debug.LogError($"NotificationManager: no handler for notification type {type}", this);
                break;
        }
    }


    public void PanTo(Transform target)
    {
        if (target == null) return;

        if (cameraController == null)
        {
            Debug.LogError("NotificationManager: no CameraController, so the camera cannot pan to this ping", this);
            return;
        }

        cameraController.PanToTransform(target);
    }


    // a ping whose target is a terrain cell rather than an entity
    public void PanToPoint(Vector3 point)
    {
        if (cameraController == null)
        {
            Debug.LogError("NotificationManager: no CameraController, so the camera cannot pan to this ping", this);
            return;
        }

        cameraController.PanToPoint(point);
    }


    // MARK: pings


    // Rebuilds the pings from the save once puzzle state is trustworthy, dropping anything that no
    // longer makes sense against it
    private void RestorePings()
    {
        if (restored) return;
        restored = true;

        bool dropped = false;

        for (int i = saved.pending.Count - 1; i >= 0; i--)
        {
            if (IsStale(saved.pending[i]))
            {
                saved.pending.RemoveAt(i);
                dropped = true;
            }
        }

        if (dropped) Save();

        // One sound for the whole batch, not a chord of them. It plays with the first ping to grow, but
        // is picked by the most urgent type in the batch, so a waiting soft lock is never announced
        // with the cheerful sound.
        SoundObject batchSound = null;

        if (saved.pending.Count > 0)
        {
            batchSound = SpawnSoundFor(ContainsType(NotificationType.FailPuzzle)
                ? NotificationType.FailPuzzle
                : NotificationType.CompletePuzzle);
        }

        for (int i = 0; i < saved.pending.Count; i++)
        {
            SpawnPing(saved.pending[i], i * spawnStagger, i == 0 ? batchSound : null);
        }
    }


    private bool IsStale(NotificationSave entry)
    {
        if (entry == null) return true;

        PuzzleManager puzzles = PuzzleManager.Instance;

        // nothing to check against, so keep it rather than lose the player's progress
        if (puzzles == null || puzzles.Registry == null) return false;

        if (!puzzles.Registry.TryGetById(entry.puzzleId, out Puzzle puzzle))
        {
            Debug.LogError($"NotificationManager: the save holds a {entry.type} notification for '{entry.puzzleId}', " +
                $"but no puzzle has that id - dropping it", this);
            return true;
        }

        if (entry.type == NotificationType.CompletePuzzle && !puzzles.IsCompleted(puzzle))
        {
            Debug.LogError($"NotificationManager: the save holds a completion notification for '{puzzle.name}', " +
                $"which is not completed - dropping it", this);
            return true;
        }

        if (entry.type == NotificationType.FailPuzzle && puzzles.IsCompleted(puzzle))
        {
            Debug.LogWarning($"NotificationManager: the save holds a soft lock notification for '{puzzle.name}', " +
                $"which has since been completed - dropping it", this);
            return true;
        }

        return false;
    }


    // sound is played as the ping starts to grow; null for silent
    private void SpawnPing(NotificationSave entry, float delay, SoundObject sound)
    {
        if (pingPrefab == null || pingCanvas == null)
        {
            if (!warnedAboutMissingPrefab)
            {
                warnedAboutMissingPrefab = true;
                Debug.LogError("NotificationManager: no pingPrefab or pingCanvas, so a notification was saved but " +
                    "has no ping to open it with", this);
            }

            return;
        }

        NotificationPing ping = Instantiate(pingPrefab, pingCanvas.transform, false);
        ping.name = $"Ping ({entry.type} {entry.puzzleId})";

        bool complete = entry.type == NotificationType.CompletePuzzle;

        ping.Init(this, entry.type, entry.targetEntityGuid, entry.puzzleId,
            complete ? completeIcon : failIcon, complete ? completeTint : failTint, delay, sound);

        pings.Add(ping);
    }


    public void Forget(NotificationPing ping)
    {
        pings.Remove(ping);
    }


    private NotificationPing FindPing(NotificationType type, string puzzleId)
    {
        for (int i = 0; i < pings.Count; i++)
        {
            NotificationPing ping = pings[i];
            if (ping == null || ping.IsExiting) continue;

            if (ping.Type == type && ping.PuzzleId == puzzleId) return ping;
        }

        return null;
    }


    private SoundObject SpawnSoundFor(NotificationType type)
    {
        return type == NotificationType.FailPuzzle ? failSpawnSound : completeSpawnSound;
    }


    private SoundObject OpenSoundFor(NotificationType type)
    {
        return type == NotificationType.FailPuzzle ? failOpenSound : completeOpenSound;
    }


    private bool ContainsType(NotificationType type)
    {
        for (int i = 0; i < saved.pending.Count; i++)
        {
            if (saved.pending[i] != null && saved.pending[i].type == type) return true;
        }

        return false;
    }


    // positional at the target while it is on screen, at the listener otherwise so an edge ping is not
    // quietened by being far away
    public void PlaySound(SoundObject sound, NotificationPing ping)
    {
        if (sound == null || AudioManager.Instance == null) return;

        Vector3 position;

        if (ping != null && ping.IsOnScreen && ping.HasTarget) position = ping.TargetPosition;
        else position = ListenerPosition();

        AudioManager.Instance.PlaySFX(sound, position);
    }


    // looked up on first use: CameraController creates the listener in its own Start
    private Vector3 ListenerPosition()
    {
        if (listener == null)
        {
            AudioListener found = FindAnyObjectByType<AudioListener>();
            if (found != null) listener = found.transform;
        }

        if (listener != null) return listener.position;

        return viewCamera != null ? viewCamera.transform.position : Vector3.zero;
    }


    // MARK: layout


    // Where a parked ping (one with no resolvable target) waits: just above the inventory, fanned out
    // around the middle of the screen so several of them do not stack
    public Vector2 ParkedPoint(NotificationPing ping)
    {
        int index = 0;
        int count = 0;

        for (int i = 0; i < pings.Count; i++)
        {
            NotificationPing other = pings[i];
            if (other == null || !other.IsParked) continue;

            if (other == ping) index = count;
            count++;
        }

        float spacing = parkedSpacing * Screen.height;
        float x = Screen.width * 0.5f + (index - (count - 1) * 0.5f) * spacing;
        float y = (hasInventoryRect ? inventoryRect.yMax : usableArea.yMin) + parkedGap;

        return new Vector2(x, y);
    }


    // Walks from the screen centre toward the target and stops at whatever it meets first: the edge
    // of the usable area, or the inventory bar. So a ping beside a narrow bar still reaches the bottom.
    public Vector2 EdgePoint(Vector2 centre, Vector2 direction, float pingWidth)
    {
        float t = RayExit(centre, direction, usableArea);

        if (hasInventoryRect)
        {
            // widened by the ping itself, whose body leans back toward the centre from its tip
            Rect blocked = inventoryRect;
            blocked.xMin -= pingWidth * 0.5f;
            blocked.xMax += pingWidth * 0.5f;

            if (!blocked.Contains(centre) && RayEnter(centre, direction, blocked, out float entry) && entry < t)
            {
                t = entry;
            }
        }

        return centre + direction * t;
    }


    private void RefreshLayout()
    {
        float margin = edgeMargin * Screen.height;

        usableArea = new Rect(margin, margin, Screen.width - margin * 2.0f, Screen.height - margin * 2.0f);

        hasInventoryRect = false;

        if (inventoryPanel == null || !inventoryPanel.gameObject.activeInHierarchy) return;

        // the inventory canvas is Screen Space Overlay, so its world corners are screen pixels
        inventoryPanel.GetWorldCorners(corners);

        float xMin = corners[0].x, xMax = corners[0].x, yMin = corners[0].y, yMax = corners[0].y;

        for (int i = 1; i < 4; i++)
        {
            xMin = Mathf.Min(xMin, corners[i].x);
            xMax = Mathf.Max(xMax, corners[i].x);
            yMin = Mathf.Min(yMin, corners[i].y);
            yMax = Mathf.Max(yMax, corners[i].y);
        }

        inventoryRect = Rect.MinMaxRect(xMin - inventoryPadding, yMin - inventoryPadding,
            xMax + inventoryPadding, yMax + inventoryPadding);

        hasInventoryRect = inventoryRect.width > 0.0f && inventoryRect.height > 0.0f;
    }


    // how far along the ray an origin inside the rect leaves it
    private static float RayExit(Vector2 origin, Vector2 direction, Rect rect)
    {
        float tx = float.PositiveInfinity;
        float ty = float.PositiveInfinity;

        if (direction.x > 0.0f) tx = (rect.xMax - origin.x) / direction.x;
        else if (direction.x < 0.0f) tx = (rect.xMin - origin.x) / direction.x;

        if (direction.y > 0.0f) ty = (rect.yMax - origin.y) / direction.y;
        else if (direction.y < 0.0f) ty = (rect.yMin - origin.y) / direction.y;

        float t = Mathf.Min(tx, ty);

        return float.IsInfinity(t) ? 0.0f : Mathf.Max(0.0f, t);
    }


    // slab test: how far along the ray an origin outside the rect enters it
    private static bool RayEnter(Vector2 origin, Vector2 direction, Rect rect, out float entry)
    {
        entry = 0.0f;

        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;

        if (!Slab(origin.x, direction.x, rect.xMin, rect.xMax, ref tMin, ref tMax)) return false;
        if (!Slab(origin.y, direction.y, rect.yMin, rect.yMax, ref tMin, ref tMax)) return false;

        if (tMin > tMax || tMin <= 0.0f) return false;

        entry = tMin;
        return true;
    }


    private static bool Slab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (Mathf.Approximately(direction, 0.0f)) return origin >= min && origin <= max;

        float a = (min - origin) / direction;
        float b = (max - origin) / direction;

        if (a > b) { float swap = a; a = b; b = swap; }

        tMin = Mathf.Max(tMin, a);
        tMax = Mathf.Min(tMax, b);

        return true;
    }


    // MARK: internals


    private void UpdateSuppressed()
    {
        bool shouldSuppress = Time.timeScale <= 0.0f || (BookAnimator.Instance != null && BookAnimator.Instance.IsReading);

        if (shouldSuppress == suppressed) return;

        suppressed = shouldSuppress;

        if (pingCanvasGroup == null) return;

        pingCanvasGroup.interactable = !suppressed;
        pingCanvasGroup.blocksRaycasts = !suppressed;

        if (suppressTween != null) suppressTween.Kill();

        // unscaled, because the pause that hides them has stopped scaled time
        suppressTween = pingCanvasGroup.DOFade(suppressed ? 0.0f : 1.0f, suppressFadeDuration)
            .SetEase(suppressed ? Ease.InQuad : Ease.OutQuad)
            .SetUpdate(true);
    }


    private void ResolveCamera()
    {
        if (viewCamera != null) return;

        if (cameraController != null) viewCamera = cameraController.GetComponent<Camera>();
        if (viewCamera == null) viewCamera = Camera.main;

        if (viewCamera == null && !warnedAboutMissingCamera)
        {
            warnedAboutMissingCamera = true;
            Debug.LogError("NotificationManager: no camera, so pings cannot be placed", this);
        }
    }


    // The save managers own the live world. A scene without one still runs, but its notifications
    // never reach disk.
    private void ResolveSave()
    {
        SaveDataManager manager = SaveDataManager.Instance;
        WorldState world = manager != null && manager.SaveData != null ? manager.SaveData.world : null;

        if (world == null)
        {
            Debug.LogError("NotificationManager: no SaveDataManager in the scene, so notifications will not be saved", this);
            world = new WorldState();
        }

        world.EnsureSections();

        saved = world.notifications;
    }


    private bool TryGetPuzzleId(Puzzle puzzle, out string puzzleId)
    {
        puzzleId = null;

        PuzzleManager puzzles = PuzzleManager.Instance;

        if (puzzles == null || puzzles.Registry == null)
        {
            Debug.LogError($"NotificationManager: no PuzzleManager or registry, so '{puzzle.name}' has no id and its " +
                $"notification was dropped", this);
            return false;
        }

        if (!puzzles.Registry.TryGetIdByEntry(puzzle, out puzzleId))
        {
            Debug.LogError($"NotificationManager: '{puzzle.name}' has no id in the registry, so its notification was dropped", puzzle);
            return false;
        }

        return true;
    }


    private void Save()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
    }
}
