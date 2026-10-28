using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace KareNot;

// Palette, fonts, icon glyphs and window helpers of the dark UI (ui-konsept): warm near-black canvas, two surface levels,
// thin borders, one orange accent; note kinds keep their colors (BUG red, DEĞİŞİKLİK amber, NOT blue).
static class Theme
{
    public static readonly Color Canvas = Rgb(0x141517), Surface = Rgb(0x1C1E21), Raised = Rgb(0x24262A), Border = Rgb(0x34363B),
        Text = Rgb(0xF3F0EA), Muted = Rgb(0xADB0B8), Faint = Rgb(0x7D818A), Accent = Rgb(0xFF9D24), OnAccent = Rgb(0x1B1406),
        Field = Rgb(0x17191B), Ok = Rgb(0x4CC27A), Error = Rgb(0xF0565C), Warn = Rgb(0xF2B33D);
    public static readonly Color SelectedRow = Mix(Raised, Accent, 0.15f), Divider = Mix(Raised, Border, 0.6f);

    public static Color Rgb(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // UI colors of the note kinds (Kinds.Color stays for the reports).
    public static Color Kind(string kind) => kind switch
    {
        "BUG" => Rgb(0xF0565C),
        "DEGISIKLIK" => Rgb(0xF2B33D),
        "NOT" => Rgb(0x4C9AFF),
        "BIP" => Rgb(0xB48CF2),
        "KES_BASLA" => Rgb(0x46C07A),
        "KES_BITIR" => Rgb(0x34A865),
        _ => Muted,
    };

    // ---------- Fonts ----------

    static string Family(params string[] names)
    {
        foreach (var n in names)
            try { using var f = new FontFamily(n); return n; } catch { }
        return null;
    }

    static readonly string UiFamily = Family("Segoe UI Variable Text", "Segoe UI") ?? "Segoe UI";
    static readonly string SemiFamily = Family("Segoe UI Variable Text Semibold", "Segoe UI Semibold") ?? "Segoe UI";
    static readonly string MonoFamily = Family("Cascadia Mono", "Consolas") ?? "Courier New";
    static readonly string IconFamily = Family("Segoe Fluent Icons", "Segoe MDL2 Assets");
    public static bool HasIcons => IconFamily != null;

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new(UiFamily, size, style);
    public static Font Semi(float size) => new(SemiFamily, size);
    public static Font Mono(float size) => new(MonoFamily, size);

    static readonly Dictionary<float, Font> iconFonts = new();
    public static Font Icons(float size)
    {
        if (IconFamily == null) return null;
        if (!iconFonts.TryGetValue(size, out var f)) iconFonts[size] = f = new Font(IconFamily, size);
        return f;
    }

    public static readonly Font Body = Ui(9.75f), Small = Ui(8.75f), Tiny = Ui(8f), Title = Semi(10.5f), Strong = Semi(9.75f),
        BadgeFont = Semi(7.5f), KeycapFont = Semi(7.75f), Time = Mono(9.5f), TimeSmall = Mono(7.5f);

    // Segoe Fluent Icons / Segoe MDL2 Assets code points (both fonts have them).
    public static class Glyph
    {
        public const string Play = "", Pause = "", PrevVideo = "", NextVideo = "", PrevFrame = "", NextFrame = "",
            FullScreen = "", ExitFullScreen = "", Volume = "", Mute = "", OpenFolder = "", Folder = "",
            Report = "", Camera = "", Edit = "", More = "", Help = "", Pane = "", Bug = "",
            Change = "", Note = "", Bip = "", Cut = "", Search = "", Clip = "", Check = "",
            Down = "", Rewind = "", Export = "", Tools = "", File = "";
    }

    // ---------- Drawing ----------

    public static int Px(Control c, float v) => (int)Math.Round(v * c.DeviceDpi / 96f);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
        g.SmoothingMode = mode;
    }

