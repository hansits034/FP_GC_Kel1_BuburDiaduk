using System.Collections.Generic;
using UnityEngine;

// Style IMGUI bersama: font Rajdhani, panel & tombol bersudut bulat, ikon.
// Tekstur diambil dari Resources/UI, font dari Resources/Fonts.
public static class UIStyles
{
    public static readonly Color Accent = new Color(1f, 0.79f, 0.29f);
    public static readonly Color Success = new Color(0.36f, 0.88f, 0.5f);
    public static readonly Color Danger = new Color(1f, 0.3f, 0.28f);
    public static readonly Color Warning = new Color(1f, 0.58f, 0.15f);
    public static readonly Color Info = new Color(0.45f, 0.82f, 1f);
    public static readonly Color Muted = new Color(0.62f, 0.67f, 0.75f);
    public static readonly Color PanelTint = new Color(1f, 1f, 1f, 1f);

    public static float Scale { get; private set; } = 1f;

    public static GUIStyle Title { get; private set; }
    public static GUIStyle Header { get; private set; }
    public static GUIStyle Text { get; private set; }
    public static GUIStyle Small { get; private set; }
    public static GUIStyle Center { get; private set; }
    public static GUIStyle SmallCenter { get; private set; }
    public static GUIStyle BigCenter { get; private set; }
    public static GUIStyle Huge { get; private set; }
    public static GUIStyle Button { get; private set; }
    public static GUIStyle SmallButton { get; private set; }
    public static GUIStyle Panel { get; private set; }
    public static GUIStyle PanelSolid { get; private set; }
    public static GUIStyle Slot { get; private set; }
    public static GUIStyle Pill { get; private set; }
    public static GUIStyle Bar { get; private set; }
    public static GUIStyle SliderTrack { get; private set; }
    public static GUIStyle SliderThumb { get; private set; }

    private static float builtScale = -1f;
    private static readonly Dictionary<string, GUIStyle> variants = new Dictionary<string, GUIStyle>();
    private static Font fontRegular;
    private static Font fontBold;
    private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

    // Panggil di awal setiap OnGUI.
    public static void Begin()
    {
        Scale = Mathf.Max(0.6f, Screen.height / 1080f);

        if (Mathf.Approximately(builtScale, Scale) && Text != null && Panel != null && Panel.normal.background != null)
            return;

        builtScale = Scale;
        variants.Clear();

        if (fontRegular == null) fontRegular = Resources.Load<Font>("Fonts/Rajdhani-SemiBold");
        if (fontBold == null) fontBold = Resources.Load<Font>("Fonts/Rajdhani-Bold");

        Text = new GUIStyle(GUI.skin.label)
        {
            font = fontRegular,
            fontSize = Px(21),
            richText = true,
            wordWrap = true,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };
        Text.normal.textColor = new Color(0.92f, 0.94f, 0.97f);

        Small = new GUIStyle(Text) { fontSize = Px(17) };
        Header = new GUIStyle(Text) { font = fontBold, fontSize = Px(17) };
        Header.normal.textColor = Accent;
        Title = new GUIStyle(Text) { font = fontBold, fontSize = Px(28) };
        Center = new GUIStyle(Text) { alignment = TextAnchor.MiddleCenter };
        SmallCenter = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter };
        BigCenter = new GUIStyle(Center) { font = fontBold, fontSize = Px(46) };
        Huge = new GUIStyle(Text) { font = fontBold, fontSize = Px(96), wordWrap = false };

        Panel = NineSlice("panel", 14);
        PanelSolid = NineSlice("panel_solid", 14);
        Slot = NineSlice("slot", 12);
        Pill = NineSlice("pill", 15);
        Bar = NineSlice("bar", 6);

        Button = new GUIStyle(GUI.skin.button)
        {
            font = fontBold,
            fontSize = Px(22),
            alignment = TextAnchor.MiddleCenter,
            richText = true,
            border = new RectOffset(10, 10, 10, 10),
            padding = new RectOffset(Px(16), Px(16), Px(6), Px(6))
        };
        Button.normal.background = Tex("button");
        Button.hover.background = Tex("button_hover");
        Button.active.background = Tex("button_active");
        Button.focused.background = Tex("button");
        Button.onNormal.background = Tex("button_hover");
        Button.normal.textColor = new Color(0.92f, 0.94f, 0.97f);
        Button.hover.textColor = Accent;
        Button.active.textColor = new Color(0.08f, 0.08f, 0.1f);
        Button.focused.textColor = Button.normal.textColor;

        SmallButton = new GUIStyle(Button) { fontSize = Px(18) };

        SliderTrack = new GUIStyle(GUI.skin.horizontalSlider)
        {
            border = new RectOffset(6, 6, 6, 6),
            fixedHeight = Px(8)
        };
        // Track transparan: isi bar digambar terpisah dengan DrawBar.
        SliderTrack.normal.background = null;

        SliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb)
        {
            fixedWidth = Px(22),
            fixedHeight = Px(22)
        };
        SliderThumb.normal.background = Tex("pill");
        SliderThumb.hover.background = Tex("pill");
        SliderThumb.active.background = Tex("pill");
    }

    public static int Px(float size)
    {
        return Mathf.RoundToInt(size * Scale);
    }

    public static float S(float value)
    {
        return value * Scale;
    }

    public static Texture2D Tex(string name)
    {
        if (textures.TryGetValue(name, out Texture2D cached) && cached != null)
            return cached;

        Texture2D texture = Resources.Load<Texture2D>("UI/" + name);
        textures[name] = texture;
        return texture;
    }

    private static GUIStyle NineSlice(string texture, int border)
    {
        GUIStyle style = new GUIStyle
        {
            border = new RectOffset(border, border, border, border)
        };
        style.normal.background = Tex(texture);
        return style;
    }

    // ==================================================
    // DRAW HELPERS
    // ==================================================

    public static void DrawRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        color.a *= previous.a; // respect an outer fade (GUI.color alpha)
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    public static void DrawStyle(Rect rect, GUIStyle style, Color tint)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        Color previous = GUI.color;
        tint.a *= previous.a;
        GUI.color = tint;
        style.Draw(rect, GUIContent.none, false, false, false, false);
        GUI.color = previous;
    }

    public static void DrawPanel(Rect rect, Color accent, bool solid = false)
    {
        DrawStyle(rect, solid ? PanelSolid : Panel, Color.white);

        if (accent.a > 0f)
            DrawStyle(new Rect(rect.x + S(14f), rect.y, rect.width - S(28f), S(3f)), Bar, accent);
    }

    public static void DrawIcon(Rect rect, string icon, Color color)
    {
        Texture2D texture = Tex("icon_" + icon);

        if (texture == null)
            return;

        Color previous = GUI.color;
        color.a *= previous.a;
        GUI.color = color;
        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
        GUI.color = previous;
    }

    public static void DrawBar(Rect rect, float fill, Color color)
    {
        DrawStyle(rect, Bar, new Color(1f, 1f, 1f, 0.12f));

        if (fill > 0.001f)
            DrawStyle(new Rect(rect.x, rect.y, Mathf.Max(rect.height, rect.width * Mathf.Clamp01(fill)), rect.height), Bar, color);
    }

    // Kotak tombol keyboard, misal [E].
    public static float DrawKeyCap(float x, float y, float height, string key)
    {
        GUIContent content = new GUIContent(key);
        GUIStyle style = new GUIStyle(SmallCenter) { font = Header.font };

        Color textColor = new Color(0.08f, 0.08f, 0.1f);
        style.normal.textColor = textColor;
        style.hover.textColor = textColor;
        style.active.textColor = textColor;

        float width = Mathf.Max(height, style.CalcSize(content).x + S(16f));
        Rect rect = new Rect(x, y, width, height);
        DrawStyle(rect, Bar, Accent);
        GUI.Label(rect, key, style);
        return width;
    }

    // Tombol dengan bunyi klik.
    public static bool ButtonClicked(Rect rect, string label, GUIStyle style = null)
    {
        if (GUI.Button(rect, label, style ?? Button))
        {
            SoundManager.Play("ui_click", 0.8f, 0f);
            return true;
        }

        return false;
    }

    public static float Slider(Rect rect, float value)
    {
        return GUI.HorizontalSlider(rect, value, 0f, 1f, SliderTrack, SliderThumb);
    }

    // ==================================================
    // TEXT HELPERS
    // ==================================================

    // Varian style dengan alignment / ukuran / wrap tertentu (di-cache, tanpa alokasi tiap frame).
    public static GUIStyle Variant(GUIStyle style, TextAnchor anchor, int fontSize, bool wrap)
    {
        string key = style.GetHashCode() + "|" + (int)anchor + "|" + fontSize + "|" + wrap;

        if (!variants.TryGetValue(key, out GUIStyle variant))
        {
            variant = new GUIStyle(style) { alignment = anchor, fontSize = fontSize, wordWrap = wrap, clipping = TextClipping.Overflow };
            variants[key] = variant;
        }

        return variant;
    }

    // Label dengan alignment tertentu (mis. MiddleLeft agar sejajar tengah dengan ikon/kotak).
    public static void Label(Rect rect, string text, GUIStyle style, TextAnchor anchor, bool wrap = false)
    {
        GUI.Label(rect, text, Variant(style, anchor, style.fontSize, wrap));
    }

    // Label satu baris yang ukuran font-nya dikecilkan otomatis sampai muat di dalam rect.
    public static void FitLabel(Rect rect, string text, GUIStyle style, TextAnchor anchor, float minScale = 0.5f)
    {
        GUI.Label(rect, text, FitStyle(rect, text, style, anchor, minScale));
    }

    public static GUIStyle FitStyle(Rect rect, string text, GUIStyle style, TextAnchor anchor, float minScale = 0.5f)
    {
        GUIContent content = new GUIContent(text);
        int size = style.fontSize;
        int min = Mathf.Max(8, Mathf.RoundToInt(style.fontSize * minScale));
        GUIStyle fitted = Variant(style, anchor, size, false);

        while (size > min)
        {
            Vector2 needed = fitted.CalcSize(content);

            if (needed.x <= rect.width && needed.y <= rect.height * 1.2f)
                break;

            size--;
            fitted = Variant(style, anchor, size, false);
        }

        return fitted;
    }

    // Tinggi teks ber-wrap pada lebar tertentu.
    public static float TextHeight(string text, GUIStyle style, float width)
    {
        return Variant(style, style.alignment, style.fontSize, true).CalcHeight(new GUIContent(text), width);
    }

    public static string Hex(Color color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(color);
    }
}
