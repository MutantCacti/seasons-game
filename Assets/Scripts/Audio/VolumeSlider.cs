using UnityEngine;
using UnityEngine.Audio;

public class VolumeSlider : MonoBehaviour {

    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string audioMixerGroupName;


    public void SetVolume(float sliderValue) {
        audioMixer.SetFloat(audioMixerGroupName, Mathf.Log10(sliderValue) * 20 - 4);
    }

}
