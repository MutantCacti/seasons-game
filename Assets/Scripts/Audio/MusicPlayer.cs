using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;


public class MusicPlayer : MonoBehaviour
{
    [SerializeField] private AudioMixerGroup audioMixerGroup;
    [SerializeField] private int musicPriority = 64;


    // index synched --- audioSources[n] is playing soundObjects[n]
    private int numVoices = 0;
    private List<AudioSource> audioSources = new List<AudioSource>();
    private List<SoundObject> soundObjects = new List<SoundObject>();


    public void Play(SoundObject soundObject)
    {
        if (soundObject == null) return;

        AudioSource audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = soundObject.PickRandomClip();
        audioSource.outputAudioMixerGroup = audioMixerGroup;
        audioSource.playOnAwake = false;
        audioSource.priority = musicPriority;
        audioSource.spatialize = false;

        audioSource.volume = soundObject.Volume;
        audioSource.pitch = soundObject.Pitch;

        // Register the new voice in the player
        if (audioSources.Count == numVoices)
        {
            audioSources.Add(null);
            soundObjects.Add(null);
        }
        audioSources[numVoices] = audioSource;
        soundObjects[numVoices] = soundObject;

        audioSource.Play();
        StartCoroutine(DestroyOnClipEnd(numVoices));
        numVoices++;
    }


    private IEnumerator DestroyOnClipEnd(int index)
    {
        yield return new WaitUntil(() => !audioSources[index].isPlaying);
        Destroy(audioSources[index]);
        SwapRemoveIndex(index);
    }


    public void Stop(SoundObject soundObject)
    {
        int index = soundObjects.IndexOf(soundObject);
        if (index == -1) return;

        // Destroy the AudioSource
        audioSources[index].Stop();
        Destroy(audioSources[index]);

        SwapRemoveIndex(index);
    }


    public void StopAll()
    {
        for (int i = 0; i < numVoices; i++)
        {
            audioSources[i].Stop();
            Destroy(audioSources[i]);
        }
        audioSources.Clear();
        soundObjects.Clear();
    }


    private void SwapRemoveIndex(int index)
    {
        // Swap-remove from the lists
        numVoices--;
        audioSources[index] = audioSources[numVoices];
        audioSources.RemoveAt(numVoices);
        soundObjects[index] = soundObjects[numVoices];
        soundObjects.RemoveAt(numVoices);
    }
}