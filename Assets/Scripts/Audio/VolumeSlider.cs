using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class VolumeSlider : MonoBehaviour {

    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string audioMixerGroupName;


    private void Start() {
        SetVolume(gameObject.GetComponent<Slider>().value);
    }


    public void SetVolume(float sliderValue) {
        audioMixer.SetFloat(audioMixerGroupName, Mathf.Log10(sliderValue) * 20 - 4);
    }

}
