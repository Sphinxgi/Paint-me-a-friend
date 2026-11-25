using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum VolumeSettings { Master, Music, SFX, Ambience }

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;
    [SerializeField] private AudioSource ambienceAudioSource;
    [SerializeField] private AudioSource footstepsAudioSource;
    [SerializeField] private AudioSource paintingAudioSource;

    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Audio Settings")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Range(0f, 1f)] public float ambienceVolume = 1f;

    [Header("SFX References")]
    public AudioClip walkingSFX;
    public AudioClip pickUpObjectSFX;
    public AudioClip dropObjectSFX;
    public AudioClip doorCloseSFX;
    public AudioClip doorOpenSFX;
    public AudioClip puzzleButtonPressSFX;
    public AudioClip pressurePlateSFX;
    public AudioClip paintingSFX;
    public AudioClip colorSwitchSFX;
    public AudioClip pauseSFX;
    public AudioClip uiButtonPressSFX;

    [Header("Ambience References")]
    public AudioClip darkAmbience;
    public AudioClip lightAmbience1;
    public AudioClip lightAmbience2;
    public AudioClip mainMenuAmbience;

    [Header("Volume Sliders")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider ambienceSlider;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SetupFootstepsSource();
        SetupPaintingSource();
        LoadAudioSettings();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AudioListener.pause = false;
        StopWalkingSFX();
        StopPaintingSFX();
        UpdateVolumeSliders();
        if (ambienceAudioSource != null)
        {
            if (scene.name == "MainMenu")
            {
                PlayAmbience(mainMenuAmbience);
            }
            else if (scene.name == "Lv1")
            {
                PlayAmbience(lightAmbience1);
            }
            else if (scene.name == "Lv2")
            {
                if (ambienceAudioSource == lightAmbience1)
                    return;
                else
                    PlayAmbience(lightAmbience1);
            }
            else if (scene.name == "Lv3")
            {
                    PlayAmbience(lightAmbience2);
            }
            else if (scene.name == "Lv4")
            {
                if (ambienceAudioSource == lightAmbience2)
                    return;
                else
                    PlayAmbience(lightAmbience2);
            }
            else if (scene.name == "Lv5")
            {
                PlayAmbience(darkAmbience);
            }
            else
            {
                StopAmbience();
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void UpdateVolumeSliders()
    {
        Slider[] sliders = FindObjectsByType<Slider>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Slider slider in sliders)
        {
            switch (slider.name)
            {
                case "MasterSlider":
                    slider.SetValueWithoutNotify(
                        PlayerPrefs.GetFloat("MasterVol", 1f));
                    break;

                case "MusicSlider":
                    slider.SetValueWithoutNotify(
                        PlayerPrefs.GetFloat("MusicVol", 1f));
                    break;

                case "SFXSlider":
                    slider.SetValueWithoutNotify(
                        PlayerPrefs.GetFloat("SFXVol", 1f));
                    break;
                case "AmbienceSlider":
                    slider.SetValueWithoutNotify(
                        PlayerPrefs.GetFloat("AmbienceVol", 1f));
                    break;
            }
        }
    }

    private void LoadAudioSettings()
    {
        float master = PlayerPrefs.GetFloat("MasterVol", 1f);
        float music = PlayerPrefs.GetFloat("MusicVol", 1f);
        float sfx = PlayerPrefs.GetFloat("SFXVol", 1f);
        float ambience = PlayerPrefs.GetFloat("AmbienceVol", 1f);

        masterSlider.SetValueWithoutNotify(master);
        musicSlider.SetValueWithoutNotify(music);
        sfxSlider.SetValueWithoutNotify(sfx);
        ambienceSlider.SetValueWithoutNotify(ambience);

        SetMasterVolume(master);
        SetMusicVolume(music);
        SetSFXVolume(sfx);
        SetAmbienceVolume(ambience);
    }

    private void SetupFootstepsSource()
    {
        // Create a dedicated looping source so footsteps do not interrupt other SFX.
        if (footstepsAudioSource == null)
        {
            footstepsAudioSource = gameObject.AddComponent<AudioSource>();

            if (sfxAudioSource != null)
                footstepsAudioSource.outputAudioMixerGroup = sfxAudioSource.outputAudioMixerGroup;
        }

        footstepsAudioSource.playOnAwake = false;
        footstepsAudioSource.loop = true;
    }

    private void SetupPaintingSource()
    {
        // Create a dedicated looping source so painting sounds do not interrupt other SFX.
        if (paintingAudioSource == null)
        {
            paintingAudioSource = gameObject.AddComponent<AudioSource>();

            if (sfxAudioSource != null)
                paintingAudioSource.outputAudioMixerGroup = sfxAudioSource.outputAudioMixerGroup;
        }

        paintingAudioSource.playOnAwake = false;
        paintingAudioSource.loop = true;
    }

    public void PlayBGM(AudioClip clip)
    {
        if (musicAudioSource == null || clip == null)
            return;

        if (musicAudioSource.clip == clip && musicAudioSource.isPlaying)
            return;

        musicAudioSource.clip = clip;
        musicAudioSource.loop = true;
        musicAudioSource.Play();
    }

    public void StopBGM()
    {
        if (musicAudioSource != null)
            musicAudioSource.Stop();
    }

    public void PlaySFX(AudioClip clip)
    {
        if (sfxAudioSource == null || clip == null)
            return;

        sfxAudioSource.PlayOneShot(clip);
    }

    public void PlayAmbience(AudioClip clip)
    {
        if (ambienceAudioSource == null || clip == null)
            return;

        if (ambienceAudioSource.clip == clip && ambienceAudioSource.isPlaying)
            return;

        ambienceAudioSource.clip = clip;
        ambienceAudioSource.loop = true;
        ambienceAudioSource.Play();
    }

    public void StopAmbience()
    {
        if (ambienceAudioSource != null)
            ambienceAudioSource.Stop();
    }

    // Footsteps use their own AudioSource so other SFX can play over them.
    public void StartWalkingSFX()
    {
        if (footstepsAudioSource == null || walkingSFX == null)
            return;

        if (footstepsAudioSource.clip != walkingSFX)
            footstepsAudioSource.clip = walkingSFX;

        if (!footstepsAudioSource.isPlaying)
            footstepsAudioSource.Play();
    }

    public void StopWalkingSFX()
    {
        if (footstepsAudioSource != null && footstepsAudioSource.isPlaying)
            footstepsAudioSource.Stop();
    }

    // Painting uses its own AudioSource so other SFX can play over it.
    public void StartPaintingSFX()
    {
        if (paintingAudioSource == null || paintingSFX == null)
            return;
        if (paintingAudioSource.clip != paintingSFX)
            paintingAudioSource.clip = paintingSFX;
        if (!paintingAudioSource.isPlaying)
            paintingAudioSource.Play();
    }

    public void StopPaintingSFX()
    {
        if (paintingAudioSource != null && paintingAudioSource.isPlaying)
            paintingAudioSource.Stop();
    }

    // Convenience methods for gameplay and UI scripts.
    public void PlayPickupSFX() => PlaySFX(pickUpObjectSFX);
    public void PlayDropSFX() => PlaySFX(dropObjectSFX);
    public void PlayDoorCloseSFX() => PlaySFX(doorCloseSFX);
    public void PlayDoorOpenSFX() => PlaySFX(doorOpenSFX);
    public void PlayPuzzleButtonPressSFX() => PlaySFX(puzzleButtonPressSFX);
    public void PlayPressurePlateSFX() => PlaySFX(pressurePlateSFX);
    public void PlayPaintingSFX() => PlaySFX(paintingSFX);
    public void PlayColorSwitchSFX() => PlaySFX(colorSwitchSFX);
    public void PlayPauseSFX() => PlaySFX(pauseSFX);
    public void PlayUIButtonSFX() => PlaySFX(uiButtonPressSFX);

    public void PlayMainMenuAmbience() => PlayAmbience(mainMenuAmbience);

    public void SetMasterVolume(float sliderValue)
    {
        masterVolume = Mathf.Clamp01(sliderValue);
        SetMixerVolume("MasterVol", masterVolume);
    }

    public void SetMusicVolume(float sliderValue)
    {
        musicVolume = Mathf.Clamp01(sliderValue);
        SetMixerVolume("MusicVol", musicVolume);
    }

    public void SetSFXVolume(float sliderValue)
    {
        sfxVolume = Mathf.Clamp01(sliderValue);
        SetMixerVolume("SFXVol", sfxVolume);
    }

    public void SetAmbienceVolume(float sliderValue)
    {
        ambienceVolume = Mathf.Clamp01(sliderValue);
        SetMixerVolume("AmbienceVol", ambienceVolume);
    }

    private void SetMixerVolume(string parameterName, float volume)
    {
        if (audioMixer == null)
            return;

        // AudioMixer uses decibels; Log10(0) is undefined, so use a low floor for mute.
        float decibels = volume <= 0.0001f ? -80f : Mathf.Log10(volume) * 20f;
        audioMixer.SetFloat(parameterName, decibels);
    }
}
