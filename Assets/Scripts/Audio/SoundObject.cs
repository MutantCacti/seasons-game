using UnityEngine;
using UnityEngine.Audio;


[CreateAssetMenu(
    fileName="SoundObject",
    menuName="ScriptableObjects/SoundObject"
)]
public class SoundObject : ScriptableObject
{
    [Header("Clips")]
    [SerializeField] private AudioClip[] clips;


    [Header("Playback")]
    [SerializeField] private bool loop;
    [Range(0f, 1f)] [SerializeField] private float volume = 1f;
    [Range(0f, 1f)] [SerializeField] private float volumeVariation;
    [Range(-3f, 3f)] [SerializeField] private float pitch = 1f;
    [Range(0f, 1f)] [SerializeField] private float pitchVariation;


    [System.NonSerialized] private int lastClipIndex = -1;  // used to avoid repeating the last clip in random order


    public bool Loop => loop;
    public float Volume => volume;
    public float Pitch => pitch;


    public AudioClip PickRandomClip()
    {
        if (clips == null || clips.Length == 0) return null;
        if (clips.Length == 1) return clips[0];

        int index = Random.Range(0, clips.Length);
        while (index == lastClipIndex) index = Random.Range(0, clips.Length);

        lastClipIndex = index;
        return clips[index];
    }


    public float PickRandomVolume()
    {
        return Mathf.Clamp(volume + Random.Range(-volumeVariation, volumeVariation), 0f, 1f);
    }


    public float PickRandomPitch()
    {
        return Mathf.Clamp(pitch + Random.Range(-pitchVariation, pitchVariation), -3f, 3f);
    }
}