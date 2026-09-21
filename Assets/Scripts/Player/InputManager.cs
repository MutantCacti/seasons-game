using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;


[RequireComponent(typeof(PlayerInput))]


public class InputManager : MonoBehaviour
{
    private static InputManager instance;
    public static InputManager Instance{ get { return instance; }}


    private PlayerInput playerInput;


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


    public void DisableInput()
    {
        foreach (Button button in TitleScreenManager.Instance.Buttons) {
            button.enabled = false;
        }
        playerInput.enabled = false;
    }


    public void EnableInput()
    {
        playerInput.enabled = true;
    }


    public event Action<bool> onLeftClickDown;
    public void OnLeftClickDown(InputAction.CallbackContext ctx)
    {
        onLeftClickDown?.Invoke(ctx.action.ReadValue<float>() == 1);
    }


    public event Action<bool> onRightClickDown;
    public void OnRightClickDown(InputAction.CallbackContext ctx)
    {
        onRightClickDown?.Invoke(ctx.action.ReadValue<float>() == 1);
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
}