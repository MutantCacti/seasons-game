using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Audio;


public class MusicPlayer : MonoBehaviour
{
    [SerializeField] private AudioMixerGroup audioMixerGroup;
    [SerializeField] private int musicPriority = 0;


    // index synched --- audioSources[n] is playing soundObjects[n]
    private List<AudioSource> audioSources = new List<AudioSource>();
    private List<SoundObject> soundObjects = new List<SoundObject>();

    // how long after regaining focus finished voices are left alone, see Update
    private const float FOCUS_GRACE_SECONDS = 0.5f;
    private float cleanupResumesAt;


    // returns the voice so a caller can tell when it's over: the voice is destroyed when its clip
    // ends (see Update) or it's stopped, and a destroyed AudioSource compares equal to null
    public AudioSource Play(SoundObject soundObject)
    {
        if (soundObject == null) return null;

        AudioClip clip = soundObject.PickRandomClip();
        if (clip == null)
        {
            Debug.LogWarning($"MusicPlayer: SoundObject '{soundObject.name}' has no clips assigned - ignoring Play !!");
            return null;
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
        return audioSource;
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


    // fades every voice to silence and then removes it. unscaled, because the pause menu sets
    // timeScale 0 and quitting to title happens from under it
    public void FadeOutAll(float duration)
    {
        for (int i = 0; i < audioSources.Count; i++)
        {
            AudioSource audioSource = audioSources[i];

            // a second fade restarts from wherever the first one got to
            audioSource.DOKill();
            audioSource.DOFade(0f, duration)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() => RemoveVoice(audioSource));
        }
    }


    public void Update()
    {
        // losing focus pauses all audio (runInBackground is off), and a paused voice reports
        // isPlaying false, so a voice is only treated as finished while the window has focus and
        // has had it for a moment: the audio system can resume a frame or two after Update does
        if (!Application.isFocused || Time.realtimeSinceStartup < cleanupResumesAt) return;

        // destroy voices when their clip is over
        for (int i = audioSources.Count - 1; i >= 0; i--)
        {
            // a looping voice never finishes by itself, it only ends through Stop
            if (audioSources[i].isPlaying || audioSources[i].loop) continue;

            RemoveVoice(i);
        }
    }


    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) cleanupResumesAt = Time.realtimeSinceStartup + FOCUS_GRACE_SECONDS;
    }


    private void OnApplicationPause(bool paused)
    {
        if (!paused) cleanupResumesAt = Time.realtimeSinceStartup + FOCUS_GRACE_SECONDS;
    }


    private void RemoveVoice(AudioSource audioSource)
    {
        // looked up again because other voices may have been removed while this one faded
        int index = audioSources.IndexOf(audioSource);
        if (index == -1) return;

        RemoveVoice(index);
    }


    private void RemoveVoice(int index)
    {
        // stop and destroy the AudioSource, then drop it from both lists. a voice can end or be
        // stopped mid-fade, so its tween goes with it
        audioSources[index].DOKill();
        audioSources[index].Stop();
        Destroy(audioSources[index]);

        audioSources.RemoveAt(index);
        soundObjects.RemoveAt(index);
    }
}
