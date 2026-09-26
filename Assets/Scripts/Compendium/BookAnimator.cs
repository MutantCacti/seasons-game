using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;


public class BookAnimator : MonoBehaviour
{
    // There is one book. It is a saved entity, so a duplicate is only warned about and never destroyed:
    // destroying it would take it out of the world. Read IsReading off this.
    private static BookAnimator instance;
    public static BookAnimator Instance { get { return instance; } }


    // Raised when a read starts, whether from a click or from Open
    public event Action Opened;


    [Serializable]
    private struct BookPart
    {
        public Transform part;

        public float openAngle;

        public float liftOnOpen;

        public float openDurationScale;

        public bool loosePage;
    }


    // Per part runtime
    private sealed class PartState
    {
        public BookPart config;
        public Vector3 restPosition;
        public Quaternion restRotation;

        public float open;          // 0 closed, 1 open; driven by this part's own tween
        public float flip;          // 0 authored side, 1 turned over; those are the sheet's two sides
        public float flipTarget;    // where a turn was heading, so an interrupted one settles there
        public float driftPhase;    // random per page, so the sheets do not move in lockstep
        public float packAngle;     // the angle the sheet lies at while the read has it packed flat
        public bool placed;         // scratch, while the pack is worked out from the widest sheet in

        public Tween openTween;
        public Tween flipTween;
        public bool flipping;
    }


    [SerializeField] private BookPart[] parts;

    // the Collider child of this prefab
    [SerializeField] private Collider hoverTarget;

    [SerializeField] private Camera viewCamera;
    [SerializeField] private float hoverRayDistance = 1000.0f;

    // resolved on first use rather than in Awake, since a hover check can come before it. 0 means
    // unresolved: ~GetMask can't be 0, it has every layer but Water
    private int hoverMask;

    [SerializeField] private Transform yawTarget;   // the book model

    // Degrees of fine tuning on top of the spine base aim below
    [SerializeField] private float yawOffset;

    // The book's own direction that its spine faces, which is straight down while it stands at rest
    [SerializeField] private Vector3 spineFacing = Vector3.down;


    [Header("Open")]
    [SerializeField] private float openDuration = 0.35f;
    [SerializeField] private Ease openEase = Ease.OutBack;
    [SerializeField] private Ease closeEase = Ease.InOutQuad;

    [Header("Float")]
    [SerializeField] private float bobAmplitude = 0.03f;
    [SerializeField] private float bobPeriod = 3.5f;

    [Header("Loose pages")]
    [SerializeField] private float driftDegrees = 4.0f;
    [SerializeField] private float driftLift = 0.01f;
    [SerializeField] private float driftSpeed = 0.7f;

    // Degrees kept between a turning page and the top sheet of the side it turns onto
    [SerializeField] private float flipClearance = 2.0f;
    [SerializeField] private float flipIntervalMin = 3.0f;
    [SerializeField] private float flipIntervalMax = 6.0f;

    // How far into the read flight the loose sheets drop out. Past 1 they never do. Dropping them
    // partway through means the movement covers the change, instead of the click showing it.
    [SerializeField] private float looseSheetsHideAt = 0.25f;


    [Header("Read")]    // the click pose: out at the camera and open the rest of the way
    [SerializeField] private float readDistance = 3.0f;

    // Degrees the widest part opens to when read, which scales onto every other part
    [SerializeField] private float readOpenAngle = 90.0f;

    // The read still takes this long, but the pages reach 90 degrees at readFlatAt of the way
    // through and hold there for the rest, so a menu drawn on them arrives on a settled book
    [SerializeField] private float readDuration = 0.6f;
    [SerializeField] [Range(0.05f, 1.0f)] private float readFlatAt = 0.4f;
    [SerializeField] private Ease readEase = Ease.InOutQuad;

    // How much bigger than the view the hover volume is while the book is out. Blown up like this a
    // click anywhere the menu does not own lands on the book and closes it, and the menu still wins
    // the pointer because its own raycasts are checked before physics.
    [SerializeField] private float readColliderMargin = 1.1f;


    private const float SPINE_BASE_YAW = -90.0f;

    private const float FLIP_OPEN_THRESHOLD = 0.9f;

    private const float FLIP_TURN_DURATION = 0.4f;

    private const float FLIP_REFERENCE_TRAVEL = 40.0f;

    private const float MIN_FLIP_TRAVEL = 5.0f;

    private const float MIN_FLIP_TURN_SCALE = 0.6f;


    private readonly List<PartState> states = new List<PartState>();
    private readonly List<PartState> loosePages = new List<PartState>();
    private readonly List<PartState> flipCandidates = new List<PartState>();

