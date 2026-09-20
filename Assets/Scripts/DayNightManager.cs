using System;
using UnityEngine;

public class DayNightManager : MonoBehaviour {

    [SerializeField] GameObject sunGO;
    [SerializeField] Vector3 sunRiseRotation = new Vector3(40f, -115f, 0f);
    [SerializeField] Vector3 sunSetRotation = new Vector3(40f, 30f, 0f);
    [SerializeField] float noonIntensity = 1f;
    [SerializeField] float nightIntensity = 0f;

    private Light sunLight;
    private Transform sunTransform;

    [Tooltip("Duration in seconds")]
    [SerializeField] float dayDuration = 10f;
    [SerializeField] float nightDuration = 5f;

    private float dayLength = 0f;
    private float currentTime = 0f;

    private void Awake() {
        sunLight = sunGO.GetComponent<Light>();
        sunTransform = sunGO.transform;

        dayLength = dayDuration + nightDuration;

        sunLight.intensity = nightIntensity;
        sunTransform.localEulerAngles = sunRiseRotation;
    }

    private void FixedUpdate() {
        
        currentTime += Time.deltaTime;

        if (currentTime >= dayLength) {
            currentTime -= dayLength;
            sunTransform.localEulerAngles = sunRiseRotation;
            Debug.LogWarning("reset");
        }


        //sine

        if (currentTime < dayDuration) {

            float dayProgress = currentTime / dayDuration;

            sunTransform.localEulerAngles = Vector3.Lerp(sunRiseRotation, sunSetRotation, dayProgress);

            if (currentTime < dayDuration / 2) {
                float intensityProgress = currentTime / (dayDuration / 2);
                sunLight.intensity = Mathf.Lerp(nightIntensity, noonIntensity, intensityProgress);
            } else {
                float intensityProgress = (currentTime / 2) / (dayDuration / 2);
                sunLight.intensity = Mathf.Lerp(noonIntensity, nightIntensity, intensityProgress);
            }


            Debug.Log("day");

        } else {
            sunLight.intensity = nightIntensity;
            Debug.Log("night");
        }
        
    }

    
}
