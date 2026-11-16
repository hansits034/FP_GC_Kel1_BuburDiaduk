using UnityEngine;

// Panel pengaturan (dipakai di menu utama & menu pause).
public static class SettingsPanel
{
    // Tinggi panel yang pas untuk seluruh isi (judul + 7 baris + tombol Kembali).
    public static float PreferredHeight => UIStyles.S(680f);

    // Menggambar panel. Mengembalikan true jika tombol "Kembali" ditekan.
    public static bool Draw(Rect rect)
    {
        UIStyles.DrawPanel(rect, UIStyles.Accent, true);

        float pad = UIStyles.S(44f);
        float rowH = UIStyles.S(50f);
        float labelW = UIStyles.S(240f);
        float x = rect.x + pad;
        float y = rect.y + UIStyles.S(26f);
        float controlW = rect.width - pad * 2f - labelW;

        GUI.Label(new Rect(rect.x, y, rect.width, UIStyles.S(24f)), "<color=#ffd36a>SISTEM</color>", new GUIStyle(UIStyles.Header) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(rect.x, y + UIStyles.S(22f), rect.width, UIStyles.S(56f)), "PENGATURAN", UIStyles.BigCenter);
        y += UIStyles.S(96f);

        Section(ref y, x, rect.width - pad * 2f, "AUDIO");
        GameSettings.MasterVolume = VolumeRow(ref y, x, labelW, controlW, rowH, "Volume Utama", GameSettings.MasterVolume);
        GameSettings.MusicVolume = VolumeRow(ref y, x, labelW, controlW, rowH, "Musik", GameSettings.MusicVolume);

        float sfx = VolumeRow(ref y, x, labelW, controlW, rowH, "Efek Suara", GameSettings.SfxVolume);
        if (!Mathf.Approximately(sfx, GameSettings.SfxVolume))
            GameSettings.SfxVolume = sfx;

        y += UIStyles.S(8f);
        Section(ref y, x, rect.width - pad * 2f, "PERMAINAN & GRAFIS");

        if (ToggleRow(ref y, x, labelW, controlW, rowH, "Jongkok (Ctrl)", GameSettings.CrouchToggle ? "Tekan sekali" : "Tahan tombol"))
            GameSettings.CrouchToggle = !GameSettings.CrouchToggle;

        // Kualitas grafis
        string[] qualities = QualitySettings.names;
        int quality = GameSettings.Quality;
        Label(x, y, labelW, rowH, "Kualitas Grafis");

        float arrowW = UIStyles.S(48f);
        Rect control = new Rect(x + labelW, y + UIStyles.S(6f), controlW, rowH - UIStyles.S(12f));

        if (UIStyles.ButtonClicked(new Rect(control.x, control.y, arrowW, control.height), "<", UIStyles.SmallButton))
            GameSettings.Quality = (quality - 1 + qualities.Length) % qualities.Length;

        GUI.Label(new Rect(control.x + arrowW, control.y, control.width - arrowW * 2f, control.height),
                  qualities.Length > 0 ? qualities[quality].ToUpper() : "-", new GUIStyle(UIStyles.Center) { font = UIStyles.Header.font });

        if (UIStyles.ButtonClicked(new Rect(control.xMax - arrowW, control.y, arrowW, control.height), ">", UIStyles.SmallButton))
            GameSettings.Quality = (quality + 1) % qualities.Length;
        y += rowH;

        if (ToggleRow(ref y, x, labelW, controlW, rowH, "Layar Penuh", Screen.fullScreen ? "Aktif" : "Nonaktif"))
            Screen.fullScreen = !Screen.fullScreen;

        if (ToggleRow(ref y, x, labelW, controlW, rowH, "Bantuan Kontrol", GameSettings.ShowControls ? "Tampilkan" : "Sembunyikan"))
            GameSettings.ShowControls = !GameSettings.ShowControls;

        // Tombol Kembali tepat di bawah baris terakhir (tidak menumpuk isi panel).
        float buttonW = UIStyles.S(260f);
        Rect back = new Rect(rect.x + (rect.width - buttonW) * 0.5f, y + UIStyles.S(24f), buttonW, UIStyles.S(52f));

        if (UIStyles.ButtonClicked(back, "KEMBALI"))
        {
            GameSettings.Save();
            return true;
        }

        return false;
    }

    private static void Section(ref float y, float x, float width, string title)
    {
        GUI.Label(new Rect(x, y, width, UIStyles.S(22f)), title, UIStyles.Header);
        UIStyles.DrawRect(new Rect(x, y + UIStyles.S(24f), width, 1f), new Color(1f, 0.79f, 0.29f, 0.25f));
        y += UIStyles.S(30f);
    }

    private static void Label(float x, float y, float width, float height, string text)
    {
        GUI.Label(new Rect(x, y, width, height), text, new GUIStyle(UIStyles.Text) { alignment = TextAnchor.MiddleLeft });
    }

    private static bool ToggleRow(ref float y, float x, float labelW, float controlW, float rowH, string label, string value)
    {
        Label(x, y, labelW, rowH, label);
        bool clicked = UIStyles.ButtonClicked(new Rect(x + labelW, y + UIStyles.S(6f), controlW, rowH - UIStyles.S(12f)), value.ToUpper(), UIStyles.SmallButton);
        y += rowH;
        return clicked;
    }

    private static float VolumeRow(ref float y, float x, float labelW, float controlW, float rowH, string label, float value)
    {
        Label(x, y, labelW, rowH, label);

        float percentW = UIStyles.S(64f);
        Rect track = new Rect(x + labelW, y + rowH * 0.5f - UIStyles.S(4f), controlW - percentW - UIStyles.S(10f), UIStyles.S(8f));

        // Isi bar di bawah slider agar terlihat progresnya.
        UIStyles.DrawBar(track, value, UIStyles.Accent);
        float result = UIStyles.Slider(new Rect(track.x, track.y - UIStyles.S(7f), track.width, UIStyles.S(22f)), value);

        GUI.Label(new Rect(track.xMax + UIStyles.S(10f), y, percentW, rowH), Mathf.RoundToInt(result * 100f) + "%",
                  new GUIStyle(UIStyles.Text) { alignment = TextAnchor.MiddleRight, font = UIStyles.Header.font });

        y += rowH;
        return result;
    }
}
