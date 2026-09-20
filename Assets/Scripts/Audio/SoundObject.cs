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
    // pitch is a playback rate multiplier, not a musical interval:
    // 1 is normal speed, 2 is twice as fast (an octave up), 0.5 is half speed (an octave down).
    // 0 freezes the clip and negatives play it backwards, so the range stays positive.
    [Range(0.5f, 2f)] [SerializeField] private float pitch = 1f;
    // random detune either side of pitch, in semitones. 1-3 is the useful range for most SFX;
    // the cap of 6 is a tritone either way
    [Range(0f, 6f)] [SerializeField] private float pitchVariationSemitones;


    [System.NonSerialized] private int lastClipIndex = -1;  // used to avoid repeating the last clip in random order

    // AudioSource.pitch is clamped by Unity to [-3..3], and the AudioRandomContainer path
    // treats anything below 0.0001 as a pause. Stay well inside both so a randomised pitch
    // can never come out silent or reversed.
    private const float MIN_PITCH = 0.25f;
    private const float MAX_PITCH = 3.0f;


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
        // Variation is applied as a musical interval rather than an absolute offset, so the
        // same setting sounds the same amount detuned whatever the base pitch is: an octave
        // is a doubling, so one semitone is a factor of 2^(1/12).
        float semitones = Random.Range(-pitchVariationSemitones, pitchVariationSemitones);
        return Mathf.Clamp(pitch * Mathf.Pow(2f, semitones / 12f), MIN_PITCH, MAX_PITCH);
    }
}