    private float openAmount;
    private Tween openTween;
    private Tween bobTween;
    private float bobRestY;

    private PartState lastFlipped;
    private float nextFlipTime;
    private bool wasHovered;
    private bool pointerHovering;

    // the left button as of the last event, and whether this book is the one holding the cursor. The
    // hand has to be pushed rather than polled, see UpdateCursor
    private bool pointerHeld;
    private bool ownsCursor;

    // Anything else that counts as the pointer being on the book, e.g. UI in front of it, which
    // IsHovered ignores on purpose. A set, so several sources cannot fight over one flag.
    private readonly HashSet<object> externalHover = new HashSet<object>();
    private static readonly Predicate<object> IsDeadSource = IsDestroyedObject;

    private bool warnedAboutParts;
    private bool warnedAboutHoverTarget;
    private bool warnedAboutYawTarget;
    private bool warnedAboutInput;
    private bool warnedAboutCursor;

    private Transform readPivot;    // runtime pivot, so the flight does not fight the bob or the yaw
    private Vector3 readOffset;     // the book node's offset inside that pivot, for aiming its base
    private float readScale = 1.0f; // the pose multiplier that brings the widest part to readOpenAngle
    private float read;             // 0 closed, 1 fully out and laid back; drives the flight and the tip
    private bool readTarget;        // where the last SetRead was heading; read itself is still > 0 while closing
    private float fan;              // 0 folded, 1 the pages at readOpenAngle; reaches 1 early
    private Tween readTween;
    private Tween fanTween;
    private bool looseSheetsShown = true;
    private bool ambientRunning = true;

