using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;


public class SFXPlayer : MonoBehaviour
{
    [SerializeField] private AudioMixerGroup audioMixerGroup;
    [SerializeField] private int sfxPriority = 128;
    [SerializeField] private int numVoices = 16;
    [SerializeField] private float maxDistance = 30.0f;


    private List<GameObject> voiceGOs = new List<GameObject>();
    private List<AudioSource> audioSources = new List<AudioSource>();


    public void Awake()
    {
        for (int i = 0; i < numVoices; i++)
        {
            // every voice is a game object with an audio source
            GameObject newVoiceGO = new GameObject($"SFX Voice {i}");
            newVoiceGO.transform.SetParent(transform, false);

            AudioSource newAudioSource = newVoiceGO.AddComponent<AudioSource>();

            newAudioSource.playOnAwake = false;
            newAudioSource.outputAudioMixerGroup = audioMixerGroup;
            newAudioSource.priority = sfxPriority;
            newAudioSource.loop = false;

            newAudioSource.spatialBlend = 1f;
            newAudioSource.dopplerLevel = 0f;
            newAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;

            // this might need hoisting to a SoundObject field if we get
            // sounds with varying distances (e.g. thunder versus footstep)
            newAudioSource.minDistance = 1f;
            newAudioSource.maxDistance = maxDistance;

            voiceGOs.Add(newVoiceGO);
            audioSources.Add(newAudioSource);
        }
    }


    public void Play(SoundObject soundObject, Vector3 worldPos)
    {
        if (soundObject == null) return;

        AudioClip clip = soundObject.PickRandomClip();
        if (clip == null)
        {
            Debug.LogWarning($"SFXPlayer: SoundObject '{soundObject.name}' has no clips assigned - ignoring Play !!");
            return;
        }

        AudioSource audioSource = null;
        for (int i = 0; i < audioSources.Count; i++)
        {
            if (!audioSources[i].isPlaying)
            {
                audioSource = audioSources[i];
                break;
            }
        }

        if (audioSource == null)
        {
            Debug.LogWarning($"SFXPlayer: SoundObject '{soundObject.name}' cannot be played because all {numVoices} SFXPlayer voices are already occupied");
            return;
        }

        audioSource.clip = clip;
        audioSource.volume = soundObject.PickRandomVolume();
        audioSource.pitch = soundObject.PickRandomPitch();

        audioSource.transform.position = worldPos;

        audioSource.Play();
    }


    public void StopAll()
    {
        for (int i = 0; i < audioSources.Count; i++)
        {
            audioSources[i].Stop();
        }
    }
}