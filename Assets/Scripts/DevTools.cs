using UnityEngine;

public class DevTools : MonoBehaviour {

    private void Start() {
        InputManager.Instance.onSpeedUp += SpeedUp;
        InputManager.Instance.onSpeedDown += SpeedDown;
    }

    private void OnDisable() {
        InputManager.Instance.onSpeedUp -= SpeedUp;
        InputManager.Instance.onSpeedDown -= SpeedDown;
    }


    private void SpeedUp() {
        Time.timeScale = 10f;
    }
    
    private void SpeedDown() {
        Time.timeScale = 1f;
    }

}