    public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width = 1)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width), radius);
        using var p = new Pen(c, width);
        g.DrawPath(p, path);
        g.SmoothingMode = mode;
    }

    public const TextFormatFlags Line = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    public static void Draw(Graphics g, string text, Font font, Rectangle r, Color c, TextFormatFlags extra = 0) =>
        TextRenderer.DrawText(g, text, font, r, c, Line | TextFormatFlags.EndEllipsis | extra);

    public static int Width(string text, Font font) =>
        string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), Line).Width;

    // A small rounded keycap with a key name ("B", "Ctrl+S").
    public static int KeycapWidth(Control c, string key) => Width(key, KeycapFont) + Px(c, 10);

    public static void DrawKeycap(Graphics g, Control c, string key, int x, int centerY, Color color)
    {
        int h = Px(c, 18), w = KeycapWidth(c, key);
        var r = new Rectangle(x, centerY - h / 2, w, h);
        DrawRound(g, Color.FromArgb(110, color), r, Px(c, 4));
        TextRenderer.DrawText(g, key, KeycapFont, r, color, Line | TextFormatFlags.HorizontalCenter);
    }

    public static string KindGlyph(string kind) => kind switch
    {
        "BUG" => Glyph.Bug,
        "DEGISIKLIK" => Glyph.Change,
        "NOT" => Glyph.Note,
        "BIP" => Glyph.Bip,
        "KES_BASLA" or "KES_BITIR" => Glyph.Cut,
        _ => null,
    };

    // Note kind badge: soft fill in the kind color, optional glyph and label, centered on centerY. Returns the width; with a null
    // Graphics it only measures.
    public static int Badge(Graphics g, Control c, string glyph, string text, Color color, Color back, int x, int centerY)
    {
        if (!HasIcons) glyph = null;
        int pad = Px(c, text.Length == 0 ? 5 : 7), gap = Px(c, 5), h = Px(c, 20);
        int gw = glyph != null ? Width(glyph, Icons(8f)) : 0, tw = Width(text, BadgeFont);
        int w = pad * 2 + gw + tw + (gw > 0 && tw > 0 ? gap : 0);
        if (g == null) return w;
        var r = new Rectangle(x, centerY - h / 2, w, h);
        FillRound(g, Mix(back, color, 0.2f), r, Px(c, 4));
        DrawRound(g, Mix(back, color, 0.45f), r, Px(c, 4));
        var fore = Mix(color, Color.White, 0.3f);
        int cx = x + pad;
        if (gw > 0)
        {
            TextRenderer.DrawText(g, glyph, Icons(8f), new Rectangle(cx, r.Y, gw, h), fore, Line);
            cx += gw + gap;
        }
        if (tw > 0) TextRenderer.DrawText(g, text, BadgeFont, new Rectangle(cx, r.Y, tw + 1, h), fore, Line);
        return w;
    }

    // ---------- Windows ----------

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // Dark native title bar (Windows 10 20H1+), caption in the toolbar color on Windows 11.
    public static void DarkTitleBar(Form f, Color? caption = null)
    {
        try
        {
            int on = 1, back = ColorRef(caption ?? Surface), text = ColorRef(Text), border = ColorRef(Border);
            DwmSetWindowAttribute(f.Handle, 20, ref on, 4);        // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(f.Handle, 35, ref back, 4);      // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(f.Handle, 36, ref text, 4);      // DWMWA_TEXT_COLOR
            DwmSetWindowAttribute(f.Handle, 34, ref border, 4);    // DWMWA_BORDER_COLOR
        }
        catch { }
    }

    // Small rounded corners for borderless popups (Windows 11).
    public static void RoundCorners(Form f)
    {
        try { int small = 3; DwmSetWindowAttribute(f.Handle, 33, ref small, 4); } catch { }
    }

    // Dark scroll bars (list views, scrolling panels) and dark combo boxes.
    public static void DarkScrollBars(Control c, string theme = "DarkMode_Explorer")
    {
        void Apply() { try { SetWindowTheme(c.Handle, theme, null); } catch { } }
        if (c.IsHandleCreated) Apply();
        c.HandleCreated += (s, e) => Apply();
    }

    // Tooltips in the palette.
    public static ToolTip Tooltip()
    {
        var tip = new ToolTip { OwnerDraw = true, InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 15000 };
        tip.Popup += (s, e) =>
        {
            var size = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), Small, new Size(420, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(size.Width + 16, size.Height + 10);
        };
        tip.Draw += (s, e) =>
        {
            e.Graphics.Clear(Raised);
            using (var p = new Pen(Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Small, new Rectangle(8, 5, e.Bounds.Width - 16, e.Bounds.Height - 10), Text,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };
        return tip;
    }
}

// Context menus in the palette.
sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.Faint : e.Item.IsOnDropDown && e.TextFormat.HasFlag(TextFormatFlags.Right) ? Theme.Muted : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        if (Theme.HasIcons) TextRenderer.DrawText(e.Graphics, Theme.Glyph.Check, Theme.Icons(9f), r, Theme.Accent, Theme.Line | TextFormatFlags.HorizontalCenter);
        else TextRenderer.DrawText(e.Graphics, "✓", Theme.Body, r, Theme.Accent, Theme.Line | TextFormatFlags.HorizontalCenter);
    }
}

sealed class DarkMenuColors : ProfessionalColorTable
{
    static readonly Color Back = Theme.Raised, Hot = Theme.Mix(Theme.Raised, Theme.Text, 0.08f);
    public override Color ToolStripDropDownBackground => Back;
    public override Color ImageMarginGradientBegin => Back;
    public override Color ImageMarginGradientMiddle => Back;
    public override Color ImageMarginGradientEnd => Back;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemBorder => Hot;
    public override Color MenuItemSelected => Hot;
    public override Color MenuItemSelectedGradientBegin => Hot;
    public override Color MenuItemSelectedGradientEnd => Hot;
    public override Color MenuItemPressedGradientBegin => Hot;
    public override Color MenuItemPressedGradientEnd => Hot;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Back;
    public override Color CheckBackground => Back;
    public override Color CheckSelectedBackground => Hot;
    public override Color CheckPressedBackground => Hot;
    public override Color ToolStripBorder => Theme.Border;
}
