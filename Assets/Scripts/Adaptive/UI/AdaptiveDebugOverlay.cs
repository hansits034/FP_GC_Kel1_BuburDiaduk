using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Panel demo: memperlihatkan PCG, DDA, ML, dan keputusan Game AI secara live.
//   F2 = panel detail      F3 = minimap validasi PCG
//   F4 = ulang layout yang sama (seed sama)   F7 = layout baru (difficulty sama)
//   F5 = paksa lebih mudah  F6 = paksa lebih sulit   F8 = reset data DDA/ML
public class AdaptiveDebugOverlay : MonoBehaviour
{
    [SerializeField] private bool showPanel;
    [SerializeField] private bool showMap = true;

    private Texture2D mapTexture;
    private GUIStyle text, header, small;
    private Vector2 scroll;
    private float scale = 1f;

    private ProceduralLevelBuilder Builder => ProceduralLevelBuilder.Instance;

    private void Update()
    {
        Keyboard k = Keyboard.current;

        if (k == null)
            return;

        if (k.f2Key.wasPressedThisFrame) showPanel = !showPanel;
        if (k.f3Key.wasPressedThisFrame) showMap = !showMap;

        if (Builder == null)
            return;

        if (k.f4Key.wasPressedThisFrame)
        {
            ProceduralLevelBuilder.NextSeedOverride = Builder.Seed;
            Reload("Ulang layout seed " + Builder.Seed);
        }

        if (k.f7Key.wasPressedThisFrame)
            Reload("Layout baru, difficulty sama");

        if (k.f5Key.wasPressedThisFrame)
        {
            DifficultyDirector.ForceNextDifficulty(DifficultyDirector.Difficulty - 0.2f);
            Reload("Difficulty dipaksa turun");
        }

        if (k.f6Key.wasPressedThisFrame)
        {
            DifficultyDirector.ForceNextDifficulty(DifficultyDirector.Difficulty + 0.2f);
            Reload("Difficulty dipaksa naik");
        }

        if (k.f8Key.wasPressedThisFrame)
        {
            DifficultyDirector.ResetAll();
            Reload("Data DDA & model ML direset");
        }
    }

    private static void Reload(string reason)
    {
        Debug.Log("[Overlay] " + reason);
        SceneTransition.Load(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (mapTexture != null)
            Destroy(mapTexture);
    }

    private void EnsureStyles()
    {
        scale = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);

        if (text != null && Mathf.Approximately(text.fontSize, Mathf.RoundToInt(13 * scale)))
            return;

        text = new GUIStyle(GUI.skin.label) { richText = true, fontSize = Mathf.RoundToInt(13 * scale), wordWrap = true };
        text.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
        small = new GUIStyle(text) { fontSize = Mathf.RoundToInt(11 * scale) };
        header = new GUIStyle(text) { fontSize = Mathf.RoundToInt(15 * scale), fontStyle = FontStyle.Bold };
        header.normal.textColor = new Color(1f, 0.83f, 0.42f);
    }

    private void OnGUI()
    {
        if (Builder == null || Builder.Layout == null)
            return;

        EnsureStyles();
        // Di belakang HUD (depth lebih besar) supaya prompt [E] & bar tahan-E tidak pernah tertutup.
        GUI.depth = 5;
        DrawTopBar();

        StealthGameState state = StealthGameManager.Instance != null ? StealthGameManager.Instance.State : StealthGameState.Playing;
        bool menu = state == StealthGameState.Briefing || state == StealthGameState.Paused ||
                    state == StealthGameState.Won || state == StealthGameState.Lost;

        if (showPanel)
            DrawPanel();

        // Pemilih difficulty digambar komponen terpisah (di DEPAN HUD), lihat AdaptiveDifficultySelector.
        if (!menu && showMap)
            DrawMap();
    }

    // ==================================================
    // PILIH TINGKAT KESULITAN (briefing / pause / akhir run)
    // ==================================================

    private static readonly (string label, float value)[] Presets =
    {
        ("MUDAH", 0.15f), ("NORMAL", 0.45f), ("SULIT", 0.75f), ("SANGAT SULIT", 0.95f)
    };

