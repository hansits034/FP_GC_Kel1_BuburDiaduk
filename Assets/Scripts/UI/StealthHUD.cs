using System.Collections.Generic;
using UnityEngine;

// HUD seluruh game (IMGUI): misi, penanda tujuan, inventory, prompt interaksi,
// postur & suara Player, status alarm fasilitas, notifikasi, menu pause, layar menang/kalah.
public class StealthHUD : MonoBehaviour
{
    private struct ToastMessage
    {
        public string text;
        public float time;
    }

    [SerializeField] private float hudFadeDuration = 0.8f;
    [SerializeField] private float toastDuration = 3.2f;

    private static StealthHUD instance;

    private readonly List<ToastMessage> toasts = new List<ToastMessage>();

    private PlayerController player;
    private PlayerInteractor interactor;
    private PlayerInventory inventory;

    private bool settingsOpen;
    private float alertFlash;
    private float briefingStartTime;
    private float hudShowTime = float.NegativeInfinity;

    private string tipTitle;
    private string tipBody;
    private float tipShowTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    public static void Toast(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (instance == null)
        {
            Debug.Log(text);
            return;
        }

        instance.toasts.Add(new ToastMessage { text = text, time = Time.unscaledTime });

        if (instance.toasts.Count > 4)
            instance.toasts.RemoveAt(0);
    }

    // Jendela instruksi (level pelatihan): menjeda game sampai pemain menekan MENGERTI.
    public static void ShowTip(string title, string body)
    {
        if (instance == null || StealthGameManager.Instance == null)
        {
            Toast(title);
            return;
        }

        instance.tipTitle = title;
        instance.tipBody = body;
        instance.tipShowTime = Time.unscaledTime;
        StealthGameManager.Instance.ShowTip();
    }

    // Dipanggil saat Esc ditekan: tutup sub-menu dulu sebelum unpause.
    public static bool TryCloseSubmenu()
    {
        if (instance == null || !instance.settingsOpen)
            return false;

        instance.settingsOpen = false;
        GameSettings.Save();
        return true;
    }

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Start()
    {
        briefingStartTime = Time.unscaledTime;
        player = FindFirstObjectByType<PlayerController>();

        if (player != null)
        {
            interactor = player.GetComponent<PlayerInteractor>();
            inventory = player.GetComponent<PlayerInventory>();
        }

        if (AlertManager.Current != null)
            AlertManager.Current.PlayerSpotted += () => alertFlash = 1f;
    }

    private void Update()
    {
        alertFlash = Mathf.MoveTowards(alertFlash, 0f, Time.unscaledDeltaTime * 1.5f);
    }

    // ==================================================
    // DRAW
    // ==================================================

    private void OnGUI()
    {
        UIStyles.Begin();

        StealthGameState state = StealthGameManager.Instance != null
            ? StealthGameManager.Instance.State
            : StealthGameState.Playing;

        if (state == StealthGameState.Briefing)
        {
            DrawBriefing();
            return;
        }

        if (state == StealthGameState.Tip)
        {
            DrawTip();
            return;
        }

        DrawAlertVignette();

        if (state == StealthGameState.Playing)
        {
            settingsOpen = false;

            float fade = Mathf.Clamp01((Time.unscaledTime - hudShowTime) / hudFadeDuration);
            DrawLetterbox(1f - fade);
            GUI.color = new Color(1f, 1f, 1f, fade);

            DrawWaypoint();
            DrawMissionPanel();
            DrawAlertStatus();
            DrawInventory();
            DrawStatus();
            DrawPrompt();
            DrawControls();
            DrawToasts();

            GUI.color = Color.white;
        }
        else
        {
            DrawOverlay(state);
        }
    }

    private static float S(float v) => UIStyles.S(v);

    // ---------------- Misi ----------------

