using System;
using UnityEngine;

public class DayNightManager : MonoBehaviour {

    [SerializeField] GameObject sunGO;
    [SerializeField] Vector3 sunRiseRotation = new Vector3(-2f, -115f, 0f);
    [SerializeField] float sunNoonAngle = 60f;
    private Vector3 sunNoonRotation;
    [SerializeField] Vector3 sunSetRotation = new Vector3(-2f, 45f, 0f);
    [SerializeField] float noonIntensity = 1f;
    [SerializeField] float nightIntensity = 0f;

    private Light sunLight;
    private Transform sunTransform;

    [Tooltip("Duration in seconds")]
    [SerializeField] float dayDuration = 10f;
    [SerializeField] float nightDuration = 2f;

    private float cycleLength = 0f;
    private float currentTime = 0f;

    private void Awake() {
        sunNoonRotation = new Vector3(sunNoonAngle, (sunRiseRotation.y + sunSetRotation.y) * 0.65f, 0f);

        sunLight = sunGO.GetComponent<Light>();
        sunTransform = sunGO.transform;

        cycleLength = dayDuration + nightDuration;

        sunLight.intensity = nightIntensity;
        sunTransform.localEulerAngles = sunRiseRotation;
    }

    private void FixedUpdate() {

        currentTime += Time.deltaTime;

        if (currentTime >= cycleLength) {
            currentTime -= cycleLength;
            sunTransform.localEulerAngles = sunRiseRotation;
            Debug.LogWarning("reset");
        }


        // TODO: ADD SINE EASING TO LERPs

        if (currentTime < dayDuration) {

            if (currentTime < dayDuration / 2) {

                float dayProgress = currentTime / (dayDuration / 2);

                sunTransform.localEulerAngles = Vector3.Lerp(sunRiseRotation, sunNoonRotation, dayProgress);

                sunLight.intensity = Mathf.Lerp(nightIntensity, noonIntensity, dayProgress);

            } else {

                float dayProgress = (currentTime - dayDuration / 2) / (dayDuration / 2);

                sunTransform.localEulerAngles = Vector3.Lerp(sunNoonRotation, sunSetRotation, dayProgress);

                sunLight.intensity = Mathf.Lerp(noonIntensity, nightIntensity, dayProgress);

            }


            Debug.Log("day");

        } else {
            sunLight.intensity = nightIntensity;
            Debug.Log("night");
        }
        
    }

    
}