    private GUIStyle button, buttonOn;

    public bool IsMenuState
    {
        get
        {
            if (Builder == null || Builder.Layout == null || StealthGameManager.Instance == null) return false;
            StealthGameState state = StealthGameManager.Instance.State;
            return state == StealthGameState.Briefing || state == StealthGameState.Paused ||
                   state == StealthGameState.Won || state == StealthGameState.Lost;
        }
    }

    private void Awake()
    {
        gameObject.AddComponent<AdaptiveDifficultySelector>().Init(this);
    }

    public void DrawDifficultySelector()
    {
        EnsureStyles();

        if (button == null || button.fontSize != Mathf.RoundToInt(14 * scale))
        {
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * scale), fontStyle = FontStyle.Bold, richText = true };
            buttonOn = new GUIStyle(button);
            buttonOn.normal.textColor = buttonOn.hover.textColor = new Color(1f, 0.83f, 0.42f);
            buttonOn.normal.background = buttonOn.hover.background = Texture2D.grayTexture;
        }

        bool locked = DifficultyDirector.Locked;
        float current = DifficultyDirector.Difficulty;
        float bw = 150f * scale, bh = 38f * scale, gap = 8f * scale;
        int count = Presets.Length + 1;
        float total = count * bw + (count - 1) * gap;
        float x = (Screen.width - total) * 0.5f;
        float y = Screen.height - bh - 48f * scale;

        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(x - 12f, y - 34f * scale, total + 24f, bh + 44f * scale), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 30f * scale, total, 26f * scale),
            "<b>TINGKAT KESULITAN</b>  <color=#9aa3ad>(klik = buat ulang fasilitas)</color>   sekarang: " +
            (locked ? "<color=#ffd36a>DIKUNCI " : "<color=#9cff9c>ADAPTIF ") + Builder.Profile.Label + " (" + current.ToString("0.00") + ")</color>", text);

        if (GUI.Button(new Rect(x, y, bw, bh), "ADAPTIF (DDA)", locked ? button : buttonOn))
        {
            DifficultyDirector.Unlock();
            Reload("Mode adaptif");
        }

        for (int i = 0; i < Presets.Length; i++)
        {
            Rect r = new Rect(x + (i + 1) * (bw + gap), y, bw, bh);
            bool on = locked && Mathf.Abs(current - Presets[i].value) < 0.01f;

            if (GUI.Button(r, Presets[i].label, on ? buttonOn : button))
            {
                DifficultyDirector.SetDifficulty(Presets[i].value, true);
                Reload("Difficulty manual " + Presets[i].label);
            }
        }
    }

    // ==================================================
    // BAR ATAS (selalu tampil)
    // ==================================================

    private void DrawTopBar()
    {
        float offset = DifficultyDirector.LiveOffset;

        string liveText = Mathf.Abs(offset) > 0.001f
            ? "  <color=#8fd6ff>live " + (offset > 0 ? "+" : "") + offset.ToString("0.00") + "</color>"
            : "";

        PlayerHealth health = FindPlayerHealth();
        string hp = health != null ? "   HP <color=" + (health.Normalized < 0.5f ? "#ff6b6b" : "#9cff9c") + ">" + health.Current + "</color>" : "";

        string line = (DifficultyDirector.Locked ? "<b>MANUAL</b> " : "<b>DDA</b> ") + Builder.Profile.Label + " (" + Builder.Profile.difficulty.ToString("0.00") + ")" + liveText +
                      "   <b>Seed</b> " + Builder.Seed + hp + "   <color=#9aa3ad>[F2] panel  [F3] peta</color>";

        float w = 640f * scale, h = 26f * scale;
        Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 6f * scale, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10f, rect.y + 3f * scale, rect.width - 20f, rect.height), line, text);
    }

    private PlayerHealth cachedHealth;

    private PlayerHealth FindPlayerHealth()
    {
        if (cachedHealth == null)
            cachedHealth = FindFirstObjectByType<PlayerHealth>();

        return cachedHealth;
    }

    // ==================================================
    // PANEL DETAIL
    // ==================================================

    private void DrawPanel()
    {
        float w = 430f * scale;
        float h = Screen.height - 200f * scale;
        Rect rect = new Rect(Screen.width - w - 10f * scale, 10f * scale, w, h);

        GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
        scroll = GUILayout.BeginScrollView(scroll);

        LevelLayout layout = Builder.Layout;
        DifficultyProfile p = Builder.Profile;
        DifficultyProfile live = DifficultyDirector.LiveProfile ?? p;

        // ---------- PCG ----------
        GUILayout.Label("PCG  -  Procedural Content Generation", header);
        GUILayout.Label(
            "Ruangan <b>" + layout.rooms.Count + "</b>  |  Penjaga <b>" + layout.guards.Count + "</b>  |  CCTV <b>" + layout.cameras.Count +
            "</b>  |  Kaleng <b>" + layout.cans.Count + "</b>  |  Panel alarm <b>" + layout.alarms.Count + "</b>\n" +
            "Cover " + (p.coverDensity * 100f).ToString("0") + "% (" + layout.props.Count + " prop)  |  Loop " + p.extraLoops +
            "  |  Keycard kedalaman " + p.keycardDepth + "  |  Ekstraksi " + (p.extractionFar ? "jauh" : "di start") + "\n" +
            "Validasi: " + (layout.valid ? "<color=#9cff9c>LOLOS</color>" : "<color=#ff6b6b>TERBAIK</color>") +
            "  |  eksposur jalur stealth <b>" + layout.exposure + "/" + layout.exposureBudget + "</b> sel  |  panjang " + layout.pathLength +
            "\nVentilasi <b>" + layout.vents.Count + "</b>" + VentSummary(layout) +
            "\nLayout dicoba " + layout.layoutAttempts + ", populasi " + layout.populationAttempts + ", prop ditolak " + layout.rejectedProps +
            "  |  par " + layout.parTime.ToString("0") + " dtk  |  " + Builder.GenerationMs.ToString("0") + " ms", text);

        int from = Mathf.Max(0, layout.log.Count - 4);
        StringBuilder log = new StringBuilder();
        for (int i = from; i < layout.log.Count; i++)
            log.Append("<color=#9aa3ad>- ").Append(layout.log[i]).Append("</color>\n");
        GUILayout.Label(log.ToString(), small);

        // ---------- DDA ----------
        GUILayout.Space(4f);
        GUILayout.Label("DDA  -  Dynamic Difficulty Adjustment", header);

        PlayerPerformanceTracker tracker = PlayerPerformanceTracker.Instance;

        if (tracker != null)
        {
            RunRecord r = tracker.Current;
            GUILayout.Label(
                "Waktu " + r.time.ToString("0") + "/" + r.parTime.ToString("0") + " dtk  |  Deteksi <b>" + r.detections + "</b>  |  Alarm " + r.alarms +
                "\nDicurigai " + r.exposure.ToString("0.0") + " dtk  |  Damage " + r.damageTaken + "  |  Suara keras " + r.noiseEvents +
                "  |  Kaleng dilempar " + r.distractions +
                "\nSkor live " + Bar(tracker.LiveScore) + "  offset " + DifficultyDirector.LiveOffset.ToString("+0.00;-0.00;0.00") +
                "\n<color=#8fd6ff>" + tracker.LastLiveEvent + "</color>", text);
        }

        if (!string.IsNullOrEmpty(DifficultyDirector.LastDecision))
            GUILayout.Label("<color=#ffd36a>Keputusan terakhir:</color> " + DifficultyDirector.LastDecision, small);

        var history = DifficultyDirector.History;
        if (history.Count > 0)
        {
            StringBuilder hist = new StringBuilder("Riwayat: ");
            for (int i = Mathf.Max(0, history.Count - 6); i < history.Count; i++)
            {
                RunRecord r = history[i];
                hist.Append(r.won ? "<color=#9cff9c>W</color>" : "<color=#ff6b6b>L</color>")
                    .Append(" d").Append(r.difficulty.ToString("0.00"))
                    .Append(" p").Append(r.performance.ToString("0.00")).Append("   ");
            }
            GUILayout.Label(hist.ToString(), small);
        }

        // ---------- ML ----------
        GUILayout.Space(4f);
        GUILayout.Label("ML  -  Skill Predictor (Logistic Regression online)", header);
        SkillPredictor model = DifficultyDirector.Predictor;
        float[] features = SkillPredictor.BuildFeatures(history, p.difficulty);
        StringBuilder weights = new StringBuilder();
        for (int i = 0; i < SkillPredictor.FeatureCount; i++)
            weights.Append(SkillPredictor.FeatureNames[i]).Append(" ").Append(model.weights[i].ToString("+0.00;-0.00")).Append(i < SkillPredictor.FeatureCount - 1 ? ",  " : "");

        GUILayout.Label(
            "Skill pemain: <b>" + SkillPredictor.SkillLabel(features[2]) + "</b> (avg performa " + features[2].ToString("0.00") + ")" +
            "\nP(menang run ini) = <b>" + (DifficultyDirector.PredictedWinThisRun * 100f).ToString("0") + "%</b>  (target " +
            (SkillPredictor.TargetWinRate * 100f).ToString("0") + "%)  |  sampel " + model.samples + ", bobot ML di DDA " +
            (model.Confidence * 100f).ToString("0") + "%\n<color=#9aa3ad>w: " + weights + "</color>", text);

        // ---------- GAME AI ----------
        GUILayout.Space(4f);
        GUILayout.Label("GAME AI  -  Behavior Tree + Perception + Memory + Utility", header);
        GUILayout.Label(
            "Pandang " + live.visionRange.ToString("0.0") + " m / " + live.visionAngle.ToString("0") + " deg  |  curiga x" + live.sightGainRate.ToString("0.00") +
            "  |  dengar x" + live.hearingSensitivity.ToString("0.00") + "\nMemori " + live.memoryDuration.ToString("0") + " dtk  |  titik cari " + live.searchPointCount +
            "  |  kejar " + live.chaseSpeed.ToString("0.0") + " m/s  |  cooldown " + live.attackCooldown.ToString("0.0") + " dtk  |  damage " + live.attackDamage +
            "\nPerilaku: " + Flag("Raise Alarm", live.raiseAlarm) + Flag("Search Terkoordinasi", live.coordinatedSearch) +
            Flag("Cek Cover", live.checkCover) + Flag("Jaga Ekstraksi", live.guardExtraction) +
            Flag("RL Search" + (Builder.HasSearchModel ? "" : " (model belum ada)"), live.useLearnedSearch && Builder.HasSearchModel), text);

        foreach (EnemyBTController guard in Builder.Guards)
        {
            if (guard == null)
                continue;

            string state = "<color=" + Hex(guard.GetStateColor()) + ">" + guard.Awareness + "</color>";
            string utility = guard.Awareness == AwarenessState.Alert
                ? "  U(alarm) " + guard.AlarmUtility.ToString("0.00") + " vs U(kejar) " + guard.ChaseUtility.ToString("0.00")
                : "";

            GUILayout.Label("<b>" + guard.name + "</b>  " + state + "  " + guard.CurrentAction + "  curiga " + Bar(guard.Suspicion) + utility, small);
        }

        GUILayout.Space(4f);
        GUILayout.Label("<color=#9aa3ad>[F4] ulang seed  [F7] layout baru  [F5] lebih mudah  [F6] lebih sulit  [F8] reset DDA/ML  [F1] debug AI</color>", small);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static string VentSummary(LevelLayout layout)
    {
        if (layout.vents.Count == 0)
            return "  (layout ini tanpa ventilasi)";

        string text = "";
        foreach (LayoutVent v in layout.vents)
            text += "  [" + layout.rooms[v.roomA].name + " -> " + layout.rooms[v.roomB].name +
                    (v.hazard == VentHazardType.Toxic ? ", <color=#9cff6b>gas</color>" : v.hazard == VentHazardType.Laser ? ", <color=#ff6b6b>laser</color>" : "") + "]";

        return text + (layout.routeUsesVent
            ? "\n<color=#c39bff>Rute stealth terbaik LEWAT VENTILASI</color> (eksposur " + layout.exposure + " vs " + layout.exposureNoVent + " lewat pintu)"
            : "");
    }

    private static string Flag(string name, bool on)
    {
        return (on ? "<color=#9cff9c>[x] " : "<color=#6b7280>[ ] ") + name + "</color>   ";
    }

    private static string Bar(float value)
    {
        int n = Mathf.RoundToInt(Mathf.Clamp01(value) * 10f);
        return "<color=#ffd36a>" + new string('|', n) + "</color><color=#444>" + new string('|', 10 - n) + "</color> " + value.ToString("0.00");
    }

    private static string Hex(Color c)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    // ==================================================
    // MINIMAP VALIDASI PCG
    // ==================================================

    private void BuildMapTexture(LevelLayout layout)
    {
        const int px = 6;
        mapTexture = new Texture2D(layout.width * px, layout.height * px) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        Color[] themeColors =
        {
            new Color(0.25f, 0.3f, 0.4f), new Color(0.32f, 0.32f, 0.3f), new Color(0.35f, 0.28f, 0.22f),
            new Color(0.28f, 0.3f, 0.26f), new Color(0.2f, 0.25f, 0.38f)
        };

        System.Collections.Generic.HashSet<Vector2Int> path = new System.Collections.Generic.HashSet<Vector2Int>(layout.stealthPath);

        for (int x = 0; x < layout.width; x++)
        {
            for (int y = 0; y < layout.height; y++)
            {
                LayoutRoom room = layout.rooms[layout.roomOf[x, y]];
                Color c = themeColors[(int)room.theme];

                if (layout.watch != null) c = Color.Lerp(c, new Color(0.95f, 0.15f, 0.1f), Mathf.Clamp01(layout.watch[x, y]) * 0.75f);
                if (path.Contains(new Vector2Int(x, y))) c = Color.Lerp(c, new Color(0.2f, 1f, 0.4f), 0.55f);
                if (layout.blocked[x, y]) c = new Color(0.08f, 0.08f, 0.09f);

                for (int i = 0; i < px; i++)
                {
                    for (int j = 0; j < px; j++)
                    {
                        Color pixel = c;

                        // Dinding: tepi sel yang berbatasan dengan ruangan lain / luar peta.
                        if ((i == 0 && !SameRoom(layout, x, y, x - 1, y)) || (i == px - 1 && !SameRoom(layout, x, y, x + 1, y)) ||
                            (j == 0 && !SameRoom(layout, x, y, x, y - 1)) || (j == px - 1 && !SameRoom(layout, x, y, x, y + 1)))
                            pixel = new Color(0.85f, 0.88f, 0.92f);

                        mapTexture.SetPixel(x * px + i, y * px + j, pixel);
                    }
                }
            }
        }

        foreach (LayoutDoor door in layout.doors)
        {
            Color c = door.locked ? new Color(1f, 0.2f, 0.15f) : new Color(0.3f, 0.3f, 0.3f);
            PaintEdge(door.cellA, door.cellB, px, c);
        }

        mapTexture.Apply();
    }

    private static bool SameRoom(LevelLayout layout, int x, int y, int nx, int ny)
    {
        if (nx < 0 || ny < 0 || nx >= layout.width || ny >= layout.height)
            return false;

        if (layout.roomOf[x, y] == layout.roomOf[nx, ny])
            return true;

        foreach (LayoutDoor d in layout.doors)
        {
            Vector2Int a = new Vector2Int(x, y), b = new Vector2Int(nx, ny);
            if ((d.cellA == a && d.cellB == b) || (d.cellA == b && d.cellB == a))
                return !d.locked;
        }

        return false;
    }

    private void PaintEdge(Vector2Int a, Vector2Int b, int px, Color c)
    {
        Vector2Int d = b - a;
        for (int k = 1; k < px - 1; k++)
        {
            if (d.x != 0)
            {
                int x = d.x > 0 ? a.x * px + px - 1 : a.x * px;
                int x2 = d.x > 0 ? b.x * px : b.x * px + px - 1;
                mapTexture.SetPixel(x, a.y * px + k, c);
                mapTexture.SetPixel(x2, b.y * px + k, c);
            }
            else
            {
                int y = d.y > 0 ? a.y * px + px - 1 : a.y * px;
                int y2 = d.y > 0 ? b.y * px : b.y * px + px - 1;
                mapTexture.SetPixel(a.x * px + k, y, c);
                mapTexture.SetPixel(b.x * px + k, y2, c);
            }
        }
    }

    private void DrawMap()
    {
        LevelLayout layout = Builder.Layout;

        if (mapTexture == null)
            BuildMapTexture(layout);

        float cell = Mathf.Min(260f * scale / layout.width, 170f * scale / layout.height);
        float w = layout.width * cell, h = layout.height * cell;
        // Sisi kanan, tengah vertikal: area kosong HUD (kontrol di kanan atas, status di kanan bawah,
        // prompt interaksi di tengah bawah). Bergeser ke kiri jika panel F2 terbuka.
        float panelOffset = showPanel ? 440f * scale : 0f;
        Rect rect = new Rect(Screen.width - w - 16f * scale - panelOffset, (Screen.height - h) * 0.5f, w, h);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(rect.x - 6f, rect.y - 6f, rect.width + 12f, rect.height + 40f * scale), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Texture: y ke atas = z dunia ke atas.
        GUI.DrawTexture(rect, mapTexture);

        Marker(rect, cell, layout, LevelLayout.CellToWorld(layout.playerStart), new Color(0.3f, 0.6f, 1f), 0.6f);
        Marker(rect, cell, layout, LevelLayout.CellToWorld(layout.keycard), new Color(1f, 0.85f, 0.2f), 0.5f);
        Marker(rect, cell, layout, LevelLayout.CellToWorld(layout.terminal.cell), new Color(0.3f, 1f, 1f), 0.5f);
        Marker(rect, cell, layout, LevelLayout.CellToWorld(layout.extraction), new Color(0.2f, 1f, 0.4f), 0.7f);

        foreach (LayoutVent v in layout.vents)
        {
            Color vc = v.hazard == VentHazardType.Toxic ? new Color(0.5f, 1f, 0.2f) : v.hazard == VentHazardType.Laser ? new Color(1f, 0.25f, 0.2f) : new Color(0.75f, 0.5f, 1f);
            Marker(rect, cell, layout, LevelLayout.CellToWorld(v.cellA), vc, 0.45f);
            Marker(rect, cell, layout, LevelLayout.CellToWorld(v.cellB), vc, 0.45f);
        }

        foreach (EnemyBTController guard in Builder.Guards)
            if (guard != null) Marker(rect, cell, layout, guard.transform.position, guard.GetStateColor(), 0.45f);

        PlayerHealth player = FindPlayerHealth();
        if (player != null) Marker(rect, cell, layout, player.transform.position, Color.white, 0.4f);

        GUI.Label(new Rect(rect.x, rect.yMax + 2f, rect.width, 40f * scale),
            "<color=#9cff9c>hijau</color>=jalur stealth tervalidasi  <color=#ff7b6b>merah</color>=area diawasi\n" +
            "<color=#4d99ff>start</color> <color=#ffd933>keycard</color> <color=#4dffff>terminal</color> <color=#33ff66>ekstraksi</color> <color=#bf80ff>ventilasi</color>  titik=penjaga", small);
    }

    private static void Marker(Rect rect, float cell, LevelLayout layout, Vector3 world, Color color, float size)
    {
        float x = rect.x + world.x / LevelLayout.CellSize * cell;
        float y = rect.yMax - world.z / LevelLayout.CellSize * cell;
        float s = Mathf.Max(4f, cell * size);
        GUI.color = color;
        GUI.DrawTexture(new Rect(x - s * 0.5f, y - s * 0.5f, s, s), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}

// Digambar di depan HUD (depth -5) agar tombol tingkat kesulitan tidak tertutup lapisan briefing/pause.
public class AdaptiveDifficultySelector : MonoBehaviour
{
    private AdaptiveDebugOverlay overlay;

    public void Init(AdaptiveDebugOverlay owner)
    {
        overlay = owner;
    }

    private void OnGUI()
    {
        if (overlay == null || !overlay.IsMenuState)
            return;

        GUI.depth = -5;
        overlay.DrawDifficultySelector();
    }
}
