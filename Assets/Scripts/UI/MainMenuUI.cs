using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Menu utama: mulai, pilih level, pengaturan, kontrol, keluar.
// Latar belakangnya adalah diorama 3D di scene MainMenu.
public class MainMenuUI : MonoBehaviour
{
    [System.Serializable]
    public class LevelEntry
    {
        public string sceneName;
        public string title;
        [TextArea] public string description;
        [Range(1, 3)] public int difficulty = 1;
    }

    [SerializeField] private string gameTitle = "OPERASI SENYAP";
    [SerializeField] private string subtitle = "Stealth Infiltration";
    [SerializeField] private string credits = "Aset: Quaternius & KayKit (CC0)  |  Font: Rajdhani (OFL)  |  Audio: sintesis prosedural";
    [SerializeField] private LevelEntry[] levels;

    private enum Page
    {
        Main,
        LevelSelect,
        Settings,
        Controls
    }

    private Page page = Page.Main;
    private float pageTime;

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && page != Page.Main)
            SetPage(Page.Main);
    }

    private void SetPage(Page newPage)
    {
        GameSettings.Save();
        page = newPage;
        pageTime = Time.unscaledTime;
    }

    private static float S(float v) => UIStyles.S(v);

    private void OnGUI()
    {
        UIStyles.Begin();
        DrawBackdrop();

        switch (page)
        {
            case Page.Main: DrawMain(); DrawTeamCredit(); break;
            case Page.LevelSelect: DrawLevelSelect(); break;
            case Page.Settings: DrawSettings(); break;
            case Page.Controls: DrawControls(); break;
        }

        GUI.Label(new Rect(S(60f), Screen.height - S(44f), Screen.width - S(120f), S(24f)),
                  "<color=#6d7685>" + credits + "</color>", UIStyles.Small);
    }

    private void DrawBackdrop()
    {
        // Gradien gelap dari kiri agar teks terbaca di atas diorama 3D.
        Texture2D gradient = UIStyles.Tex("gradient_left");
        if (gradient != null)
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, page == Page.Main ? 0.92f : 0.97f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width * (page == Page.Main ? 0.62f : 1.4f), Screen.height), gradient, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        Texture2D vignette = UIStyles.Tex("vignette");
        if (vignette != null)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        // Garis pindai tipis (nuansa monitor keamanan).
        float sweep = (Time.unscaledTime * 90f) % (Screen.height + S(300f)) - S(150f);
        UIStyles.DrawRect(new Rect(0, sweep, Screen.width, S(2f)), new Color(1f, 0.79f, 0.29f, 0.08f));
    }

    private void DrawTitle(float x, float y)
    {
        GUI.Label(new Rect(x, y, S(900f), S(26f)), "<color=#ffd36a>// RAHASIA - HANYA UNTUK AGEN</color>", UIStyles.Header);
        GUI.Label(new Rect(x - S(4f), y + S(16f), S(1100f), S(120f)), gameTitle, UIStyles.Huge);
        UIStyles.DrawStyle(new Rect(x, y + S(130f), S(90f), S(5f)), UIStyles.Bar, UIStyles.Accent);
        UIStyles.Label(new Rect(x + S(104f), y + S(117f), S(600f), S(30f)), "<color=#aeb6c2>" + subtitle.ToUpper() + "</color>", UIStyles.Text, TextAnchor.MiddleLeft);
    }

    // Kartu "Dibuat oleh" di pojok kanan bawah menu utama.
    private void DrawTeamCredit()
    {
        float width = S(420f);
        float height = S(118f);
        float alpha = Mathf.Clamp01((Time.unscaledTime - pageTime - 0.4f) / 0.5f);
        Rect rect = new Rect(Screen.width - width - S(48f), Screen.height - height - S(64f), width, height);

        GUI.color = new Color(1f, 1f, 1f, alpha);
        UIStyles.DrawPanel(rect, UIStyles.Accent);

        float pad = S(22f);
        GUI.Label(new Rect(rect.x + pad, rect.y + S(16f), rect.width - pad * 2f, S(22f)), "DIBUAT OLEH", UIStyles.Header);
        UIStyles.FitLabel(new Rect(rect.x + pad, rect.y + S(38f), rect.width - pad * 2f, S(34f)),
                  Credits.Group + "  <color=#ffd36a>\u2014</color>  " + Credits.TeamName, UIStyles.Title, TextAnchor.MiddleLeft, 0.6f);
        UIStyles.FitLabel(new Rect(rect.x + pad, rect.y + S(76f), rect.width - pad * 2f, S(26f)),
                  "<color=#b9c0cb>" + Credits.MembersLine + "</color>", UIStyles.Text, TextAnchor.MiddleLeft, 0.7f);
        GUI.color = Color.white;
    }

    private void DrawMain()
    {
        float x = S(110f);
        DrawTitle(x, Screen.height * 0.14f);

        float width = S(380f);
        float height = S(58f);
        float gap = S(12f);
        float y = Screen.height * 0.42f;

        string[] labels = { "MULAI MISI", "PILIH LEVEL", "PENGATURAN", "KONTROL", "KELUAR" };

        for (int i = 0; i < labels.Length; i++)
        {
            // Animasi masuk bertahap.
            float t = Mathf.Clamp01((Time.unscaledTime - pageTime - i * 0.06f) / 0.3f);
            float offset = (1f - t) * S(-40f);
            Rect rect = new Rect(x + offset, y, width, height);

            GUI.color = new Color(1f, 1f, 1f, t);
            if (UIStyles.ButtonClicked(rect, labels[i]))
                OnMainButton(i);
            GUI.color = Color.white;

            y += height + gap;
        }
    }

    private void OnMainButton(int index)
    {
        switch (index)
        {
            case 0: LoadLevel(0); break;
            case 1: SetPage(Page.LevelSelect); break;
            case 2: SetPage(Page.Settings); break;
            case 3: SetPage(Page.Controls); break;
            case 4:
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
        }
    }

    private void DrawLevelSelect()
    {
        if (levels == null || levels.Length == 0)
        {
            SetPage(Page.Main);
            return;
        }

        float y = Screen.height * 0.2f;
        GUI.Label(new Rect(0, y, Screen.width, S(26f)), "<color=#ffd36a>OPERASI TERSEDIA</color>", new GUIStyle(UIStyles.Header) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(0, y + S(24f), Screen.width, S(60f)), "PILIH LEVEL", UIStyles.BigCenter);

        float cardW = S(460f);
        float cardH = S(420f);
        float gap = S(36f);
        float totalW = levels.Length * cardW + (levels.Length - 1) * gap;
        float x = (Screen.width - totalW) * 0.5f;
        y += S(110f);

        for (int i = 0; i < levels.Length; i++)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - pageTime - i * 0.08f) / 0.3f);
            Rect card = new Rect(x + i * (cardW + gap), y + (1f - t) * S(30f), cardW, cardH);
            GUI.color = new Color(1f, 1f, 1f, t);

            UIStyles.DrawPanel(card, UIStyles.Accent, true);

            float pad = S(28f);
            string header = i == 0 ? "PELATIHAN" : "LEVEL 0" + i;
            GUI.Label(new Rect(card.x + pad, card.y + pad, S(200f), S(24f)), header, UIStyles.Header);

            // Tingkat kesulitan
            for (int d = 0; d < 3; d++)
            {
                Rect pip = new Rect(card.xMax - pad - S(22f) * (3 - d), card.y + pad + S(4f), S(16f), S(16f));
                UIStyles.DrawIcon(pip, d < levels[i].difficulty ? "dot" : "ring", d < levels[i].difficulty ? UIStyles.Danger : UIStyles.Muted);
            }

            GUI.Label(new Rect(card.x + pad, card.y + pad + S(26f), card.width - pad * 2f, S(70f)), levels[i].title, UIStyles.Title);
            UIStyles.DrawRect(new Rect(card.x + pad, card.y + S(130f), card.width - pad * 2f, 1f), new Color(1f, 1f, 1f, 0.1f));
            // Deskripsi berhenti sebelum tombol MAINKAN.
            GUI.Label(new Rect(card.x + pad, card.y + S(144f), card.width - pad * 2f, cardH - S(144f) - S(100f)),
                      "<color=#b9c0cb>" + levels[i].description + "</color>", UIStyles.Text);

            if (UIStyles.ButtonClicked(new Rect(card.x + pad, card.yMax - S(84f), card.width - pad * 2f, S(56f)), "MAINKAN"))
                LoadLevel(i);

            GUI.color = Color.white;
        }

        BackButton(y + cardH + S(40f));
    }

    private void DrawSettings()
    {
        float width = S(760f);
        float height = SettingsPanel.PreferredHeight;
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        if (SettingsPanel.Draw(rect))
            SetPage(Page.Main);
    }

    private void DrawControls()
    {
        float width = S(700f);
        float height = S(560f);
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        UIStyles.DrawPanel(rect, UIStyles.Accent, true);

        GUI.Label(new Rect(rect.x, rect.y + S(26f), rect.width, S(24f)), "<color=#ffd36a>PANDUAN AGEN</color>", new GUIStyle(UIStyles.Header) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(rect.x, rect.y + S(48f), rect.width, S(56f)), "KONTROL", UIStyles.BigCenter);

        string[,] rows =
        {
            { "WASD", "Gerak" },
            { "SHIFT", "Lari - cepat tapi berisik" },
            { "CTRL", "Jongkok - senyap, sembunyi di balik cover rendah" },
            { "SPACE", "Berguling ke arah gerak" },
            { "E", "Ambil / buka / pakai (beberapa harus ditahan)" },
            { "KLIK KIRI", "Lempar kaleng ke arah mouse" },
            { "ESC", "Pause & menu" }
        };

        float y = rect.y + S(124f);
        float x = rect.x + S(60f);

        for (int i = 0; i < rows.GetLength(0); i++)
        {
            float kw = UIStyles.DrawKeyCap(x, y + S(4f), S(30f), rows[i, 0]);
            GUI.Label(new Rect(x + Mathf.Max(kw, S(120f)) + S(16f), y, rect.width - S(260f), S(38f)), rows[i, 1],
                      new GUIStyle(UIStyles.Text) { alignment = TextAnchor.MiddleLeft });
            y += S(46f);
        }

        float buttonW = S(260f);
        if (UIStyles.ButtonClicked(new Rect(rect.x + (rect.width - buttonW) * 0.5f, rect.yMax - S(78f), buttonW, S(52f)), "KEMBALI"))
            SetPage(Page.Main);
    }

    private void BackButton(float y)
    {
        float width = S(260f);

        if (UIStyles.ButtonClicked(new Rect((Screen.width - width) * 0.5f, y, width, S(52f)), "KEMBALI"))
            SetPage(Page.Main);
    }

    private void LoadLevel(int index)
    {
        if (levels == null || index >= levels.Length)
            return;

        SceneTransition.Load(levels[index].sceneName);
    }
}
