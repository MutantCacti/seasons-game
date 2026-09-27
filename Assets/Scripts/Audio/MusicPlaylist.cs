using UnityEngine;
using UnityEngine.SceneManagement;


// The in-game soundtrack. Lives next to AudioManager in the Persistent Scene, so a checkpoint reset
// (which reloads the Game scene) never touches it: the current song, or the gap, just carries on.
// Every round plays each song once in a shuffled order, then reshuffles.
public class MusicPlaylist : MonoBehaviour
{
    private static MusicPlaylist instance;
    public static MusicPlaylist Instance { get { return instance; } }


    private const string GAME_SCENE = "Game";
    private const string TITLE_SCENE = "Title Screen";

    // one frame after the window comes back carries all the time spent away, so a gap would be
    // burned in one go without this cap
    private const float MAX_TIMER_STEP = 0.1f;


    [Header("Songs")]
    // loop off on every one of these, a looping song never ends so the playlist would stick on it
    [SerializeField] private SoundObject[] songs;


    [Header("Timing (seconds, unscaled)")]
    // lets the loading screen slide away before the first song
    [SerializeField] private float startDelay = 1f;
    [SerializeField] private float pauseBetweenSongs = 4f;
    // the gap is pauseBetweenSongs plus or minus up to this much
    [SerializeField] private float pauseVariation = 1f;
    // quitting to title fades the song out behind the loading screen, which takes about this long
    [SerializeField] private float titleFadeOut = 0.5f;


    private enum State { Idle, Waiting, Playing }
    private State state = State.Idle;

    private float waitRemaining;
    private AudioSource currentVoice;

    // the shuffle bag: song indices in play order, and where we are in it
    private int[] bag;
    private int bagPosition;
    private int lastPlayed = -1;


    // MARK: lifecycle


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Found more than one MusicPlaylist in the scene - destroying newest !!");
            Destroy(gameObject);
            return;
        }

        instance = this;
    }


    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }


    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }


    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }


    private void Start()
    {
        // pressing Play with the Game scene already open may load it before we subscribe
        if (SceneManager.GetActiveScene().name == GAME_SCENE) Begin();
    }


    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == GAME_SCENE) Begin();
        else if (scene.name == TITLE_SCENE) StopAndReset();
    }


    // MARK: control


    // a checkpoint reload lands here too, and a running playlist just keeps going
    private void Begin()
    {
        if (state != State.Idle) return;

        if (songs == null || songs.Length == 0)
        {
            Debug.LogError("MusicPlaylist: no songs assigned, so the game has no music");
            return;
        }

        state = State.Waiting;
        waitRemaining = startDelay;
    }


    // Quitting to title. Goes idle BEFORE fading, so the fading voice being destroyed isn't read as
    // the song ending, and forgets the bag so the next game starts a fresh round.
    public void StopAndReset()
    {
        state = State.Idle;
        currentVoice = null;
        bag = null;
        lastPlayed = -1;

        if (AudioManager.Instance != null) AudioManager.Instance.FadeOutAllMusic(titleFadeOut);
    }


    // MARK: update


    private void Update()
    {
        if (state == State.Idle) return;

        // losing focus pauses the audio, so neither the song nor the gap should move on meanwhile.
        // the song's end is already focus-safe: MusicPlayer only destroys finished voices with focus
        if (!Application.isFocused) return;

        if (state == State.Playing)
        {
            if (currentVoice != null) return;

            state = State.Waiting;
            waitRemaining = Mathf.Max(0f, pauseBetweenSongs + Random.Range(-pauseVariation, pauseVariation));
            return;
        }

        waitRemaining -= Mathf.Min(Time.unscaledDeltaTime, MAX_TIMER_STEP);
        if (waitRemaining > 0f) return;

        PlayNext();
    }


    private void PlayNext()
    {
        int index = NextFromBag();
        SoundObject song = songs[index];
        lastPlayed = index;

        if (song != null && song.Loop)
        {
            Debug.LogError($"MusicPlaylist: '{song.name}' has Loop on, so the playlist never gets past it");
        }

        currentVoice = AudioManager.Instance != null ? AudioManager.Instance.PlayMusic(song) : null;

        // an empty slot or a song with no clips is skipped with a gap instead of a silent stall
        state = State.Playing;
    }


    // MARK: shuffle bag


    private int NextFromBag()
    {
        // the array can be edited in play mode, so a size change starts a new round
        if (bag == null || bag.Length != songs.Length || bagPosition >= bag.Length) Reshuffle();

        return bag[bagPosition++];
    }


    private void Reshuffle()
    {
        int count = songs.Length;
        if (bag == null || bag.Length != count) bag = new int[count];

        for (int i = 0; i < count; i++) bag[i] = i;

        // fisher-yates
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        // don't open the round with the song that closed the last one
        if (count > 1 && bag[0] == lastPlayed)
        {
            int j = Random.Range(1, count);
            (bag[0], bag[j]) = (bag[j], bag[0]);
        }

        bagPosition = 0;
    }
}
