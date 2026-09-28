using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;


[RequireComponent(typeof(PlayerInput))]


public class InputManager : MonoBehaviour
{
    private static InputManager instance;
    public static InputManager Instance{ get { return instance; }}


    private PlayerInput playerInput;

    // the last frame a text field had focus; see IsTypingText
    private int typingFrame = -10;


    public void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one InputManager in the scene - destroying newest !!");
            Destroy(gameObject);
            return;
        }
        instance = this;
    }


    public void Start()
    {
        playerInput = GetComponent<PlayerInput>();
    }


    private void Update()
    {
        if (IsTextFieldFocused()) typingFrame = Time.frameCount;
    }


    // MARK: typing


    public static bool IsTypingText
    {
        get
        {
            if (IsTextFieldFocused()) return true;
            return instance != null && Time.frameCount - instance.typingFrame <= 1;
        }
    }


    private static bool IsTextFieldFocused()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected == null) return false;

        TMP_InputField field = selected.GetComponent<TMP_InputField>();
        return field != null && field.isFocused;
    }


    public void DisableInput()
    {
        playerInput.enabled = false;
    }


    public void EnableInput()
    {
        playerInput.enabled = true;
    }


    public event Action<bool> onLeftClickDown;
    public void OnLeftClickDown(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) onLeftClickDown?.Invoke(true);
        else if (ctx.canceled) onLeftClickDown?.Invoke(false);
    }


    public event Action<bool> onRightClickDown;
    public void OnRightClickDown(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) onRightClickDown?.Invoke(true);
        else if (ctx.canceled) onRightClickDown?.Invoke(false);
    }


    public event Action<Vector2> onMouseDelta;
    public void OnMouseDelta(InputAction.CallbackContext ctx)
    {
        onMouseDelta?.Invoke(ctx.action.ReadValue<Vector2>());
    }


    private Vector2 pointerPosition;
    private bool hasPointerPosition;

    // Cached from Mouse Position action so consumers can read the pointer without the event
    // HasPointerPosition stays false until that action has fired once
    public Vector2 PointerPosition { get { return pointerPosition; } }
    public bool HasPointerPosition { get { return hasPointerPosition; } }


    public event Action<Vector2> onMousePosition;
    public void OnMousePosition(InputAction.CallbackContext ctx)
    {
        pointerPosition = ctx.action.ReadValue<Vector2>();
        hasPointerPosition = true;
        onMousePosition?.Invoke(pointerPosition);
    }


    public event Action<float> onScrollWheel;
    public void OnScrollWheel(InputAction.CallbackContext ctx)
    {
        onScrollWheel?.Invoke(ctx.action.ReadValue<float>());
    }


    public event Action<Vector2> onMovement;
    public void OnMovement(InputAction.CallbackContext ctx)
    {
        onMovement?.Invoke(ctx.action.ReadValue<Vector2>());
    }


    public event Action onPause;
    public void OnPause(InputAction.CallbackContext ctx) {
        if (ctx.performed) {
            onPause?.Invoke();
        }
    }

    public event Action onSpeedUp;
    public void OnSpeedUp(InputAction.CallbackContext ctx) {
        if (ctx.performed) {
            onSpeedUp?.Invoke();
        }
    }

    public event Action onSpeedDown;
    public void OnSpeedDown(InputAction.CallbackContext ctx) {
        if (ctx.performed) {
            onSpeedDown?.Invoke();
        }
    }

    private bool modifierDown;
    public bool ModifierDown { get { return modifierDown; } }

    public event Action<bool> onModifier;
    public void OnModifier(InputAction.CallbackContext ctx)
    {
        if (ctx.performed)
        {
            modifierDown = true;
            onModifier?.Invoke(true);
        }
        else if (ctx.canceled)
        {
            modifierDown = false;
            onModifier?.Invoke(false);
        }
    }


    public event Action onGrantItems;
    public void OnGrantItems(InputAction.CallbackContext ctx)
    {
        if (ctx.performed && modifierDown) {
            onGrantItems?.Invoke();
        }
    }
}