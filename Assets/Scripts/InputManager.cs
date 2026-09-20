using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;


[RequireComponent(typeof(PlayerInput))]


public class InputManager : MonoBehaviour
{
    private static InputManager _instance;
    public static InputManager Instance{ get { return _instance; }}


    private PlayerInput _playerInput;


    void Awake() {_instance = this;}


    void Start()
    {
        _playerInput = GetComponent<PlayerInput>();
    }


    public void DisableInput()
    {
        foreach (Button button in TitleScreenManager.instance.buttons) {
            button.enabled = false;
        }
        _playerInput.enabled = false;
    }


    public void EnableInput()
    {
        _playerInput.enabled = true;
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


    public event Action<Vector2> onMousePosition;
    public void OnMousePosition(InputAction.CallbackContext ctx)
    {
        onMousePosition?.Invoke(ctx.action.ReadValue<Vector2>());
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