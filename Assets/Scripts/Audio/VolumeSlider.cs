using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


// A volume row in the options menu. It reads its starting value from GlobalOptionsManager and
// writes changes back there; the manager is the only thing that touches the AudioMixer.
public class VolumeSlider : MonoBehaviour, IPointerUpHandler
{
    [SerializeField] private string audioMixerGroupName;


    private Slider slider;
    private bool loggedMissingManager;


    private void Awake()
    {
        slider = GetComponent<Slider>();
    }


    private void Start()
    {
        if (slider == null)
        {
            Debug.LogError("VolumeSlider: no Slider on the same GameObject, so the value cannot be initialised");
            return;
        }

        GlobalOptionsManager manager = TryGetManager();
        if (manager == null) return;

        // SetValueWithoutNotify so initialising does not fire onValueChanged and write back
        slider.SetValueWithoutNotify(manager.GetValue(audioMixerGroupName));
    }


    public void SetVolume(float sliderValue)
    {
        GlobalOptionsManager manager = TryGetManager();
        if (manager == null) return;

        manager.SetValue(audioMixerGroupName, sliderValue);
    }


    public void OnPointerUp(PointerEventData eventData)
    {
        GlobalOptionsManager manager = TryGetManager();
        if (manager == null) return;

        manager.Save();
    }


    private void OnDisable()
    {
        // may run before Start if the options menu starts active and is switched off in Awake;
        // Save is a no-op while nothing has changed, and the manager may still be null here
        GlobalOptionsManager manager = TryGetManager();
        if (manager == null) return;

        manager.Save();
    }


    private GlobalOptionsManager TryGetManager()
    {
        GlobalOptionsManager manager = GlobalOptionsManager.Instance;

        if (manager == null && !loggedMissingManager)
        {
            loggedMissingManager = true;
            Debug.LogError("VolumeSlider: no GlobalOptionsManager in the scene, so the slider keeps its value and writes nothing back");
        }

        return manager;
    }
}
