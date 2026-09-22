using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;


public class BookAnimator : MonoBehaviour
{
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

        public Tween openTween;
        public Tween flipTween;
        public bool flipping;
    }


    [SerializeField] private BookPart[] parts;

    // the Collider child of this prefab
    [SerializeField] private Collider hoverTarget;

    [SerializeField] private Camera viewCamera;
    [SerializeField] private float hoverRayDistance = 1000.0f;

    [SerializeField] private Transform yawTarget;   // the book model

    // Degrees of fine tuning on top of the spine base aim below
    [SerializeField] private float yawOffset;


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


    private const float SPINE_BASE_YAW = -90.0f;

    private const float FLIP_OPEN_THRESHOLD = 0.9f;

    private const float FLIP_TURN_DURATION = 0.4f;

    private const float FLIP_REFERENCE_TRAVEL = 40.0f;

    private const float MIN_FLIP_TRAVEL = 5.0f;

    private const float MIN_FLIP_TURN_SCALE = 0.6f;


    private readonly List<PartState> states = new List<PartState>();
    private readonly List<PartState> loosePages = new List<PartState>();
    private readonly List<PartState> flipCandidates = new List<PartState>();

    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
    private PointerEventData uiRaycastPointer;
    private EventSystem uiRaycastSystem;

    private float openAmount;
    private Tween openTween;
    private Tween bobTween;
    private float bobRestY;

    private PartState lastFlipped;
    private float nextFlipTime;
    private bool wasHovered;
    private bool warnedAboutParts;
    private bool warnedAboutHoverTarget;
    private bool warnedAboutYawTarget;


    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;

        BuildStates();
        StartBob();
        ApplyAll();
    }


    private void OnDestroy()
    {
        KillTween(ref openTween);
        KillTween(ref bobTween);

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
        if (Time.timeScale <= 0.0f) return;

        UpdateHover();
        UpdateFlips();
    }


    // LateUpdate so the camera has already settled for this frame
    private void LateUpdate()
    {
        UpdateYaw();
        ApplyAll();
    }


    // MARK: open


    private void UpdateHover()
    {
        bool hovered = IsHovered();

        if (hovered == wasHovered) return;

        wasHovered = hovered;
        TweenTo(hovered ? 1.0f : 0.0f);
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
        for (int i = 0; i < states.Count; i++) Apply(states[i]);
    }


    private void Apply(PartState state)
    {
        Transform part = state.config.part;

        float angle = AngleAt(state, state.flip) * state.open;

        float lift = state.config.liftOnOpen * state.open;

        if (state.config.loosePage && driftDegrees > 0.0f)
        {
            // scaled, so the idle sway holds still with the rest of the book while the game is paused
            float sway = Mathf.Sin(Time.time * driftSpeed + state.driftPhase) * openAmount;
            float swing = Mathf.Min(driftDegrees, DriftRoom(state));

            angle += sway * swing;
            lift += sway * driftLift * (swing / driftDegrees);
        }

        part.localRotation = state.restRotation * Quaternion.AngleAxis(angle, Vector3.right);
        part.localPosition = state.restPosition + Vector3.up * lift;
    }


    // MARK: float


    private void StartBob()
    {
        if (yawTarget == null) return;

        bobRestY = yawTarget.localPosition.y;

        bobTween = yawTarget.DOLocalMoveY(bobRestY + bobAmplitude, bobPeriod)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }


    // MARK: loose pages


    private void UpdateFlips()
    {
        if (loosePages.Count == 0) return;

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
        Vector3 toCamera = viewCamera.transform.position - yawTarget.position;
        toCamera.y = 0.0f;

        // what if the camera is directly overhead
        if (toCamera.sqrMagnitude < 0.000001f) return;

        Quaternion aim = Quaternion.LookRotation(toCamera, Vector3.up);

        yawTarget.rotation = aim * Quaternion.Euler(0.0f, SPINE_BASE_YAW + yawOffset, 0.0f);
    }


    // MARK: hover


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
        if (IsPointerOverUI(screenPosition)) return false;

        Ray ray = viewCamera.ScreenPointToRay(screenPosition);

        // triggers included, so the volume can be switched to a trigger without changing this
        if (!Physics.Raycast(ray, out RaycastHit hit, hoverRayDistance, ~0, QueryTriggerInteraction.Collide)) return false;

        return hit.collider == hoverTarget;
    }


    // MARK: UI hit test


    // Live UI hit test at a screen position
    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        // rebuilt only when the EventSystem changes, which means a scene load
        if (uiRaycastPointer == null || uiRaycastSystem != eventSystem)
        {
            uiRaycastSystem = eventSystem;
            uiRaycastPointer = new PointerEventData(eventSystem);
        }

        uiRaycastPointer.position = screenPosition;

        // RaycastAll clears the results list itself
        eventSystem.RaycastAll(uiRaycastPointer, uiRaycastResults);

        return uiRaycastResults.Count > 0;
    }


    // MARK: internals


    private void BuildStates()
    {
        states.Clear();
        loosePages.Clear();

        if (parts == null) return;

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

            if (parts[i].loosePage) loosePages.Add(state);
        }
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