    private void DrawMissionPanel()
    {
        MissionManager mission = MissionManager.Instance;

        if (mission == null)
            return;

        float x = S(24f);
        float y = S(24f);
        float width = S(440f);
        float pad = S(18f);
        float iconSize = S(20f);
        float textX = pad + iconSize + S(12f);
        float textW = width - textX - pad;

        MissionManager.MissionStep current = mission.CurrentStep;

        // Hitung tinggi dari teks yang benar-benar ditampilkan (termasuk label OPSIONAL).
        float height = pad + S(26f) + S(36f);
        foreach (MissionManager.MissionStep step in mission.Steps)
            height += UIStyles.TextHeight(StepText(step, current), UIStyles.Text, textW) + S(10f);
        height += pad - S(4f);

        Rect panel = new Rect(x, y, width, height);
        UIStyles.DrawPanel(panel, UIStyles.Accent);

        float cy = y + pad;
        GUI.Label(new Rect(x + pad, cy, textW + textX, S(24f)), "MISI AKTIF", UIStyles.Header);
        cy += S(24f);
        UIStyles.FitLabel(new Rect(x + pad, cy, width - pad * 2f, S(34f)), mission.LevelTitle, UIStyles.Title, TextAnchor.MiddleLeft, 0.6f);
        cy += S(38f);

        float lineH = UIStyles.TextHeight("A", UIStyles.Text, textW);

        foreach (MissionManager.MissionStep step in mission.Steps)
        {
            string text = StepText(step, current);
            float lh = UIStyles.TextHeight(text, UIStyles.Text, textW);
            Rect iconRect = new Rect(x + pad, cy + (lineH - iconSize) * 0.5f, iconSize, iconSize);

            if (step.completed)
            {
                UIStyles.DrawIcon(iconRect, "check", UIStyles.Success);
            }
            else if (step == current)
            {
                UIStyles.DrawStyle(new Rect(x + S(8f), cy - S(4f), width - S(16f), lh + S(8f)), UIStyles.Bar, new Color(1f, 0.79f, 0.29f, 0.14f));
                float pulse = 0.75f + Mathf.Sin(Time.unscaledTime * 4f) * 0.25f;
                UIStyles.DrawIcon(iconRect, "diamond", new Color(1f, 0.79f, 0.29f, pulse));
            }
            else
            {
                UIStyles.DrawIcon(iconRect, "ring", step.optional ? UIStyles.Info : UIStyles.Muted);
            }

            UIStyles.Label(new Rect(x + textX, cy, textW, lh), text, UIStyles.Text, TextAnchor.UpperLeft, true);
            cy += lh + S(10f);
        }
    }

    private static string StepText(MissionManager.MissionStep step, MissionManager.MissionStep current)
    {
        if (step.completed)
            return "<color=#7d8794>" + step.description + "</color>";

        if (step == current)
            return "<color=#ffd36a>" + step.description + "</color>";

        if (step.optional)
            return "<color=#8fd6ff>" + step.description + "</color>  <size=" + UIStyles.Px(15) + "><color=#5f9fc0>OPSIONAL</color></size>";

        return "<color=#c4cad4>" + step.description + "</color>";
    }

    // ---------------- Penanda tujuan ----------------

