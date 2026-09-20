using UnityEngine;


public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;
    public static AudioManager Instance{get {return instance; }}


    [SerializeField] private MusicPlayer musicPlayer;
    //[SerializeField] private SFXPlayer sfxPlayer;
    //[SerializeField] private UIPlayer uiPlayer;


    public void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one AudioManager in the scene - destroying newest !!");
            Destroy(gameObject);
            return;
        }
        instance = this;
    }


    public void PlayMusic(SoundObject musicObject)
    {
        musicPlayer.Play(musicObject);
    }


    public void PlaySFX(SoundObject sfxObject, Vector3 worldPos)
    {
        // TODO
        Debug.LogWarning("AudioManager.PlaySFX is not yet implemented");
        return;
    }


    public void PlayUI(SoundObject sfxObject)
    {
        // TODO
        Debug.LogWarning("AudioManager.PlayUI is not yet implemented");
        return;
    }
}