using System;
using DG.Tweening;
using UnityEngine;

public class DayNightManager : MonoBehaviour {

    [SerializeField] GameObject sunGO;
    [SerializeField] Vector3 sunRiseRotation = new Vector3(-2f, -135f, 0f);
    [SerializeField] float sunNoonAngle = 60f;
    private Vector3 sunNoonRotation;
    [SerializeField] Vector3 sunSetRotation = new Vector3(-2f, 55f, 0f);
    [SerializeField] float noonIntensity = 1f;
    [SerializeField] float nightIntensity = 0f;


    [Tooltip("Duration in seconds")]
    [SerializeField] float dayDuration = 10f;
    [SerializeField] float nightDuration = 2f;


    private Light sunLight;
    private Transform sunTransform;


    private void Awake() {
        sunNoonRotation = new Vector3(sunNoonAngle, (sunRiseRotation.y + sunSetRotation.y) * 0.5f, 0f);

        sunLight = sunGO.GetComponent<Light>();
        sunTransform = sunGO.transform;

        sunLight.intensity = nightIntensity;
        sunTransform.localEulerAngles = sunRiseRotation;
    }


    private void Start() {
        SunRise();
    }


    private void SunRise() {
        sunTransform.DOLocalRotate(sunNoonRotation, dayDuration / 2f).SetEase(Ease.OutSine).OnComplete(SunSet);
        sunLight.DOIntensity(noonIntensity, dayDuration / 2f).SetEase(Ease.OutSine);
    }


    private void SunSet() {
        sunTransform.DOLocalRotate(sunSetRotation, dayDuration / 2f).SetEase(Ease.InSine).OnComplete(Night);
        sunLight.DOIntensity(nightIntensity, dayDuration / 2f).SetEase(Ease.InSine);
    }


    private void Night() {
        sunTransform.DOLocalRotate(sunRiseRotation, nightDuration).OnComplete(SunRise);
    }
}
