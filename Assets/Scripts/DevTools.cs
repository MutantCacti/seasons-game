using UnityEngine;


public class DevTools : MonoBehaviour
{
    [SerializeField] private float spedUpTimeScale = 10f;
    [SerializeField] private float normalTimeScale = 1f;


    private void Start() {
        InputManager.Instance.onSpeedUp += SpeedUp;
        InputManager.Instance.onSpeedDown += SpeedDown;
    }


    private void OnDisable() {
        InputManager.Instance.onSpeedUp -= SpeedUp;
        InputManager.Instance.onSpeedDown -= SpeedDown;
    }


    private void SpeedUp() {
        Time.timeScale = spedUpTimeScale;
    }


    private void SpeedDown() {
        Time.timeScale = normalTimeScale;
    }
}
