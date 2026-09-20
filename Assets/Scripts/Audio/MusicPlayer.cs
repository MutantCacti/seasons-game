using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;


public class MusicPlayer : MonoBehaviour
{
    [SerializeField] private AudioMixerGroup audioMixerGroup;
    [SerializeField] private int musicPriority = 0;


    // index synched --- audioSources[n] is playing soundObjects[n]
    private List<AudioSource> audioSources = new List<AudioSource>();
    private List<SoundObject> soundObjects = new List<SoundObject>();


    public void Play(SoundObject soundObject)
    {
        if (soundObject == null) return;

        AudioClip clip = soundObject.PickRandomClip();
        if (clip == null)
        {
            Debug.LogWarning($"SoundObject '{soundObject.name}' has no clips assigned - ignoring Play !!");
            return;
        }

        AudioSource audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;  // music is always 2D; the listener moves with the camera
        audioSource.outputAudioMixerGroup = audioMixerGroup;
        audioSource.priority = musicPriority;

        audioSource.clip = clip;
        audioSource.loop = soundObject.Loop;
        audioSource.volume = soundObject.Volume;
        audioSource.pitch = soundObject.Pitch;

        // register the new voice in the player
        audioSources.Add(audioSource);
        soundObjects.Add(soundObject);

        audioSource.Play();
    }


    public void Stop(SoundObject soundObject)
    {
        int index = soundObjects.IndexOf(soundObject);
        if (index == -1) return;

        RemoveVoice(index);
    }


    public void StopAll()
    {
        for (int i = audioSources.Count - 1; i >= 0; i--)
        {
            RemoveVoice(i);
        }
    }


    public void Update()
    {
        // destroy voices when their clip is over
        for (int i = audioSources.Count - 1; i >= 0; i--)
        {
            if (audioSources[i].isPlaying) continue;

            RemoveVoice(i);
        }
    }


    private void RemoveVoice(int index)
    {
        // stop and destroy the AudioSource, then drop it from both lists
        audioSources[index].Stop();
        Destroy(audioSources[index]);

        audioSources.RemoveAt(index);
        soundObjects.RemoveAt(index);
    }
}
