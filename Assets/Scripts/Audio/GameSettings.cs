using UnityEngine;

// Pengaturan pemain, disimpan di PlayerPrefs.
public static class GameSettings
{
    private const string MasterKey = "settings.masterVolume";
    private const string MusicKey = "settings.musicVolume";
    private const string SfxKey = "settings.sfxVolume";
    private const string CrouchToggleKey = "settings.crouchToggle";
    private const string ShowControlsKey = "settings.showControls";
    private const string QualityKey = "settings.quality";

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterKey, 0.8f);
        set { PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); }
    }

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(MusicKey, 0.6f);
        set => PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
    }

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(SfxKey, 0.8f);
        set => PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
    }

    // true = tekan Ctrl untuk jongkok/berdiri, false = tahan Ctrl.
    public static bool CrouchToggle
    {
        get => PlayerPrefs.GetInt(CrouchToggleKey, 1) == 1;
        set => PlayerPrefs.SetInt(CrouchToggleKey, value ? 1 : 0);
    }

    public static bool ShowControls
    {
        get => PlayerPrefs.GetInt(ShowControlsKey, 1) == 1;
        set => PlayerPrefs.SetInt(ShowControlsKey, value ? 1 : 0);
    }

    public static int Quality
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
        set
        {
            int level = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
            PlayerPrefs.SetInt(QualityKey, level);
            QualitySettings.SetQualityLevel(level, true);
        }
    }

    public static void Apply()
    {
        AudioListener.volume = MasterVolume;

        if (PlayerPrefs.HasKey(QualityKey))
            QualitySettings.SetQualityLevel(Quality, true);
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }
}
