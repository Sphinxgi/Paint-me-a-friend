
using UnityEngine;
using UnityEngine.Audio;

[DefaultExecutionOrder(-1000)]
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Mouse Sensitivity")]
    [SerializeField] private float defaultSensitivity = 1f;

    public float MasterVol { get; private set; }
    public float MusicVol { get; private set; }
    public float SFXVol { get; private set; }
    public float AmbienceVol { get; private set; }
    public float MouseSensitivity { get; private set; }

    private const string MASTER_KEY = "MasterVol";
    private const string MUSIC_KEY = "MusicVol";
    private const string SFX_KEY = "SFXVol";
    private const string AMBIENCE_KEY = "AmbienceVol";
    private const string SENSITIVITY_KEY = "MouseSensitivity";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSettings();
        ApplyAudioSettings();
    }

    private void LoadSettings()
    {
        MasterVol = PlayerPrefs.GetFloat(MASTER_KEY, 1f);
        MusicVol = PlayerPrefs.GetFloat(MUSIC_KEY, 1f);
        SFXVol = PlayerPrefs.GetFloat(SFX_KEY, 1f);
        AmbienceVol = PlayerPrefs.GetFloat(AMBIENCE_KEY, 1f);

        MouseSensitivity = PlayerPrefs.GetFloat(
            SENSITIVITY_KEY, defaultSensitivity);
    }

    private void ApplyAudioSettings()
    {
        SetMixerVol("MasterVol", MasterVol);
        SetMixerVol("MusicVol", MusicVol);
        SetMixerVol("SFXVol", SFXVol);
        SetMixerVol("AmbienceVol", SFXVol);
    }

    private void SetMixerVol(string parameter, float value)
    {
        if (audioMixer == null)
            return;

        float decibels = value <= 0.0001f
            ? -80f
            : Mathf.Log10(value) * 20f;

        audioMixer.SetFloat(parameter, decibels);
    }

    public void SetMasterVol(float value)
    {
        MasterVol = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MASTER_KEY, MasterVol);
        SetMixerVol("MasterVol", MasterVol);
    }

    public void SetMusicVol(float value)
    {
        MusicVol = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MUSIC_KEY, MusicVol);
        SetMixerVol("MusicVol", MusicVol);
    }

    public void SetSFXVol(float value)
    {
        SFXVol = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(SFX_KEY, SFXVol);
        SetMixerVol("SFXVol", SFXVol);
    }
    public void SetAmbienceVol(float value)
    {
        SFXVol = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(AMBIENCE_KEY, AmbienceVol);
        SetMixerVol("AmbienceVol", SFXVol);
    }

    public void SetMouseSensitivity(float value)
    {
        MouseSensitivity = Mathf.Max(0.01f, value);
        PlayerPrefs.SetFloat(SENSITIVITY_KEY, MouseSensitivity);
    }

    public void SaveSettings()
    {
        PlayerPrefs.Save();
    }

    private void OnApplicationQuit()
    {
        SaveSettings();
    }
}
