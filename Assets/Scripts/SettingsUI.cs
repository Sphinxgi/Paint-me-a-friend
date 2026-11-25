
using UnityEngine;
using UnityEngine.UI;

public class SettingsUI : MonoBehaviour
{
    [Header("Audio Sliders")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider ambienceSlider;

    [Header("Gameplay Sliders")]
    [SerializeField] private Slider sensitivitySlider;

    private SettingsManager settings;

    private void OnEnable()
    {
        settings = SettingsManager.Instance;

        if (settings == null)
        {
            Debug.LogError("SettingsManager not found!");
            return;
        }

        // Initialize slider values without triggering events.
        masterSlider.SetValueWithoutNotify(settings.MasterVol);
        musicSlider.SetValueWithoutNotify(settings.MusicVol);
        sfxSlider.SetValueWithoutNotify(settings.SFXVol);
        ambienceSlider.SetValueWithoutNotify(settings.AmbienceVol);
        sensitivitySlider.SetValueWithoutNotify(
            settings.MouseSensitivity);

        // Register listeners.
        masterSlider.onValueChanged.AddListener(settings.SetMasterVol);
        musicSlider.onValueChanged.AddListener(settings.SetMusicVol);
        sfxSlider.onValueChanged.AddListener(settings.SetSFXVol);
        ambienceSlider.onValueChanged.AddListener(settings.SetAmbienceVol);
        sensitivitySlider.onValueChanged.AddListener(
            settings.SetMouseSensitivity);
    }

    private void OnDisable()
    {
        if (settings == null)
            return;

        masterSlider.onValueChanged.RemoveListener(settings.SetMasterVol);
        musicSlider.onValueChanged.RemoveListener(settings.SetMusicVol);
        sfxSlider.onValueChanged.RemoveListener(settings.SetSFXVol);
        ambienceSlider.onValueChanged.RemoveListener(settings.SetAmbienceVol);
        sensitivitySlider.onValueChanged.RemoveListener(
            settings.SetMouseSensitivity);
    }
}
