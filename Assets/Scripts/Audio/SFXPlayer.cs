using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;


public class SFXPlayer : MonoBehaviour
{
    [SerializeField] private AudioMixerGroup audioMixerGroup;
    [SerializeField] private int sfxPriority = 128;
    [SerializeField] private int numVoices = 16;


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

            // spatialBlend, minDistance and maxDistance belong to the SoundObject instead, because one
            // voice ends up playing a footstep and thunder and they want very different distances
            newAudioSource.dopplerLevel = 0f;
            newAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;

            voiceGOs.Add(newVoiceGO);
            audioSources.Add(newAudioSource);
        }
    }


    public void Play(SoundObject soundObject, Vector3 worldPos)
    {
        Play(soundObject, worldPos, 1f);
    }


    // pitchScale multiplies the SoundObject's own (randomised) pitch, for callers that step a pitch
    // up through a sequence
    public void Play(SoundObject soundObject, Vector3 worldPos, float pitchScale)
    {
        Play(soundObject, worldPos, pitchScale, 1f);
    }


    // volumeScale multiplies the SoundObject's own (randomised) volume, for callers that get louder
    // or quieter with what they're doing. the result is still capped at 1, so a SoundObject that
    // wants to be boosted needs its volume set below 1 to leave headroom
    public void Play(SoundObject soundObject, Vector3 worldPos, float pitchScale, float volumeScale)
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
        audioSource.volume = Mathf.Clamp01(soundObject.PickRandomVolume() * volumeScale);
        // same limits SoundObject keeps its own pitch inside
        audioSource.pitch = Mathf.Clamp(soundObject.PickRandomPitch() * pitchScale, 0.25f, 3f);

        // set per play rather than once per voice, since a voice is reused for any sound
        audioSource.spatialBlend = soundObject.SpatialBlend;
        audioSource.minDistance = soundObject.MinDistance;
        audioSource.maxDistance = soundObject.MaxDistance;

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