    private void DrawWaypoint()
    {
        MissionManager mission = MissionManager.Instance;
        Camera cam = Camera.main;

        if (mission == null || cam == null || player == null)
            return;

        MissionManager.MissionStep step = mission.CurrentStep;

        if (step == null || step.target == null)
            return;

        Vector3 screen = cam.WorldToScreenPoint(step.target.position + Vector3.up * 1.4f);
        float distance = Vector3.Distance(player.transform.position, step.target.position);
        string distanceText = Mathf.RoundToInt(distance) + " M";

        Vector2 point = new Vector2(screen.x, Screen.height - screen.y);
        float margin = S(70f);

        bool onScreen =
            screen.z > 0f &&
            point.x > margin && point.x < Screen.width - margin &&
            point.y > margin && point.y < Screen.height - margin;

        float pulse = 0.8f + Mathf.Sin(Time.unscaledTime * 4f) * 0.2f;
        Color color = new Color(1f, 0.79f, 0.29f, pulse);
        float size = S(30f);

        if (onScreen)
        {
            float bob = Mathf.Sin(Time.unscaledTime * 3f) * S(4f);
            UIStyles.DrawIcon(new Rect(point.x - size * 0.5f, point.y - size - S(10f) + bob, size, size), "diamond", color);
            DrawTag(new Vector2(point.x, point.y + S(6f) + bob), distanceText);
            return;
        }

        // Di luar layar: panah di tepi layar.
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 direction = point - center;

        if (screen.z < 0f)
            direction = -direction;

        if (direction.sqrMagnitude < 0.01f)
            direction = Vector2.up;

        direction.Normalize();

        float halfW = Screen.width * 0.5f - margin;
        float halfH = Screen.height * 0.5f - margin;
        float t = Mathf.Min(
            Mathf.Abs(direction.x) > 0.001f ? halfW / Mathf.Abs(direction.x) : float.MaxValue,
            Mathf.Abs(direction.y) > 0.001f ? halfH / Mathf.Abs(direction.y) : float.MaxValue);

        Vector2 edge = center + direction * t;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        Matrix4x4 matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, edge);
        UIStyles.DrawIcon(new Rect(edge.x - size * 0.5f, edge.y - size * 0.5f, size, size), "arrow", color);
        GUI.matrix = matrix;

