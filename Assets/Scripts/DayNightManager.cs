using System;
using UnityEngine;

public class DayNightManager : MonoBehaviour {

    [SerializeField] GameObject sunGO;
    private Vector3 sunRotation;
    private Light sunLight;

    [Tooltip("Duration in seconds")]
    [SerializeField] float dayDuration = 10f;
    [SerializeField] float nightDuration = 5f;

    private float dayLength = 0f;
    private float currentTime = 0f;

    private void Awake() {
        sunRotation = sunGO.transform.localEulerAngles;
        sunLight = sunGO.GetComponent<Light>();

        dayLength = dayDuration + nightDuration;
    }

    private void FixedUpdate() {
        currentTime += Time.deltaTime;
        if (currentTime >= dayLength) {
            currentTime -= dayLength;
        }

        if (currentTime < dayDuration) {
            Debug.Log("day");
        } else {
            Debug.Log("night");
        }
    }





    
}