    // The hover volume's authored shape and pose, so covering the view can be undone. The box is
    // resized directly when it is one; anything else is scaled.
    private BoxCollider hoverBox;
    private Vector3 hoverBoxSize;
    private Vector3 hoverBoxCenter;
    private Vector3 hoverBaseSize;
    private Vector3 hoverLocalPosition;
    private Quaternion hoverLocalRotation;
    private Vector3 hoverLocalScale;
    private bool hoverIsTrigger;
    private bool hoverPoseCaptured;
    private bool hoverCovering;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("BookAnimator: found more than one book - BookAnimator.Instance keeps the first", this);
        }
        else
        {
            instance = this;
        }

        if (viewCamera == null) viewCamera = Camera.main;

        // captured once, so restarting the bob after a read settles the book at the same height
        if (yawTarget != null) bobRestY = yawTarget.localPosition.y;

        BuildStates();
        BuildReadPivot();
        CaptureHoverPose();
        StartBob();
        ApplyAll();
    }


    private void OnEnable()
    {
        // a press that spans a disable would otherwise still read as held when this comes back
        pointerHeld = false;

        InputManager input = InputManager.Instance;

        if (input == null)
        {
            WarnOnce(ref warnedAboutInput, "no InputManager, so the book cannot be clicked open");
            return;
        }

        input.onLeftClickDown += OnLeftClickDown;
    }


    private void OnDisable()
    {
        InputManager input = InputManager.Instance;

        if (input != null) input.onLeftClickDown -= OnLeftClickDown;

        // a claim that outlived the book would strand the cursor on the hand
        ReleaseCursor();
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;

        KillTween(ref openTween);
        KillTween(ref bobTween);
        KillTween(ref readTween);
        KillTween(ref fanTween);

        for (int i = 0; i < states.Count; i++)
        {
            KillTween(ref states[i].openTween);
            KillTween(ref states[i].flipTween);
        }
    }


    private void Update()
    {
        // the state machine stops with the rest of the game, so a hover that changes under the pause menu
        // cannot start a close and snap a page that was mid turn; the tweens are on scaled time too
        if (Time.timeScale <= 0.0f)
        {
            // the cursor is not part of the frozen pose: a claim held into the pause would also sit on
            // the pause menu's own buttons, so it is handed back for as long as the game is stopped
            ReleaseCursor();
            return;
        }

        UpdateHover();
        UpdateFlips();
    }


    // LateUpdate so the camera has already settled for this frame
    private void LateUpdate()
    {
        UpdateRead();
        UpdateReadCollider();
        UpdateAmbient();
        UpdateLooseSheets();
        UpdateYaw();
        ApplyAll();
    }


    // MARK: read


    // The click pose, driven from outside: a physical canvas now, and later the menu that sits on it
    // owns the way back. For the moment the click is the only thing calling this.
    public void SetRead(bool reading)
    {
        // Asking for the way it is already going changes nothing. Restarting would replay the whole
        // tween from here, and on a close that includes CloseFan's hold, so repeated clicks froze it.
        if (reading == readTarget && readTween != null && readTween.IsActive()) return;

        // only the closed -> reading edge counts, so repeating an open does not raise it again
        bool starting = reading && !readTarget;

        readTarget = reading;

        // the screen-sized click catcher only belongs to an open read, so a close hands the screen
        // back this frame rather than when the book lands
        if (!reading && hoverCovering) RestoreHoverPose();

        KillTween(ref readTween);
        KillTween(ref fanTween);

        // scaled, so a pause freezes the book where it stands instead of animating it behind the menu
        readTween = DOTween.To(() => read, value => read = value, reading ? 1.0f : 0.0f, readDuration)
            .SetEase(readEase);

        // Only the fan is pulled forward: it is what decides when the pages are flat, so the menu can
        // arrive early while the flight and the lay-back still take the whole read. Shaping read
        // itself would shorten the whole read instead.
        fanTween = reading ? OpenFan() : CloseFan();

        if (starting) Opened?.Invoke();
    }


    // Opens the book from code, exactly as clicking it does. Guarded on where the read is heading rather
    // than on IsReading, which stays true for the whole close, so an open during the close still opens.
    public void Open()
    {
        if (readTarget) return;

        // The covers open on hover, not on read, and a click can only land on a hovered book. Coming from
        // code there was no hover, so do what a hover-enter would; once the read is back to 0,
        // UpdateHover sees the real hover state differ from this and closes them as usual.
        wasHovered = true;
        TweenTo(1.0f);

        SetRead(true);

        if (CompendiumUIManager.Instance != null) CompendiumUIManager.Instance.OnOpenCloseCompendium(true);
    }


    // Pages rise to flat over the first readFlatAt of the read and then hold there
    private Tween OpenFan()
    {
        float flatAt = readDuration * Mathf.Clamp(readFlatAt, 0.05f, 1.0f);

        return DOTween.To(() => fan, value => fan = value, 1.0f, flatAt).SetEase(Ease.OutQuad);
    }


    // The same thing backwards: the pages hold flat for the first (1 - readFlatAt) of the way out and
    // only then fold, so they stay at 90 degrees for as long as the menu is still on them
    private Tween CloseFan()
    {
        float foldAt = Mathf.Clamp(readFlatAt, 0.05f, 1.0f);
        float foldDuration = readDuration * foldAt;
        float hold = readDuration - foldDuration;

        Sequence closing = DOTween.Sequence();
        closing.AppendInterval(hold);
        closing.Append(DOTween.To(() => fan, value => fan = value, 0.0f, foldDuration).SetEase(Ease.InQuad));

        return closing;
    }


    public bool IsReading { get { return read > 0.0f; } }


    // 0 closed, 1 fully out and laid back; the whole read takes readDuration
    public float ReadAmount { get { return read; } }


    // 0 folded, 1 with the pages at readOpenAngle. A menu sitting on the pages waits for this rather
    // than for ReadAmount, because only the fan decides whether the pages are flat.
    public float PagesFlat { get { return fan; } }


    private void OnLeftClickDown(bool pressed)
    {
        // remembered for the cursor too: a held button is a drag or a press in progress, never a hover,
        // and the drag that already owns the cursor must not have it taken away mid-move
        pointerHeld = pressed;

        // the press only, and not while a menu has the game stopped behind it
        if (!pressed || Time.timeScale <= 0.0f) return;
        if (!IsHovered()) return;

        // toggled on where the read is heading, not on IsReading: that stays true for the whole
        // close, so a click on the descending book would close it again instead of catching it
        bool reading = !readTarget;

        SetRead(reading);

        if (CompendiumUIManager.Instance != null) CompendiumUIManager.Instance.OnOpenCloseCompendium(reading);
    }


    // Slides the book out in front of the camera, on its own pivot so the bob and the yaw keep theirs
    private void UpdateRead()
    {
        if (readPivot == null) return;

        if (read <= 0.0f)
        {
            readPivot.localPosition = Vector3.zero;
            return;
        }

        if (viewCamera == null) return;

        // where the book's base settles: out along the view, dropped so its body sits in the middle
        Vector3 point = viewCamera.transform.position
            + viewCamera.transform.forward * readDistance;

        // the book node keeps its own offset inside the pivot, so the pivot takes the difference
        Vector3 local = readPivot.parent.InverseTransformPoint(point) - readOffset;

        readPivot.localPosition = Vector3.Lerp(Vector3.zero, local, read);
    }


    private void CaptureHoverPose()
    {
        if (hoverTarget == null) return;

        Transform volume = hoverTarget.transform;

        hoverLocalPosition = volume.localPosition;
        hoverLocalRotation = volume.localRotation;
        hoverLocalScale = volume.localScale;
        hoverBaseSize = hoverTarget.bounds.size;
        hoverIsTrigger = hoverTarget.isTrigger;

        hoverBox = hoverTarget as BoxCollider;
        if (hoverBox != null)
        {
            hoverBoxSize = hoverBox.size;
            hoverBoxCenter = hoverBox.center;
        }

        hoverPoseCaptured = true;
    }


    // While the book is out at the camera the hover volume is stretched over the whole view, so a
    // click anywhere the menu does not own lands on the book and closes it. It is put back the moment
    // a close starts, so the descending book only answers to a click on itself.
    private void UpdateReadCollider()
    {
        if (!hoverPoseCaptured) return;

        if (read <= 0.0f || !readTarget)
        {
            if (hoverCovering) RestoreHoverPose();
            return;
        }

        if (viewCamera == null) return;

        hoverCovering = true;

        // a plane in front of the camera, far enough out that the book itself is not clipped by it
        float distance = Mathf.Max(readDistance, viewCamera.nearClipPlane + 0.05f);
        Vector3 point = viewCamera.transform.position + viewCamera.transform.forward * distance;

        // the half extents of the view at that plane, so every corner ray is inside the volume
        float halfHeight = viewCamera.orthographic
            ? viewCamera.orthographicSize
            : distance * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * viewCamera.aspect;

        float depth = Mathf.Max(distance * 0.5f, 0.1f);

        // never smaller than the view itself, whatever the field is set to
        float margin = Mathf.Max(readColliderMargin, 1.0f);

        Transform volume = hoverTarget.transform;
        volume.SetPositionAndRotation(point, viewCamera.transform.rotation);

        // a click catcher only: as a trigger it can never shove the world around while it is huge
        hoverTarget.isTrigger = true;

        Vector3 cover = new Vector3(
            2.0f * halfWidth * margin,
            2.0f * halfHeight * margin,
            depth);

        if (hoverBox != null)
        {
            // the volume now faces the camera, so its own axes line up with the view
            Vector3 scale = volume.lossyScale;
            hoverBox.center = Vector3.zero;
            hoverBox.size = new Vector3(
                cover.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                cover.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                cover.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
            return;
        }

        // not a box: scale the authored footprint up to the same cover
        volume.localScale = new Vector3(
            hoverLocalScale.x * cover.x / Mathf.Max(hoverBaseSize.x, 0.0001f),
            hoverLocalScale.y * cover.y / Mathf.Max(hoverBaseSize.y, 0.0001f),
            hoverLocalScale.z * cover.z / Mathf.Max(hoverBaseSize.z, 0.0001f));
    }


    private void RestoreHoverPose()
    {
        hoverCovering = false;

        Transform volume = hoverTarget.transform;
        volume.localPosition = hoverLocalPosition;
        volume.localRotation = hoverLocalRotation;
        volume.localScale = hoverLocalScale;

        hoverTarget.isTrigger = hoverIsTrigger;

        if (hoverBox != null)
        {
            hoverBox.size = hoverBoxSize;
            hoverBox.center = hoverBoxCenter;
        }
    }


    // The sheets drop out and come back with the movement, and only pay for a hierarchy change on the
    // frame the state actually flips
    private void UpdateLooseSheets()
    {
        bool shown = read < looseSheetsHideAt;

        if (shown == looseSheetsShown) return;

        looseSheetsShown = shown;

        for (int i = 0; i < loosePages.Count; i++)
        {
            loosePages[i].config.part.gameObject.SetActive(shown);
        }
    }


    private void BuildReadPivot()
    {
        if (yawTarget == null) return;

        GameObject pivot = new GameObject("ReadPivot");

        readPivot = pivot.transform;
        readPivot.SetParent(yawTarget.parent, false);
        readPivot.localPosition = Vector3.zero;
        readPivot.localRotation = Quaternion.identity;
        readPivot.localScale = Vector3.one;

        // world position stays, so nothing about the book moves on the frame this runs
        yawTarget.SetParent(readPivot, true);

        readOffset = yawTarget.localPosition;

        // the hover volume comes along, or the click could not reach the book once it has flown out
        if (hoverTarget != null) hoverTarget.transform.SetParent(readPivot, true);
    }


    // MARK: open


    private void UpdateHover()
    {
        // while the book is out at the camera the click owns the pose, so the hover lets it be
        if (read > 0.0f)
        {
            pointerHovering = false;

            // the read's click catcher covers the whole view, so a hand everywhere would be wrong. The
            // menu on the pages earns its own cursor through the ordinary UI hover
            ReleaseCursor();
            return;
        }

        // a source destroyed without letting go would hold the book open forever
        if (externalHover.Count > 0) externalHover.RemoveWhere(IsDeadSource);

        pointerHovering = IsHovered();

        // worked out before the early out below, and off pointerHovering alone: an external hover can
        // hold the covers where they are while the pointer has already left the book
        UpdateCursor();

        bool hovered = pointerHovering || externalHover.Count > 0;

        if (hovered == wasHovered) return;

        wasHovered = hovered;
        TweenTo(hovered ? 1.0f : 0.0f);
    }


    // The book is a world collider, so the UI raycast CursorManager polls can never land on it: the
    // hand has to be pushed as an override instead. It is only held while the button is up, because a
    // placement drag owns the cursor itself and both write the one override - without the held check
    // the two would trade it back and forth frame by frame.
    private void UpdateCursor()
    {
        if (pointerHovering && !pointerHeld)
        {
            TakeCursor();
            return;
        }

        ReleaseCursor();
    }


    // Re-asserted every frame rather than only on the hover edge. There is a single override slot, so
    // another claimant standing down can clear this out from under the book - AnimalSelection does
    // exactly that when it defers to the book on a one frame lag - and an edge triggered claim would
    // never be taken back. SetOverride is built for this: it only submits to the cursor on a real
    // change, so the repeat costs a couple of field writes.
    private void TakeCursor()
    {
        CursorManager cursorManager = CursorManager.Instance;

        if (cursorManager == null)
        {
            WarnOnce(ref warnedAboutCursor, "no CursorManager, so hovering the book will not show the pointer cursor");
            return;
        }

        cursorManager.SetOverride(CursorType.Pointer);
        ownsCursor = true;
    }


    // Only a claim this book actually holds is handed back. The guard also matters on teardown, where
    // this can run after the CursorManager has already been destroyed
    private void ReleaseCursor()
    {
        if (!ownsCursor) return;

        ownsCursor = false;

        CursorManager cursorManager = CursorManager.Instance;
        if (cursorManager != null) cursorManager.ClearOverride();
    }


    private void TweenTo(float target)
    {
        bool opening = target > openAmount;

        KillTween(ref openTween);

        // scaled, so a pause freezes the book where it stands instead of animating it behind the menu
        openTween = DOTween.To(() => openAmount, value => openAmount = value, target, openDuration)
            .SetEase(opening ? openEase : closeEase);

        for (int i = 0; i < states.Count; i++)
        {
            PartState state = states[i];

            KillTween(ref state.openTween);

            // each part on its own clock, which is what lets the loose pages lag behind the covers
            // The per part scale is an opening order only. On the way closed every part takes the same
            // time, because a page still lagging open is a page the cover closing on it sweeps through.
            float duration = opening ? openDuration * ScaleOf(state.config) : openDuration;

            state.openTween = DOTween.To(() => state.open, value => state.open = value, target, duration)
                .SetEase(opening ? openEase : closeEase);
        }

        // a page caught mid turn when the book closes has to settle back, or it stays askew
        if (!opening) CancelFlips();
    }


    private void ApplyAll()
    {
        // the pack is only ever wanted under the read, and it moves with the block, so it is worked out
        // per frame from where the fan actually is
        if (read > 0.0f) AssignPackAngles();

        for (int i = 0; i < states.Count; i++) Apply(states[i]);
    }


    private float PoseScale()
    {
        return Mathf.Lerp(1.0f, readScale, fan);
    }


    private void Apply(PartState state)
    {
        Transform part = state.config.part;

        // the read pose scales every part's angle, which is what takes the widest one to readOpenAngle
        float angle = AngleAt(state, state.flip) * PoseScale() * state.open;

        float lift = state.config.liftOnOpen * state.open;

        if (state.config.loosePage)
        {
            if (driftDegrees > 0.0f)
            {
                // scaled, so the idle sway holds still with the rest of the book while the game is paused
                float sway = Mathf.Sin(Time.time * driftSpeed + state.driftPhase) * openAmount;
                float swing = Mathf.Min(driftDegrees, DriftRoom(state));

                angle += sway * swing;
                lift += sway * driftLift * (swing / driftDegrees);
            }

            // A read packs the sheet flat onto the page block, giving up its fan angle, its lift and its
            // drift together. That is the pose it is hidden in and the pose it comes back in, so neither
            // the vanish nor the return is a sheet changing shape in mid air.
            float pack = PackAmount();

            angle = Mathf.Lerp(angle, state.packAngle, pack);
            lift = Mathf.Lerp(lift, 0.0f, pack);
        }

        part.localRotation = state.restRotation * Quaternion.AngleAxis(angle, Vector3.right);
        part.localPosition = state.restPosition + Vector3.up * lift;
    }


    // How far the sheets have packed themselves flat for the read. The pack finishes exactly where they
    // are hidden, so the hide lands on a stack already lying on the block, and on the way back they
    // reappear in that same packed pose and peel off it as the book comes home.
    private float PackAmount()
    {
        float window = looseSheetsHideAt < 1.0f ? Mathf.Max(looseSheetsHideAt, 0.0001f) : 1.0f;

        return Mathf.Clamp01(read / window);
    }


    // Squeezes every loose sheet up against the part outside it in the fan, one clearance apart, which
    // is the highest each of them can sit without breaking the clearance the rest of the book keeps.
    // The result is a stack pressed flat on the page block that is about to be revealed underneath.
    private void AssignPackAngles()
    {
        for (int i = 0; i < states.Count; i++) states[i].placed = false;

        AssignPackAnglesForSide(1.0f);
        AssignPackAnglesForSide(-1.0f);
    }


    private void AssignPackAnglesForSide(float side)
    {
        float ceiling = 0.0f;
        float search = float.MaxValue;

        for (int step = 0; step < states.Count; step++)
        {
            PartState part = WidestInside(side, search);

            if (part == null) return;

            part.placed = true;
            search = ScaledAngle(part);

            // the covers and the blocks are what the sheets press against, and they do not give ground
            if (!part.config.loosePage)
            {
                ceiling = search;
                continue;
            }

            ceiling -= flipClearance;

            part.packAngle = Mathf.Sign(EffectiveAngle(part)) * ceiling;
        }
    }


    // The part on one side with the largest absolute angle that is still inside the bound and has not
    // been placed, which walks the fan from its widest sheet inwards
    private PartState WidestInside(float side, float bound)
    {
        PartState best = null;
        float bestAngle = float.MinValue;

        for (int i = 0; i < states.Count; i++)
        {
            PartState part = states[i];

            if (part.placed) continue;
            if (Mathf.Sign(EffectiveAngle(part)) != side) continue;

            float angle = ScaledAngle(part);

            if (angle > bound + 0.0001f) continue;
            if (angle <= bestAngle) continue;

            best = part;
            bestAngle = angle;
        }

        return best;
    }


    private float ScaledAngle(PartState part)
    {
        return Mathf.Abs(EffectiveAngle(part)) * PoseScale() * part.open;
    }


    // MARK: float


    private void StartBob()
    {
        if (yawTarget == null) return;

        bobTween = yawTarget.DOLocalMoveY(bobRestY + bobAmplitude, bobPeriod)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }


    // The bob is the one ambient effect on the book node itself, so stopping it also settles the book
    // back to its rest height. The read pose has to land in exactly the same place every time, or a
    // menu sitting on the book would sit somewhere different on every read.
    private void StopBob()
    {
        KillTween(ref bobTween);

        if (yawTarget == null) return;

        Vector3 position = yawTarget.localPosition;
        position.y = bobRestY;
        yawTarget.localPosition = position;
    }


    // Ambient life stops while the book is out, and starts again once it is back on the stump
    private void UpdateAmbient()
    {
        bool running = read <= 0.0f;

        if (running == ambientRunning) return;

        ambientRunning = running;

        if (running) StartBob();
        else StopBob();
    }


    // MARK: loose pages


    private void UpdateFlips()
    {
        if (loosePages.Count == 0) return;

        // a turn started under the read flight would be hidden while it happened, quietly rearranging
        // the fan, so no sheet turns at all until the book is back
        if (read > 0.0f) return;

        if (openAmount < FLIP_OPEN_THRESHOLD) return;
        // scaled, so page turning stops with the rest of the book while the game is paused
        if (Time.time < nextFlipTime) return;

        nextFlipTime = Time.time + UnityEngine.Random.Range(flipIntervalMin, flipIntervalMax);

        flipCandidates.Clear();

        AddTopmostTurnable(1.0f, allowLast: false);
        AddTopmostTurnable(-1.0f, allowLast: false);

        if (flipCandidates.Count == 0)
        {
            AddTopmostTurnable(1.0f, allowLast: true);
            AddTopmostTurnable(-1.0f, allowLast: true);
        }

        if (flipCandidates.Count == 0) return;

        StartFlip(flipCandidates[UnityEngine.Random.Range(0, flipCandidates.Count)]);
    }


    private float AngleAt(PartState page, float flip)
    {
        return page.config.openAngle * (1.0f - 2.0f * flip);
    }


    private float EffectiveAngle(PartState page)
    {
        return AngleAt(page, page.flip);
    }


    // The highest sheet on one side that is free to turn
    private void AddTopmostTurnable(float side, bool allowLast)
    {
        PartState best = null;
        float bestAngle = float.MaxValue;

        for (int i = 0; i < loosePages.Count; i++)
        {
            PartState page = loosePages[i];

            if (page.flipping) continue;
            if (!allowLast && page == lastFlipped) continue;
            if (Mathf.Sign(EffectiveAngle(page)) != side) continue;

            float target;
            if (!TryPlanFlip(page, out target)) continue;

            float angle = Mathf.Abs(EffectiveAngle(page));
            if (angle >= bestAngle) continue;

            best = page;
            bestAngle = angle;
        }

        if (best != null) flipCandidates.Add(best);
    }


    // Where a turn would go or false when it has nowhere to land
    private bool TryPlanFlip(PartState page, out float target)
    {
        target = 0.0f;

        float authored = page.config.openAngle;
        if (Mathf.Abs(authored) <= 0.0001f) return false;

        float angle = EffectiveAngle(page);
        float landing = -angle;

        float destination = TopAngle(-Mathf.Sign(angle));

        if (destination < float.MaxValue)
        {
            // no room left on the far side at all, so the sheet cannot get across the spine
            float room = destination - flipClearance;
            if (room <= 0.0f) return false;

            landing = Mathf.Sign(landing) * Mathf.Min(Mathf.Abs(landing), room);
        }

        if (Mathf.Sign(landing) == Mathf.Sign(angle)) return false;
        if (Mathf.Abs(landing - angle) < MIN_FLIP_TRAVEL) return false;

        target = 0.5f * (1.0f - landing / authored);
        return true;
    }


    // The smallest absolute angle on one side of the pages (the topmost page on one side)
    private float TopAngle(float side)
    {
        float top = float.MaxValue;

        for (int i = 0; i < loosePages.Count; i++)
        {
            float angle = EffectiveAngle(loosePages[i]);

            if (Mathf.Abs(angle) <= 0.0001f) continue;
            if (Mathf.Sign(angle) != side) continue;
            if (Mathf.Abs(angle) >= top) continue;

            top = Mathf.Abs(angle);
        }

        return top;
    }


    private float DriftRoom(PartState page)
    {
        float angle = AngleAt(page, page.flip);

        float room = RoomTo(angle, Neighbour(page, angle, 1.0f));
        room = Mathf.Min(room, RoomTo(angle, Neighbour(page, angle, -1.0f)));

        return Mathf.Max(room, 0.0f);
    }


    // The nearest settled angle in one direction through the pages
    private PartState Neighbour(PartState page, float angle, float direction)
    {
        PartState best = null;
        float bestGap = float.MaxValue;

        for (int i = 0; i < states.Count; i++)
        {
            PartState other = states[i];

            if (other == page) continue;

            float gap = (AngleAt(other, other.flip) - angle) * direction;

            if (gap <= 0.0f) continue;
            if (gap >= bestGap) continue;

            best = other;
            bestGap = gap;
        }

        return best;
    }


    private float RoomTo(float angle, PartState other)
    {
        if (other == null) return float.MaxValue;

        float room = Mathf.Abs(AngleAt(other, other.flip) - angle) - flipClearance;

        return other.config.loosePage ? room * 0.5f : room;
    }


    private void StartFlip(PartState page)
    {
        float target;
        if (!TryPlanFlip(page, out target)) return;

        page.flipping = true;
        page.flipTarget = target;
        lastFlipped = page;

        float travel = Mathf.Abs(AngleAt(page, target) - EffectiveAngle(page));
        float scale = Mathf.Clamp(travel / FLIP_REFERENCE_TRAVEL, MIN_FLIP_TURN_SCALE, 1.0f);

        KillTween(ref page.flipTween);

        page.flipTween = DOTween.To(() => page.flip, value => page.flip = value, target, FLIP_TURN_DURATION * scale)
            .SetEase(Ease.InOutQuad)
            .OnComplete(() => page.flipping = false);
    }


    private void CancelFlips()
    {
        for (int i = 0; i < loosePages.Count; i++)
        {
            PartState page = loosePages[i];

            KillTween(ref page.flipTween);

            // a book that closes mid turn finishes the sheet where it was heading
            if (page.flipping) page.flip = page.flipTarget;

            page.flipping = false;
        }
    }


    // MARK: yaw


    private void UpdateYaw()
    {
        if (yawTarget == null)
        {
            WarnOnce(ref warnedAboutYawTarget, "no yawTarget assigned, so the book will not turn to face the camera");
            return;
        }

        if (viewCamera == null) return;

        // the horizontal direction from the book to the camera
        Vector3 flat = viewCamera.transform.position - yawTarget.position;
        flat.y = 0.0f;

        // what if the camera is directly overhead
        if (flat.sqrMagnitude < 0.000001f) return;

        // the yaw the book always carries: standing upright, spine down, turned to face the camera
        Quaternion look = Quaternion.LookRotation(flat, Vector3.up);
        Quaternion rest = look * Quaternion.Euler(0.0f, SPINE_BASE_YAW + yawOffset, 0.0f);

        // Reading lays the book back on its spine, and the spine is what aims: it turns to face along
        // the view, so it ends up pointing away from the camera. From the rest pose it points straight
        // down, so the tilt is the shortest rotation from that to the aim. At read 0 this is the rest
        // pose exactly, which is what puts it back on the stump.
        Quaternion tip = Quaternion.FromToRotation(rest * spineFacing, viewCamera.transform.forward);

        yawTarget.rotation = Quaternion.Slerp(rest, tip * rest, read);
    }


    // MARK: hover


    // The real pointer on the book, without any external hover
    public bool IsPointerHovering { get { return pointerHovering; } }


    // Lets something other than the pointer hover the book. Every source that sets it must clear it.
    public void SetExternalHover(object source, bool hovering)
    {
        if (source == null) return;

        if (hovering) externalHover.Add(source);
        else externalHover.Remove(source);
    }


    private static bool IsDestroyedObject(object source)
    {
        // Unity-null: the C# object is still here but the engine object behind it is gone
        UnityEngine.Object unityObject = source as UnityEngine.Object;

        return source == null || (unityObject is not null && unityObject == null);
    }


    private bool IsHovered()
    {
        if (viewCamera == null) return false;

        if (hoverTarget == null)
        {
            WarnOnce(ref warnedAboutHoverTarget, "no hoverTarget assigned, so the book will never open");
            return false;
        }

        if (states.Count == 0)
        {
            WarnOnce(ref warnedAboutParts, "no parts assigned, so the book has nothing to animate");
            return false;
        }

        InputManager input = InputManager.Instance;
        if (input == null || !input.HasPointerPosition) return false;

        Vector2 screenPosition = input.PointerPosition;

        // The UI owns a pointer that lands on it, so a menu in front of the book cannot open it through
        if (PointerRaycastManager.IsPointerOverUI(screenPosition)) return false;

        // triggers included, so the volume can be switched to a trigger without changing this.
        // Water is left out: the sea's trigger box spans the whole world, so a book standing on
        // ground below the sea level would otherwise have its hover caught by the water
        if (hoverMask == 0) hoverMask = ~LayerMask.GetMask("Water");

        if (!PointerRaycastManager.TryGetHit(screenPosition, hoverMask, hoverRayDistance, QueryTriggerInteraction.Collide, out RaycastHit hit)) return false;

        return hit.collider == hoverTarget;
    }


    // MARK: internals


    private void BuildStates()
    {
        states.Clear();
        loosePages.Clear();

        if (parts == null) return;

        float widest = 0.0f;

        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = parts[i].part;

            if (part == null)
            {
                Debug.LogError($"BookAnimator: part {i} is empty", this);
                continue;
            }

            PartState state = new PartState
            {
                config = parts[i],
                restPosition = part.localPosition,
                restRotation = part.localRotation,
                driftPhase = UnityEngine.Random.Range(0.0f, Mathf.PI * 2.0f)
            };

            states.Add(state);

            widest = Mathf.Max(widest, Mathf.Abs(parts[i].openAngle));

            if (parts[i].loosePage) loosePages.Add(state);
        }

        // read takes the widest part to readOpenAngle, and everything else follows it round
        readScale = widest > 0.0001f ? readOpenAngle / widest : 1.0f;
    }


    private static float ScaleOf(BookPart config)
    {
        return config.openDurationScale > 0.0f ? config.openDurationScale : 1.0f;
    }


    private static void KillTween(ref Tween tween)
    {
        if (tween != null && tween.IsActive()) tween.Kill();

        tween = null;
    }


    private void WarnOnce(ref bool warned, string message)
    {
        if (warned) return;

        warned = true;
        Debug.LogWarning($"BookAnimator: {message}", this);
    }
}
