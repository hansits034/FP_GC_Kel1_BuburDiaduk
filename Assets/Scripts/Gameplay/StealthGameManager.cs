using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum StealthGameState
{
    Briefing,
    Playing,
    Paused,
    Won,
    Lost,
    Tip // jendela instruksi pelatihan terbuka (game dijeda)
}

// Alur level: main, pause (Esc), menang (ekstraksi), kalah (tertangkap).
public class StealthGameManager : MonoBehaviour
{
    [Tooltip("Scene berikutnya setelah menang. Kosong = kembali ke menu utama.")]
    [SerializeField] private string nextSceneName;

    [Tooltip("Scene menu utama (index 0 di Build Settings).")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    public static StealthGameManager Instance { get; private set; }

    // Dipanggil sekali saat run berakhir (true = menang). Dipakai DDA.
    public static event System.Action<bool> RunEnded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents() { RunEnded = null; }

    public static bool IsPlaying => Instance == null || Instance.State == StealthGameState.Playing;

    public StealthGameState State { get; private set; } = StealthGameState.Playing;
    public float ElapsedTime { get; private set; }
    public string EndReason { get; private set; }
    public bool HasNextLevel => !string.IsNullOrEmpty(nextSceneName);

    public int Detections => AlertManager.Current != null ? AlertManager.Current.DetectionCount : 0;

    public string Rating
    {
        get
        {
            if (Detections == 0) return "HANTU - tidak pernah terdeteksi";
            if (Detections <= 2) return "AGEN - sempat ketahuan " + Detections + "x";
            return "BERISIK - ketahuan " + Detections + "x";
        }
    }

    private void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
    }

    private void Start()
    {
        if (MissionManager.Instance != null)
            SetState(StealthGameState.Briefing);
    }

    public void EndBriefing()
    {
        if (State == StealthGameState.Briefing)
            SetState(StealthGameState.Playing);
    }

    public void ShowTip()
    {
        if (State == StealthGameState.Playing)
            SetState(StealthGameState.Tip);
    }

    public void CloseTip()
    {
        if (State == StealthGameState.Tip)
            SetState(StealthGameState.Playing);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (State == StealthGameState.Playing)
            ElapsedTime += Time.deltaTime;

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame && !StealthHUD.TryCloseSubmenu())
        {
            if (State == StealthGameState.Playing) Pause();
            else if (State == StealthGameState.Paused) Resume();
        }

        if (State == StealthGameState.Playing || State == StealthGameState.Briefing || State == StealthGameState.Tip)
            return;

        if (keyboard.rKey.wasPressedThisFrame)
            Restart();

        if (State == StealthGameState.Won && keyboard.nKey.wasPressedThisFrame)
            LoadNext();
    }

    public void Pause()
    {
        if (State == StealthGameState.Playing)
            SetState(StealthGameState.Paused);
    }

    public void Resume()
    {
        if (State == StealthGameState.Paused)
            SetState(StealthGameState.Playing);
    }

    public void Win()
    {
        if (State == StealthGameState.Won || State == StealthGameState.Lost)
            return;

        EndReason = "Berhasil kabur dari fasilitas!";
        SetState(StealthGameState.Won);
        RunEnded?.Invoke(true);
        SoundManager.Play("win", 1f, 0f);
    }

    public void Lose(string reason)
    {
        if (State == StealthGameState.Won || State == StealthGameState.Lost)
            return;

        EndReason = reason;
        SetState(StealthGameState.Lost);
        RunEnded?.Invoke(false);
        SoundManager.Play("lose", 1f, 0f);
    }

    // Pindah scene lewat animasi pintu (SceneTransition). Game tetap dijeda selama pintu menutup.
    public void Restart()
    {
        SceneTransition.Load(SceneManager.GetActiveScene().name);
    }

    public void LoadNext()
    {
        if (HasNextLevel)
            SceneTransition.Load(nextSceneName);
        else
            LoadMainMenu();
    }

    public void LoadMainMenu()
    {
        if (Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            SceneTransition.Load(mainMenuSceneName);
        else
            SceneTransition.Load(0);
    }

    private void SetState(StealthGameState state)
    {
        State = state;
        Time.timeScale = state == StealthGameState.Playing ? 1f : 0f;
        Debug.Log("Game state = " + state);
    }
}