        DrawTag(edge - direction * S(42f), distanceText);
    }

    private static void DrawTag(Vector2 centerTop, string text)
    {
        GUIContent content = new GUIContent(text);
        float w = UIStyles.SmallCenter.CalcSize(content).x + S(18f);
        Rect rect = new Rect(centerTop.x - w * 0.5f, centerTop.y - S(12f), w, S(24f));
        UIStyles.DrawStyle(rect, UIStyles.Pill, new Color(0.05f, 0.07f, 0.1f, 0.85f));
        GUI.Label(rect, "<color=#ffd36a>" + text + "</color>", UIStyles.SmallCenter);
    }

    // ---------------- Status alarm ----------------

    private void DrawAlertStatus()
    {
        AlertManager alert = AlertManager.Current;
        FacilityAlertLevel level = alert != null ? alert.Level : FacilityAlertLevel.Calm;

        string text;
        Color color;
        string icon;

        switch (level)
        {
            case FacilityAlertLevel.Alert:
                text = "BAHAYA  -  PENYUSUP TERDETEKSI";
                color = UIStyles.Danger;
                icon = "alert";
                break;
            case FacilityAlertLevel.Caution:
                text = "WASPADA  -  PENJAGA MENCARI";
                color = UIStyles.Warning;
                icon = "eye";
                break;
            default:
                text = "AMAN  -  TIDAK TERDETEKSI";
                color = UIStyles.Success;
                icon = "eye";
                break;
        }

        GUIContent content = new GUIContent(text);
        float width = UIStyles.Text.CalcSize(content).x + S(80f);
        float height = S(42f);
        Rect rect = new Rect((Screen.width - width) * 0.5f, S(22f), width, height);

        UIStyles.DrawStyle(rect, UIStyles.Pill, new Color(0.04f, 0.06f, 0.09f, 0.85f));
        UIStyles.DrawStyle(new Rect(rect.x, rect.y, rect.width, rect.height), UIStyles.Pill,
            new Color(color.r, color.g, color.b, 0.18f + alertFlash * 0.4f));

        float iconSize = S(22f);
        UIStyles.DrawIcon(new Rect(rect.x + S(18f), rect.y + (height - iconSize) * 0.5f, iconSize, iconSize), icon, color);
        UIStyles.Label(new Rect(rect.x + S(48f), rect.y, rect.width - S(60f), height), "<color=" + UIStyles.Hex(color) + ">" + text + "</color>", UIStyles.Text, TextAnchor.MiddleLeft);

        if (alert != null && alert.DetectionCount > 0)
        {
            Rect sub = new Rect(rect.x, rect.yMax + S(4f), rect.width, S(22f));
            GUI.Label(sub, "<color=#9aa3b0>Terdeteksi " + alert.DetectionCount + "x</color>", UIStyles.SmallCenter);
        }
    }

    private void DrawAlertVignette()
    {
        AlertManager alert = AlertManager.Current;
        Texture2D vignette = UIStyles.Tex("vignette");

        if (vignette == null)
            return;

        float strength = alertFlash * 0.8f;

        if (alert != null && alert.Level == FacilityAlertLevel.Alert)
            strength = Mathf.Max(strength, 0.45f + Mathf.Sin(Time.unscaledTime * 6f) * 0.15f);

        if (strength <= 0.01f)
            return;

        Color previous = GUI.color;
        GUI.color = new Color(1f, 0.1f, 0.08f, strength);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
        GUI.color = previous;
    }

    // ---------------- Inventory ----------------

    private void DrawInventory()
    {
        if (inventory == null)
            return;

        float slot = S(76f);
        float gap = S(10f);
        float x = S(24f);
        float y = Screen.height - slot - S(28f);

        GUI.Label(new Rect(x, y - S(28f), S(300f), S(24f)), "INVENTORY", UIStyles.Header);

        int count = Mathf.Max(inventory.Items.Count, 1);

        for (int i = 0; i < Mathf.Max(count, 4); i++)
        {
            Rect rect = new Rect(x + i * (slot + gap), y, slot, slot);
            UIStyles.DrawStyle(rect, UIStyles.Slot, Color.white);

            if (i >= inventory.Items.Count)
                continue;

            PlayerInventory.Entry entry = inventory.Items[i];
            float iconSize = slot * 0.55f;
            UIStyles.DrawIcon(new Rect(rect.x + (slot - iconSize) * 0.5f, rect.y + S(8f), iconSize, iconSize), ItemIcon(entry.id), entry.color);

            UIStyles.FitLabel(new Rect(rect.x + S(4f), rect.yMax - S(22f), slot - S(8f), S(18f)), entry.displayName, UIStyles.Small, TextAnchor.MiddleCenter, 0.55f);

            if (entry.count > 1)
            {
                Rect badge = new Rect(rect.xMax - S(26f), rect.y - S(8f), S(30f), S(24f));
                UIStyles.DrawStyle(badge, UIStyles.Pill, UIStyles.Accent);
                GUIStyle badgeStyle = new GUIStyle(UIStyles.SmallCenter) { font = UIStyles.Header.font };
                badgeStyle.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
                GUI.Label(badge, "x" + entry.count, badgeStyle);
            }
        }

        if (interactor != null && inventory.Has(interactor.ThrowableItemId))
        {
            float hx = x;
            float hy = y + slot + S(4f);
            float kw = UIStyles.DrawKeyCap(hx, hy, S(20f), "KLIK KIRI");
            UIStyles.Label(new Rect(hx + kw + S(8f), hy, S(320f), S(20f)), "<color=#8fd6ff>lempar kaleng ke arah mouse</color>", UIStyles.Small, TextAnchor.MiddleLeft);
        }
    }

    private static string ItemIcon(string id)
    {
        if (id.StartsWith("keycard")) return "keycard";
        if (id == "screwdriver") return "screwdriver";
        if (id == "can") return "can";
        if (id == "prototype") return "prototype";
        return "dot";
    }

    // ---------------- Status Player ----------------

    private void DrawStatus()
    {
        if (player == null)
            return;

        float width = S(300f);
        float height = S(124f);
        Rect rect = new Rect(Screen.width - width - S(24f), Screen.height - height - S(28f), width, height);
        UIStyles.DrawPanel(rect, new Color(0, 0, 0, 0));

        string stanceIcon;
        string stanceText;
        Color stanceColor;

        if (player.IsDashing) { stanceIcon = "run"; stanceText = "BERGULING"; stanceColor = UIStyles.Info; }
        else if (player.IsRunning) { stanceIcon = "run"; stanceText = "LARI"; stanceColor = UIStyles.Warning; }
        else if (player.IsCrouching) { stanceIcon = "crouch"; stanceText = "JONGKOK"; stanceColor = UIStyles.Success; }
        else { stanceIcon = "stand"; stanceText = "BERDIRI"; stanceColor = Color.white; }

        float pad = S(16f);
        float big = S(64f);
        UIStyles.DrawStyle(new Rect(rect.x + pad, rect.y + (height - big - S(16f)) * 0.5f, big + S(16f), big + S(16f)), UIStyles.Slot, Color.white);
        UIStyles.DrawIcon(new Rect(rect.x + pad + S(8f), rect.y + (height - big) * 0.5f, big, big), stanceIcon, stanceColor);

        float cx = rect.x + pad + big + S(30f);
        float cw = rect.xMax - cx - pad;

        UIStyles.FitLabel(new Rect(cx, rect.y + S(12f), cw, S(34f)), "<color=" + UIStyles.Hex(stanceColor) + ">" + stanceText + "</color>", UIStyles.Title, TextAnchor.MiddleLeft, 0.6f);

        // Suara
        float noise = player.MaxNoiseRadius > 0f ? Mathf.Clamp01(player.CurrentNoiseRadius / player.MaxNoiseRadius) : 0f;
        float iy = rect.y + S(56f);
        UIStyles.DrawIcon(new Rect(cx, iy, S(20f), S(20f)), "sound", UIStyles.Muted);
        UIStyles.DrawBar(new Rect(cx + S(28f), iy + S(6f), cw - S(28f), S(8f)), noise,
            Color.Lerp(UIStyles.Success, UIStyles.Danger, noise));

        // Cahaya
        bool dark = LightZone.GetVisibility(player.VisibilityPoint) < 1f;
        iy += S(30f);
        UIStyles.DrawIcon(new Rect(cx, iy, S(20f), S(20f)), "bulb", dark ? UIStyles.Info : UIStyles.Accent);
        UIStyles.FitLabel(new Rect(cx + S(28f), iy - S(2f), cw - S(28f), S(24f)),
            dark ? "<color=#8fd6ff>Gelap - sulit terlihat</color>" : "<color=#c4cad4>Area terang</color>", UIStyles.Small, TextAnchor.MiddleLeft, 0.7f);
    }

    // ---------------- Prompt interaksi ----------------

    private void DrawPrompt()
    {
        if (interactor == null || interactor.Current == null || inventory == null)
            return;

        string prompt = interactor.Current.GetPrompt(inventory);
        string key = null;

        // Pisahkan "[E]" / "[Tahan E]" menjadi keycap.
        if (prompt.StartsWith("["))
        {
            int close = prompt.IndexOf(']');
            if (close > 0)
            {
                key = prompt.Substring(1, close - 1).ToUpper();
                prompt = prompt.Substring(close + 1).Trim();
            }
        }

        bool blocked = key == null;
        GUIContent content = new GUIContent(prompt);
        float textW = UIStyles.Text.CalcSize(content).x;
        float keyW = key != null ? UIStyles.SmallCenter.CalcSize(new GUIContent(key)).x + S(16f) : 0f;
        float height = S(52f);
        float width = textW + keyW + S(blocked ? 78f : 56f);

        Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height - S(170f), width, height);
        UIStyles.DrawStyle(rect, UIStyles.Pill, new Color(0.04f, 0.06f, 0.09f, 0.9f));

        float x = rect.x + S(18f);

        if (key != null)
            x += UIStyles.DrawKeyCap(x, rect.y + S(12f), S(28f), key) + S(12f);
        else
        {
            UIStyles.DrawIcon(new Rect(x, rect.y + S(14f), S(24f), S(24f)), "lock", UIStyles.Danger);
            x += S(34f);
        }

        GUI.Label(new Rect(x, rect.y, textW + S(10f), height), prompt, new GUIStyle(UIStyles.Text) { alignment = TextAnchor.MiddleLeft, wordWrap = false });

        if (interactor.HoldProgress > 0f)
            UIStyles.DrawBar(new Rect(rect.x + S(20f), rect.yMax + S(6f), rect.width - S(40f), S(8f)), interactor.HoldProgress, UIStyles.Accent);
    }

    // ---------------- Bantuan kontrol ----------------

    private void DrawControls()
    {
        if (!GameSettings.ShowControls)
            return;

        string[,] rows =
        {
            { "WASD", "Gerak" },
            { "SHIFT", "Lari (berisik)" },
            { "CTRL", "Jongkok (senyap)" },
            { "SPACE", "Berguling" },
            { "E", "Interaksi" },
            { "ESC", "Menu" }
        };

        float rowH = S(30f);
        float width = S(230f);
        float height = rows.GetLength(0) * rowH + S(24f);
        Rect rect = new Rect(Screen.width - width - S(24f), S(24f), width, height);
        UIStyles.DrawStyle(rect, UIStyles.Panel, new Color(1f, 1f, 1f, 0.75f));

        float y = rect.y + S(12f);

        for (int i = 0; i < rows.GetLength(0); i++)
        {
            float kw = UIStyles.DrawKeyCap(rect.x + S(14f), y + S(3f), S(24f), rows[i, 0]);
            UIStyles.Label(new Rect(rect.x + S(14f) + kw + S(10f), y + S(3f), width - kw - S(30f), S(24f)), "<color=#c4cad4>" + rows[i, 1] + "</color>", UIStyles.Small, TextAnchor.MiddleLeft);
            y += rowH;
        }
    }

    // ---------------- Notifikasi ----------------

    private void DrawToasts()
    {
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime - toasts[i].time > toastDuration)
                toasts.RemoveAt(i);
        }

        float y = S(110f);
        Color hud = GUI.color;

        foreach (ToastMessage toast in toasts)
        {
            float age = Time.unscaledTime - toast.time;
            float alpha = Mathf.Clamp01((toastDuration - age) / 0.5f);
            float slide = Mathf.Clamp01(age / 0.18f);

            GUIContent content = new GUIContent(toast.text);
            float width = UIStyles.Text.CalcSize(content).x + S(60f);
            Rect rect = new Rect((Screen.width - width) * 0.5f, y - (1f - slide) * S(14f), width, S(40f));

            UIStyles.DrawStyle(rect, UIStyles.Pill, new Color(0.04f, 0.06f, 0.09f, 0.88f * alpha));
            UIStyles.DrawIcon(new Rect(rect.x + S(14f), rect.y + S(11f), S(18f), S(18f)), "diamond", new Color(1f, 0.79f, 0.29f, alpha));

            GUI.color = new Color(1f, 1f, 1f, alpha * hud.a);
            GUI.Label(new Rect(rect.x + S(40f), rect.y, rect.width - S(50f), rect.height), toast.text, new GUIStyle(UIStyles.Text) { alignment = TextAnchor.MiddleLeft, wordWrap = false });
            GUI.color = hud;

            y += S(48f);
        }
    }

    // ---------------- Briefing ----------------

    private void DrawLetterbox(float amount)
    {
        if (amount <= 0f)
            return;

        float bar = S(110f) * amount;
        UIStyles.DrawRect(new Rect(0, 0, Screen.width, bar), Color.black);
        UIStyles.DrawRect(new Rect(0, Screen.height - bar, Screen.width, bar), Color.black);
    }

    private void DrawBriefing()
    {
        MissionManager mission = MissionManager.Instance;

        if (mission == null)
            return;

        float enter = Mathf.Clamp01((Time.unscaledTime - briefingStartTime) / 0.6f);
        DrawLetterbox(enter);

        float width = S(860f);
        float bodyW = width - S(100f);
        string body = "<color=#c4cad4>" + mission.Briefing + "</color>";
        float bodyH = UIStyles.TextHeight(body, UIStyles.Center, bodyW);
        float height = S(116f) + bodyH + S(28f) + S(52f) + S(24f);
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f + (1f - enter) * S(20f), width, height);

        GUI.color = new Color(1f, 1f, 1f, enter);
        UIStyles.DrawPanel(rect, UIStyles.Accent, true);
        UIStyles.Label(new Rect(rect.x, rect.y + S(22f), rect.width, S(24f)), "<color=#ffd36a>BRIEFING MISI</color>", UIStyles.Header, TextAnchor.MiddleCenter);
        UIStyles.FitLabel(new Rect(rect.x + S(30f), rect.y + S(48f), rect.width - S(60f), S(56f)), mission.LevelTitle, UIStyles.BigCenter, TextAnchor.MiddleCenter);
        UIStyles.Label(new Rect(rect.x + S(50f), rect.y + S(116f), bodyW, bodyH), body, UIStyles.Center, TextAnchor.UpperCenter, true);

        // ROGER button, bottom-center of the panel.
        float pad = S(24f);
        float buttonW = S(200f);
        float buttonH = S(52f);

        if (UIStyles.ButtonClicked(new Rect(rect.center.x - buttonW * 0.5f, rect.yMax - pad - buttonH, buttonW, buttonH), "ROGER"))
        {
            hudShowTime = Time.unscaledTime;

            if (StealthGameManager.Instance != null)
                StealthGameManager.Instance.EndBriefing();
        }

        GUI.color = Color.white;
    }

    // ---------------- Instruksi pelatihan ----------------

    private void DrawTip()
    {
        UIStyles.DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.01f, 0.02f, 0.04f, 0.55f));

        float enter = Mathf.Clamp01((Time.unscaledTime - tipShowTime) / 0.25f);
        float width = S(760f);
        float pad = S(44f);
        float bodyH = UIStyles.Text.CalcHeight(new GUIContent(tipBody), width - pad * 2f);
        float buttonH = S(52f);
        float height = S(118f) + bodyH + S(32f) + buttonH + S(30f);
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f + (1f - enter) * S(20f), width, height);

        GUI.color = new Color(1f, 1f, 1f, enter);
        UIStyles.DrawPanel(rect, UIStyles.Info, true);
        GUI.Label(new Rect(rect.x, rect.y + S(24f), rect.width, S(24f)), "<color=#8fd6ff>INSTRUKSI PELATIHAN</color>", new GUIStyle(UIStyles.Header) { alignment = TextAnchor.MiddleCenter });
        UIStyles.FitLabel(new Rect(rect.x + S(30f), rect.y + S(50f), rect.width - S(60f), S(56f)), tipTitle, UIStyles.BigCenter, TextAnchor.MiddleCenter);
        GUI.Label(new Rect(rect.x + pad, rect.y + S(118f), rect.width - pad * 2f, bodyH), "<color=#c4cad4>" + tipBody + "</color>", UIStyles.Text);

        float buttonW = S(240f);
        if (UIStyles.ButtonClicked(new Rect(rect.center.x - buttonW * 0.5f, rect.yMax - S(30f) - buttonH, buttonW, buttonH), "MENGERTI"))
            StealthGameManager.Instance.CloseTip();

        GUI.color = Color.white;
    }

    // ---------------- Pause / menang / kalah ----------------

    private void DrawOverlay(StealthGameState state)
    {
        StealthGameManager game = StealthGameManager.Instance;

        UIStyles.DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.01f, 0.02f, 0.04f, 0.72f));

        if (settingsOpen)
        {
            float sw = S(760f);
            float sh = SettingsPanel.PreferredHeight;

            if (SettingsPanel.Draw(new Rect((Screen.width - sw) * 0.5f, (Screen.height - sh) * 0.5f, sw, sh)))
                settingsOpen = false;

            return;
        }

        bool paused = state == StealthGameState.Paused;
        float width = S(600f);

        string goal = paused && MissionManager.Instance != null && MissionManager.Instance.CurrentStep != null
            ? "<color=#9aa3b0>Tujuan: </color>" + MissionManager.Instance.CurrentStep.description
            : null;
        float goalH = goal != null ? UIStyles.TextHeight(goal, UIStyles.Center, width - S(80f)) : 0f;

        int buttons = state == StealthGameState.Lost ? 3 : 4;
        float content = paused ? (goal != null ? goalH + S(24f) : 0f) : S(3f * 38f + 16f);
        float height = S(130f) + content + buttons * S(62f) + S(24f);
        Rect rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        Color accent =
            state == StealthGameState.Won ? UIStyles.Success :
            state == StealthGameState.Lost ? UIStyles.Danger : UIStyles.Accent;

        UIStyles.DrawPanel(rect, accent, true);

        string kicker =
            state == StealthGameState.Won ? "EKSTRAKSI BERHASIL" :
            state == StealthGameState.Lost ? "OPERASI DIBATALKAN" : "OPERASI DIJEDA";

        string title =
            state == StealthGameState.Won ? "MISI BERHASIL" :
            state == StealthGameState.Lost ? "TERTANGKAP" : "PAUSE";

        UIStyles.Label(new Rect(rect.x, rect.y + S(28f), rect.width, S(24f)), "<color=" + UIStyles.Hex(accent) + ">" + kicker + "</color>", UIStyles.Header, TextAnchor.MiddleCenter);
        UIStyles.FitLabel(new Rect(rect.x + S(30f), rect.y + S(54f), rect.width - S(60f), S(60f)), title, UIStyles.BigCenter, TextAnchor.MiddleCenter);

        float y = rect.y + S(130f);

        if (!paused)
        {
            int seconds = Mathf.FloorToInt(game.ElapsedTime);
            StatRow(rect, ref y, "Waktu", (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"));
            StatRow(rect, ref y, "Terdeteksi", game.Detections + "x");
            StatRow(rect, ref y, "Peringkat", "<color=" + UIStyles.Hex(accent) + ">" + game.Rating + "</color>");
            y += S(16f);
        }
        else if (goal != null)
        {
            UIStyles.Label(new Rect(rect.x + S(40f), y, rect.width - S(80f), goalH), goal, UIStyles.Center, TextAnchor.UpperCenter, true);
            y += goalH + S(24f);
        }

        float buttonW = rect.width - S(120f);
        float buttonH = S(52f);
        float gap = S(10f);
        float bx = rect.x + (rect.width - buttonW) * 0.5f;

        if (paused)
        {
            if (UIStyles.ButtonClicked(new Rect(bx, y, buttonW, buttonH), "LANJUTKAN"))
                game.Resume();
            y += buttonH + gap;
        }
        else if (state == StealthGameState.Won)
        {
            if (UIStyles.ButtonClicked(new Rect(bx, y, buttonW, buttonH), game.HasNextLevel ? "LEVEL BERIKUTNYA" : "SELESAI  -  MENU UTAMA"))
                game.LoadNext();
            y += buttonH + gap;
        }

        if (UIStyles.ButtonClicked(new Rect(bx, y, buttonW, buttonH), state == StealthGameState.Lost ? "COBA LAGI" : "ULANGI LEVEL"))
            game.Restart();
        y += buttonH + gap;

        if (UIStyles.ButtonClicked(new Rect(bx, y, buttonW, buttonH), "PENGATURAN"))
            settingsOpen = true;
        y += buttonH + gap;

        if (UIStyles.ButtonClicked(new Rect(bx, y, buttonW, buttonH), "MENU UTAMA"))
            game.LoadMainMenu();
    }

    private static void StatRow(Rect panel, ref float y, string label, string value)
    {
        float pad = S(50f);
        Rect row = new Rect(panel.x + pad, y, panel.width - pad * 2f, S(34f));
        UIStyles.DrawRect(new Rect(row.x, row.yMax - 1f, row.width, 1f), new Color(1f, 1f, 1f, 0.08f));
        UIStyles.Label(row, "<color=#9aa3b0>" + label + "</color>", UIStyles.Text, TextAnchor.MiddleLeft);

        // Nilai rata kanan, dikecilkan bila terlalu panjang agar tidak menimpa label.
        Rect valueRect = new Rect(row.x + row.width * 0.3f, row.y, row.width * 0.7f, row.height);
        UIStyles.FitLabel(valueRect, value, UIStyles.Text, TextAnchor.MiddleRight, 0.6f);
        y += S(38f);
    }
}
