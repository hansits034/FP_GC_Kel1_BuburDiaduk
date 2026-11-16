using UnityEngine;
using UnityEngine.SceneManagement;

// Transisi antar scene: dua pintu baja menutup dari kiri & kanan, kartu info level
// muncul di tengah selama scene dimuat (async), lalu pintu terbuka di scene baru.
// Dibuat otomatis saat dipanggil, bertahan antar scene (DontDestroyOnLoad).
public class SceneTransition : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Closing,
        Loading,
        Opening
    }

    private const float CloseDuration = 0.6f;
    private const float OpenDuration = 0.65f;
    private const float CardHoldDuration = 1.6f;   // lama kartu level ditampilkan
    private const float PlainHoldDuration = 0.15f; // tanpa kartu (ulangi level / menu)

    private static SceneTransition instance;

    private Phase phase = Phase.Idle;
    private float phaseStart;
    private string targetScene;
    private AsyncOperation loading;
    private float shownProgress;

    private bool showCard;
    private string kicker;
    private string title;

    public static bool IsBusy => instance != null && instance.phase != Phase.Idle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    // Pindah scene dengan animasi. Kartu level otomatis tampil untuk scene "LevelN_...".
    public static void Load(string sceneName)
    {
        if (IsBusy || string.IsNullOrEmpty(sceneName))
            return;

        if (instance == null)
        {
            GameObject go = new GameObject("SceneTransition");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SceneTransition>();
        }

        instance.Begin(sceneName);
    }

    public static void Load(int buildIndex)
    {
        string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        Load(System.IO.Path.GetFileNameWithoutExtension(path));
    }

    private void Begin(string sceneName)
    {
        targetScene = sceneName;
        shownProgress = 0f;
        showCard = Describe(sceneName, out kicker, out title);
        SetPhase(Phase.Closing);
        SoundManager.Play("door_open", 0.8f, 0f);
    }

    // Judul untuk kartu transisi berdasarkan nama scene.
    private static bool Describe(string sceneName, out string kicker, out string title)
    {
        kicker = "";
        title = "";

        if (!sceneName.StartsWith("Level"))
            return false;

        if (sceneName.Contains("Tutorial")) { kicker = "PELATIHAN"; title = "Simulasi Akademi Agen"; }
        else if (sceneName.Contains("SecurityCheckpoint")) { kicker = "LEVEL 01"; title = "Pos Keamanan Sektor 7"; }
        else if (sceneName.Contains("ResearchLab")) { kicker = "LEVEL 02"; title = "Laboratorium Riset Bawah Tanah"; }
        else
        {
            // Level baru: "Level3_NamaLevel" -> "LEVEL 03" / "Nama Level"
            int underscore = sceneName.IndexOf('_');
            string number = underscore > 5 ? sceneName.Substring(5, underscore - 5) : "";
            kicker = number.Length > 0 ? "LEVEL " + number.PadLeft(2, '0') : "OPERASI";
            title = underscore > 0 ? System.Text.RegularExpressions.Regex.Replace(sceneName.Substring(underscore + 1), "(\\B[A-Z])", " $1") : sceneName;
        }

        return true;
    }

    private void SetPhase(Phase newPhase)
    {
        phase = newPhase;
        phaseStart = Time.unscaledTime;
    }

    private void Update()
    {
        float t = Time.unscaledTime - phaseStart;

        switch (phase)
        {
            case Phase.Closing:
                if (t >= CloseDuration)
                {
                    Time.timeScale = 1f;
                    loading = SceneManager.LoadSceneAsync(targetScene);
                    loading.allowSceneActivation = false;
                    SetPhase(Phase.Loading);
                }
                break;

            case Phase.Loading:
                float hold = showCard ? CardHoldDuration : PlainHoldDuration;
                float progress = Mathf.Clamp01(loading.progress / 0.9f);

                // Bar progres mengikuti loading, tapi tidak penuh sebelum waktu tahan selesai.
                float target = Mathf.Min(progress, Mathf.Clamp01(t / hold));
                shownProgress = Mathf.MoveTowards(shownProgress, target, Time.unscaledDeltaTime * 2.5f);

                if (progress >= 1f && t >= hold && shownProgress >= 0.99f)
                    loading.allowSceneActivation = true;

                if (loading.isDone)
                {
                    SetPhase(Phase.Opening);
                    SoundManager.Play("door_open", 0.6f, 0f);
                }
                break;

            case Phase.Opening:
                if (t >= OpenDuration)
                    SetPhase(Phase.Idle);
                break;
        }
    }

    // ==================================================
    // DRAW
    // ==================================================

    private void OnGUI()
    {
        if (phase == Phase.Idle)
            return;

        // Gambar di atas semua UI lain & blokir klik/tombol selama transisi.
        GUI.depth = -1000;
        UIStyles.Begin();

        float t = Time.unscaledTime - phaseStart;
        float closed;

        switch (phase)
        {
            case Phase.Closing: closed = EaseInOut(Mathf.Clamp01(t / CloseDuration)); break;
            case Phase.Opening: closed = 1f - EaseInOut(Mathf.Clamp01(t / OpenDuration)); break;
            default: closed = 1f; break;
        }

        DrawDoors(closed);

        if (showCard)
            DrawCard(t);

        Event e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.KeyDown || e.type == EventType.ScrollWheel)
            e.Use();
    }

    private static float S(float v) => UIStyles.S(v);

    private static float EaseInOut(float x)
    {
        return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;
    }

    private static void DrawDoors(float closed)
    {
        float half = Screen.width * 0.5f;
        float width = half * closed + 2f;

        Rect left = new Rect(0f, 0f, width, Screen.height);
        Rect right = new Rect(Screen.width - width, 0f, width, Screen.height);

        DrawDoor(left, true);
        DrawDoor(right, false);
    }

    private static void DrawDoor(Rect rect, bool isLeft)
    {
        Color body = new Color(0.07f, 0.09f, 0.12f, 1f);
        UIStyles.DrawRect(rect, body);

        // Garis panel horizontal (pelat baja).
        float panel = S(120f);
        for (float y = panel; y < Screen.height; y += panel)
        {
            UIStyles.DrawRect(new Rect(rect.x, y, rect.width, S(2f)), new Color(0f, 0f, 0f, 0.5f));
            UIStyles.DrawRect(new Rect(rect.x, y + S(2f), rect.width, S(1f)), new Color(1f, 1f, 1f, 0.05f));
        }

        // Tepi dalam: garis hazard kuning-hitam + lampu aksen.
        float stripeW = S(26f);
        float edgeX = isLeft ? rect.xMax - stripeW : rect.x;
        float block = S(30f);
        int index = 0;

        for (float y = 0f; y < Screen.height; y += block, index++)
        {
            Color c = index % 2 == 0 ? new Color(0.95f, 0.72f, 0.12f) : new Color(0.08f, 0.08f, 0.09f);
            UIStyles.DrawRect(new Rect(edgeX, y, stripeW, block), c);
        }

        float glowX = isLeft ? edgeX - S(4f) : edgeX + stripeW;
        UIStyles.DrawRect(new Rect(glowX, 0f, S(4f), Screen.height), new Color(1f, 0.79f, 0.29f, 0.5f));
    }

    private void DrawCard(float t)
    {
        float alpha;

        switch (phase)
        {
            case Phase.Closing: alpha = Mathf.Clamp01((t - CloseDuration * 0.7f) / (CloseDuration * 0.3f)); break;
            case Phase.Loading: alpha = 1f; break;
            default: alpha = 1f - Mathf.Clamp01(t / (OpenDuration * 0.35f)); break;
        }

        if (alpha <= 0f)
            return;

        float width = S(720f);
        float height = S(260f);
        float rise = (1f - alpha) * S(16f);
        Rect card = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f + rise, width, height);

        GUI.color = new Color(1f, 1f, 1f, alpha);

        UIStyles.DrawPanel(card, UIStyles.Accent, true);

        UIStyles.Label(new Rect(card.x, card.y + S(30f), card.width, S(26f)), "<color=#ffd36a>" + kicker + "</color>", UIStyles.Header, TextAnchor.MiddleCenter);
        UIStyles.FitLabel(new Rect(card.x + S(36f), card.y + S(60f), card.width - S(72f), S(64f)), title.ToUpper(), UIStyles.BigCenter, TextAnchor.MiddleCenter);

        // Bar loading
        Rect bar = new Rect(card.x + S(80f), card.y + S(150f), card.width - S(160f), S(8f));
        UIStyles.DrawBar(bar, shownProgress, UIStyles.Accent);

        string status = shownProgress >= 0.99f ? "OPERASI SIAP" : "MEMUAT DATA OPERASI...";
        GUI.Label(new Rect(card.x, bar.yMax + S(10f), card.width, S(24f)),
                  "<color=#9aa3b0>" + status + "  " + Mathf.RoundToInt(shownProgress * 100f) + "%</color>", UIStyles.SmallCenter);

        GUI.Label(new Rect(card.x, card.yMax - S(40f), card.width, S(22f)),
                  "<color=#5f6875>" + Credits.TeamLine + "</color>", UIStyles.SmallCenter);

        GUI.color = Color.white;
    }
}
