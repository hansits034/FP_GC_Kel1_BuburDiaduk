using System.Collections.Generic;
using UnityEngine;

// Pemutar efek suara & musik latar. Dibuat otomatis saat game mulai (tidak perlu dipasang di scene).
// Audio diambil dari Resources/Audio/SFX dan Resources/Audio/Music, jadi file bisa diganti
// dengan audio lain selama namanya sama. Variasi: "footstep_1", "footstep_2" -> Play("footstep").
public class SoundManager : MonoBehaviour
{
    private const int SfxVoices = 16;
    private const float MusicFadeSpeed = 0.8f;

    private const string MenuTrack = "music_menu";
    private const string StealthTrack = "music_stealth";
    private const string AlertTrack = "music_alert";

    private static SoundManager instance;

    private readonly Dictionary<string, List<AudioClip>> sfx = new Dictionary<string, List<AudioClip>>();
    private readonly Dictionary<string, AudioClip> music = new Dictionary<string, AudioClip>();

    private AudioSource[] voices;
    private int nextVoice;

    private AudioSource musicA;
    private AudioSource musicB;
    private AudioSource activeMusic;
    private string currentTrack;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        GameSettings.Apply();

        if (instance != null)
            return;

        GameObject go = new GameObject("SoundManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SoundManager>();
    }

    private void Awake()
    {
        LoadClips();

        voices = new AudioSource[SfxVoices];
        for (int i = 0; i < SfxVoices; i++)
            voices[i] = CreateSource(false);

        musicA = CreateSource(true);
        musicB = CreateSource(true);
    }

    private AudioSource CreateSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        return source;
    }

    private void LoadClips()
    {
        foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio/SFX"))
        {
            string key = BaseName(clip.name);

            if (!sfx.TryGetValue(key, out List<AudioClip> list))
            {
                list = new List<AudioClip>();
                sfx[key] = list;
            }

            list.Add(clip);
        }

        foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio/Music"))
            music[clip.name] = clip;
    }

    // "footstep_2" -> "footstep"
    private static string BaseName(string name)
    {
        int underscore = name.LastIndexOf('_');

        if (underscore > 0 && int.TryParse(name.Substring(underscore + 1), out _))
            return name.Substring(0, underscore);

        return name;
    }

    // ==================================================
    // SFX
    // ==================================================

    // Suara 2D (UI, suara Player sendiri).
    public static void Play(string name, float volume = 1f, float pitchVariation = 0.05f)
    {
        if (instance != null)
            instance.PlayInternal(name, volume, pitchVariation, 0f);
    }

    // Suara di dunia: makin jauh dari Player makin pelan, kiri/kanan mengikuti posisi di layar.
    public static void PlayAt(string name, Vector3 position, float volume = 1f, float maxDistance = 25f, float pitchVariation = 0.05f)
    {
        if (instance == null)
            return;

        Vector3 listener = GetListenerPosition();
        float distance = Vector3.Distance(listener, position);

        if (distance > maxDistance)
            return;

        float attenuation = 1f - distance / maxDistance;
        attenuation *= attenuation;

        float pan = 0f;
        Camera cam = Camera.main;

        if (cam != null)
        {
            float side = Vector3.Dot(position - listener, cam.transform.right);
            pan = Mathf.Clamp(side / 12f, -0.8f, 0.8f);
        }

        instance.PlayInternal(name, volume * Mathf.Max(0.05f, attenuation), pitchVariation, pan);
    }

    private static Vector3 GetListenerPosition()
    {
        PlayerController player = FindFirstObjectByType<PlayerController>();

        if (player != null)
            return player.transform.position;

        return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
    }

    private void PlayInternal(string name, float volume, float pitchVariation, float pan)
    {
        if (!sfx.TryGetValue(name, out List<AudioClip> clips) || clips.Count == 0)
        {
            Debug.LogWarning("SFX tidak ditemukan: " + name);
            return;
        }

        AudioSource source = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;

        source.clip = clips[Random.Range(0, clips.Count)];
        source.volume = Mathf.Clamp01(volume) * GameSettings.SfxVolume;
        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.panStereo = pan;
        source.Play();
    }

    // ==================================================
    // MUSIC
    // ==================================================

    private void Update()
    {
        string wanted = ChooseTrack();

        if (wanted != currentTrack)
            SwitchTrack(wanted);

        float target = GameSettings.MusicVolume * 0.7f;

        // Musik lebih pelan saat pause / layar akhir.
        if (StealthGameManager.Instance != null && StealthGameManager.Instance.State != StealthGameState.Playing)
            target *= 0.4f;

        float step = MusicFadeSpeed * Time.unscaledDeltaTime;

        foreach (AudioSource source in new[] { musicA, musicB })
        {
            float goal = source == activeMusic ? target : 0f;
            source.volume = Mathf.MoveTowards(source.volume, goal, step);

            if (source != activeMusic && source.isPlaying && source.volume <= 0f)
                source.Stop();
        }
    }

    private static string ChooseTrack()
    {
        if (StealthGameManager.Instance == null)
            return MenuTrack;

        AlertManager alert = AlertManager.Current;

        return alert != null && alert.Level == FacilityAlertLevel.Alert ? AlertTrack : StealthTrack;
    }

    private void SwitchTrack(string track)
    {
        currentTrack = track;

        if (!music.TryGetValue(track, out AudioClip clip))
            return;

        activeMusic = activeMusic == musicA ? musicB : musicA;
        activeMusic.clip = clip;
        activeMusic.volume = 0f;
        activeMusic.Play();
    }
}
