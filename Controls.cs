using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace KareNot;

// Button that never takes keyboard focus, so Space/arrows always reach the player shortcuts. Rounded and dark with an optional
// icon glyph (or Painter), keycap hint and drop-down chevron. Accent: soft fill in that color (note kinds); Primary: orange fill;
// Quiet: no fill until hovered. Compact leaves only the icon (the tooltip still names the action).
class FlatButton : Control
{
    string glyph, keycap;
    bool compact, pushed, hover, down;
    public Color? Accent;
    public bool Primary, Quiet, Chevron;
    public float GlyphSize = 11f;
    public int BaseHeight = 32;                          // logical pixels
    public Action<Graphics, Rectangle, Color> Painter;   // draws the icon instead of a glyph
    public string Fallback;                              // text for an icon-only button when no icon font is installed

    public FlatButton(string text, Color? accent = null)
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Text = text;
        Accent = accent;
        Font = accent.HasValue ? Theme.Strong : Theme.Body;
        Padding = new Padding(10, 0, 10, 0);
        Margin = Padding.Empty;
        Cursor = Cursors.Hand;
        AutoSize = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get => glyph; set { if (glyph != value) { glyph = value; Relayout(); } } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Keycap { get => keycap; set { if (keycap != value) { keycap = value; Relayout(); } } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Compact { get => compact; set { if (compact != value) { compact = value; Invalidate(); } } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Pushed { get => pushed; set { if (pushed != value) { pushed = value; Invalidate(); } } }

    void Relayout()
    {
        Parent?.PerformLayout();
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Relayout();
    }

    int Px(float v) => Theme.Px(this, v);
    bool HasIcon => Painter != null || (glyph != null && Theme.HasIcons);
    string Label => compact && HasIcon ? "" : !string.IsNullOrEmpty(Text) ? Text : HasIcon ? "" : Fallback ?? "";
    int IconWidth => Painter != null ? Px(GlyphSize * 1.5f) : HasIcon ? Theme.Width(glyph, Theme.Icons(GlyphSize)) : 0;
    int ChevronWidth => Chevron ? Px(13) : 0;

    public override Size GetPreferredSize(Size proposed)
    {
        int h = Math.Max(Px(BaseHeight), Font.Height + Px(8));
        int tw = Theme.Width(Label, Font), iw = IconWidth, gap = Px(6);
        if (tw == 0 && keycap == null && !Chevron) return new Size(h, h);   // icon only: square
        int w = Padding.Horizontal + iw + tw + (iw > 0 && tw > 0 ? gap : 0) + ChevronWidth;
        if (keycap != null) w += gap + Theme.KeycapWidth(this, keycap);
        return new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = Parent?.BackColor ?? Theme.Surface;
        g.Clear(back);
        Color fill, border, fore;
        if (Primary)
        {
            fill = down ? Theme.Mix(Theme.Accent, Color.Black, 0.15f) : hover ? Theme.Mix(Theme.Accent, Color.White, 0.15f) : Theme.Accent;
            border = fill;
            fore = Theme.OnAccent;
        }
        else if (Accent is Color a)
        {
            fill = Theme.Mix(back, a, down ? 0.34f : hover ? 0.25f : 0.15f);
            border = Theme.Mix(back, a, hover ? 0.75f : 0.5f);
            fore = Theme.Mix(a, Color.White, 0.35f);
        }
        else
        {
            float level = down ? 0.16f : hover ? 0.1f : pushed ? 0.07f : Quiet ? 0 : 0.055f;
            fill = Theme.Mix(back, Theme.Text, level);
            border = Quiet && !hover ? fill : Theme.Mix(back, Theme.Text, 0.15f);
            fore = Quiet && !hover && !pushed ? Theme.Muted : Theme.Text;
        }
        if (!Enabled) fore = Theme.Faint;
        var r = new Rectangle(0, 0, Width, Height);
        if (fill != back) Theme.FillRound(g, fill, r, Px(6));
        if (border != fill) Theme.DrawRound(g, border, r, Px(6));

        // Content centered; the keycap, then the icon go first when the button is too narrow, the text ends with an ellipsis.
        string label = Label;
        int gap = Px(6), tw = Theme.Width(label, Font), iw = IconWidth, cw = ChevronWidth;
        int kw = keycap != null ? gap + Theme.KeycapWidth(this, keycap) : 0;
        bool square = tw == 0 && keycap == null && !Chevron;
        int avail = square ? Width : Width - Padding.Horizontal;
        if (kw > 0 && iw + tw + (iw > 0 && tw > 0 ? gap : 0) + kw + cw > avail) kw = 0;
        if (iw > 0 && tw > 0 && iw + gap + tw + kw + cw > avail) iw = 0;
        int textW = Math.Max(0, Math.Min(tw, avail - iw - (iw > 0 && tw > 0 ? gap : 0) - kw - cw));
        int total = iw + (iw > 0 && textW > 0 ? gap : 0) + textW + kw + cw;
        int x = Math.Max(square ? 0 : Padding.Left, (Width - total) / 2);
        if (iw > 0)
        {
            var ir = new Rectangle(x, 0, iw, Height);
            if (Painter != null) Painter(g, ir, fore);
            else TextRenderer.DrawText(g, glyph, Theme.Icons(GlyphSize), ir, fore, Theme.Line | TextFormatFlags.HorizontalCenter);
            x += iw + (textW > 0 ? gap : 0);
        }
        if (textW > 0)
        {
            Theme.Draw(g, label, Font, new Rectangle(x, 0, textW, Height), fore);
            x += textW;
        }
        if (kw > 0)
        {
            Theme.DrawKeycap(g, this, keycap, x + gap, Height / 2, fore);
            x += kw;
        }
        if (cw > 0) DrawChevron(g, x + Px(4), Height / 2, fore);
    }

    void DrawChevron(Graphics g, int x, int cy, Color c)
    {
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(c, 1.4f * s);
        g.DrawLines(pen, new[] { new PointF(x, cy - 2 * s), new PointF(x + 4 * s, cy + 2 * s), new PointF(x + 8 * s, cy - 2 * s) });
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = down = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        down = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        down = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}

// Non-focusable drop-down list (speed, list filter): a dark menu with the items, the chosen one checked.
sealed class DropButton : FlatButton
{
    public readonly List<string> Items = new();
    int index = -1;
    public event Action SelectionChangeCommitted;

    public DropButton() : base("")
    {
        Chevron = true;
        Click += (s, e) => ShowMenu();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => index;
        set
        {
            index = value;
            Text = value >= 0 && value < Items.Count ? Items[value] : "";
        }
    }

    void ShowMenu()
    {
        var menu = new ContextMenuStrip();
        for (int i = 0; i < Items.Count; i++)
        {
            int k = i;
            menu.Items.Add(new ToolStripMenuItem(Items[i], null, (s, e) =>
            {
                SelectedIndex = k;
                SelectionChangeCommitted?.Invoke();
            }) { Checked = i == index });
        }
        menu.Closed += (s, e) => BeginInvoke(menu.Dispose);
        menu.Show(this, new Point(0, Height));
    }
}

// Non-focusable check box in the palette (orange box when checked).
sealed class CheckToggle : Control
{
    bool isChecked, hover;

    public CheckToggle(string text)
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Text = text;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        AutoSize = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked { get => isChecked; set { if (isChecked != value) { isChecked = value; Invalidate(); } } }

    int Px(float v) => Theme.Px(this, v);

    public override Size GetPreferredSize(Size proposed) =>
        new(Px(26) + Theme.Width(Text, Font) + Px(2), Math.Max(Px(28), Font.Height + Px(4)));

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = Parent?.BackColor ?? Theme.Surface;
        g.Clear(back);
        int s = Px(18);
        var box = new Rectangle(0, (Height - s) / 2, s, s);
        if (isChecked)
        {
            Theme.FillRound(g, hover ? Theme.Mix(Theme.Accent, Color.White, 0.15f) : Theme.Accent, box, Px(4));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.OnAccent, 2f * DeviceDpi / 96f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(box.X + s * 0.26f, box.Y + s * 0.52f), new PointF(box.X + s * 0.44f, box.Y + s * 0.7f), new PointF(box.X + s * 0.76f, box.Y + s * 0.32f),
            });
        }
        else
        {
            Theme.FillRound(g, Theme.Field, box, Px(4));
            Theme.DrawRound(g, Theme.Mix(back, Theme.Text, hover ? 0.5f : 0.3f), box, Px(4));
        }
        Theme.Draw(g, Text, Font, new Rectangle(s + Px(8), 0, Math.Max(0, Width - s - Px(8)), Height), Theme.Text);
    }
}

// Rounded dark frame around a borderless single-line text box (optional glyph on the left); clicking the frame focuses the box.
sealed class FieldHost : Control
{
    public readonly TextBox Box;
    readonly string glyph;
    readonly int boxWidth;   // logical width of the text box when the frame sizes itself (dialogs)
    public int BaseHeight = 34;

    public FieldHost(TextBox box, string glyph = null)
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Box = box;
        boxWidth = box.Width;
        this.glyph = Theme.HasIcons ? glyph : null;
        box.BorderStyle = BorderStyle.None;
        box.BackColor = Theme.Field;
        box.ForeColor = Theme.Text;
        box.Dock = DockStyle.None;
        box.Margin = Padding.Empty;
        Controls.Add(box);
        box.GotFocus += (s, e) => Invalidate();
        box.LostFocus += (s, e) => Invalidate();
        Cursor = Cursors.IBeam;
        Margin = new Padding(3);
        AutoSize = true;
    }

    int Px(float v) => Theme.Px(this, v);
    int Inset => glyph != null ? Px(32) : Px(10);

    public override Size GetPreferredSize(Size proposed) =>
        new(Inset + Px(boxWidth) + Px(10), Math.Max(Px(BaseHeight), Box.PreferredHeight + Px(12)));

    protected override void OnLayout(LayoutEventArgs e)
    {
        Box.SetBounds(Inset, (Height - Box.Height) / 2, Math.Max(Px(20), Width - Inset - Px(10)), Box.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Box.Enabled) Box.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var r = new Rectangle(0, 0, Width, Height);
        Theme.FillRound(g, Theme.Field, r, Px(6));
        Theme.DrawRound(g, Box.Focused ? Theme.Mix(Theme.Border, Theme.Accent, 0.7f) : Theme.Border, r, Px(6));
        if (glyph != null)
            TextRenderer.DrawText(g, glyph, Theme.Icons(10f), new Rectangle(Px(10), 0, Px(16), Height), Theme.Faint, Theme.Line | TextFormatFlags.HorizontalCenter);
    }
}

// Text tabs of the side panel; the selected one is bright with an orange underline.
sealed class TabStrip : Control
{
    readonly List<(string Text, string Tip)> tabs = new();
    readonly ToolTip tip;
    int selected, hot = -1;
    public event Action<int> TabClicked;

    public TabStrip(ToolTip tip)
    {
        this.tip = tip;
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Strong;
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
    }

    public void Add(string text, string tooltip) => tabs.Add((text, tooltip));
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected { get => selected; set { if (selected != value) { selected = value; Invalidate(); } } }

    int Px(float v) => Theme.Px(this, v);

    // Padding around the labels shrinks on a narrow panel so all tabs stay visible.
    List<Rectangle> TabBounds(out int pad)
    {
        int side = Px(12), sum = tabs.Sum(t => Theme.Width(t.Text, Font));
        pad = Math.Clamp((Width - 2 * side - sum) / Math.Max(1, tabs.Count * 2), Px(3), Px(14));
        var list = new List<Rectangle>();
        int x = side;
        foreach (var t in tabs)
        {
            int w = Theme.Width(t.Text, Font) + 2 * pad;
            list.Add(new Rectangle(x, 0, w, Height));
            x += w;
        }
        return list;
    }

    int HitTest(Point p) => TabBounds(out _).FindIndex(r => r.Contains(p));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var line = new SolidBrush(Theme.Border)) g.FillRectangle(line, 0, Height - 1, Width, 1);
        var bounds = TabBounds(out int pad);
        for (int i = 0; i < tabs.Count; i++)
        {
            var r = bounds[i];
            var color = i == selected ? Theme.Text : i == hot ? Theme.Mix(Theme.Muted, Theme.Text, 0.6f) : Theme.Muted;
            TextRenderer.DrawText(g, tabs[i].Text, Font, r, color, Theme.Line | TextFormatFlags.HorizontalCenter);
            if (i == selected)
                Theme.FillRound(g, Theme.Accent, new Rectangle(r.X + pad / 2, Height - Px(3), r.Width - pad, Px(3)), Px(1.5f));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = HitTest(e.Location);
        if (i == hot) return;
        hot = i;
        Invalidate();
        tip.SetToolTip(this, i >= 0 ? tabs[i].Tip : null);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hot = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && i >= 0) TabClicked?.Invoke(i);
    }
}

// Status badges above the video ("Duraklatıldı", "Alan seçili · Esc ile iptal"). They live outside the video panel because VLC's
// native window would cover controls placed over it. Left badges from the left, right ones from the right.
sealed class BadgeStrip : Control
{
    List<(string Glyph, string Text, Color Color, bool Right)> badges = new();
    string key = "";

    public BadgeStrip()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Small;
        BackColor = Theme.Canvas;
    }

    public void Set(List<(string Glyph, string Text, Color Color, bool Right)> list)
    {
        var k = string.Join("|", list.Select(b => b.Text));
        if (k == key) return;
        key = k;
        badges = list;
        Invalidate();
    }

    public string Shown => key;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        float s = DeviceDpi / 96f;
        int left = Padding.Left, right = Width - Padding.Right, h = (int)(26 * s);
        foreach (var (glyph, text, color, isRight) in badges)
        {
            var icon = Theme.HasIcons ? glyph : null;
            int gw = icon != null ? Theme.Width(icon, Theme.Icons(8.5f)) : (int)(7 * s);
            int w = (int)(10 * s) + gw + (int)(7 * s) + Theme.Width(text, Font) + (int)(12 * s);
            int x = isRight ? right - w : left;
            var r = new Rectangle(x, (Height - h) / 2, w, h);
            Theme.FillRound(g, Theme.Mix(BackColor, color, 0.14f), r, 6 * s);
            Theme.DrawRound(g, Theme.Mix(BackColor, color, 0.4f), r, 6 * s);
            var ir = new Rectangle(x + (int)(10 * s), r.Y, gw, h);
            if (icon != null) TextRenderer.DrawText(g, icon, Theme.Icons(8.5f), ir, color, Theme.Line | TextFormatFlags.HorizontalCenter);
            else Theme.FillRound(g, color, new RectangleF(ir.X, r.Y + (h - gw) / 2f, gw, gw), gw / 2f);
            Theme.Draw(g, text, Font, new Rectangle(ir.Right + (int)(7 * s), r.Y, Theme.Width(text, Font) + 2, h), Theme.Text);
            if (isRight) right = x - (int)(8 * s);
            else left = x + w + (int)(8 * s);
        }
    }
}

// Save state with a dot: green after a successful save, red with the error, gray before the first save.
sealed class SaveIndicator : Control
{
    string full = "Notlar otomatik kaydedilir", shortText = "Otomatik kayıt";

    public SaveIndicator()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Small;
        AutoSize = true;
    }

    public bool? Ok { get; private set; }
    public string Shown => Lead + Theme.Width(full, Font) <= Width ? full : shortText;

    public void Set(bool? ok, string fullText, string shortLabel)
    {
        Ok = ok;
        full = fullText;
        shortText = shortLabel;
        Invalidate();
    }

    int Lead => Theme.Px(this, 16);
    public int MinWidth => Lead + Theme.Width(shortText, Font);

    public override Size GetPreferredSize(Size proposed) => new(Lead + Theme.Width(full, Font), Theme.Px(this, 24));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        int d = Theme.Px(this, 8);
        Theme.FillRound(g, Ok == true ? Theme.Ok : Ok == false ? Theme.Error : Theme.Faint, new Rectangle(0, (Height - d) / 2, d, d), d / 2f);
        Theme.Draw(g, Shown, Font, new Rectangle(Lead, 0, Math.Max(0, Width - Lead), Height), Ok == false ? Theme.Mix(Theme.Error, Theme.Text, 0.3f) : Theme.Muted);
    }
}

// Non-focusable volume slider, 0–100 (louder settings made with the keyboard show as full).
sealed class VolumeSlider : Control
{
    int value = 100;
    bool drag;
    public event Action<int> ValueChanged;   // changed by the user

    public VolumeSlider()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(84, 32);
        Cursor = Cursors.Hand;
    }

    public bool Dragging => drag;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { value = Math.Max(0, value); if (this.value != value) { this.value = value; Invalidate(); } } }

    int Px(float v) => Theme.Px(this, v);
    Rectangle Track => new(Px(7), Height / 2 - Px(2), Math.Max(1, Width - Px(14)), Px(4));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var back = Parent?.BackColor ?? Theme.Surface;
        g.Clear(back);
        var t = Track;
        Theme.FillRound(g, Theme.Mix(back, Theme.Text, 0.15f), t, t.Height / 2f);
        int x = t.X + t.Width * Math.Min(value, 100) / 100;
        if (x > t.X) Theme.FillRound(g, Theme.Accent, new Rectangle(t.X, t.Y, x - t.X, t.Height), t.Height / 2f);
        int k = Px(12);
        Theme.FillRound(g, Theme.Text, new Rectangle(x - k / 2, Height / 2 - k / 2, k, k), k / 2f);
    }

    void SetFromMouse(int x)
    {
        var t = Track;
        Value = Math.Clamp((x - t.X) * 100 / t.Width, 0, 100);
        ValueChanged?.Invoke(value);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        drag = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        drag = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value = Math.Clamp(Math.Min(value, 100) + (e.Delta > 0 ? 5 : -5), 0, 100);
        ValueChanged?.Invoke(value);
    }
}

// Timeline: time ticks with labels, the played part and playhead in orange, colored note markers above the track, cut / bleep
// bands and a green line under the reviewed part; click or drag to seek, hover for a preview.
sealed class SeekBar : Control
{
    public long LengthMs;
    public long ValueMs;
    public readonly List<(long ms, Color color)> Markers = new();
    public readonly List<(long startMs, long endMs, Color color)> Ranges = new();   // cut / bleep ranges of the open tab
    public long ReviewedMs;                                                          // "reviewed up to here"
    public event Action<long> SeekRequested;
    public event Action<long?> HoverChanged;                                         // hovered time, null when the mouse leaves
    long lastSeekTick;
    static readonly int[] TickSteps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1200, 1800, 3600, 7200 };

    public SeekBar()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 52;
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
    }

    int Px(float v) => Theme.Px(this, v);

    Rectangle Track => new(Px(16), Height - Px(16), Math.Max(1, Width - Px(32)), Px(6));

    int X(long ms) => Track.X + (int)(Track.Width * Math.Clamp((double)ms / LengthMs, 0, 1));

    long MsAt(int x) => (long)(LengthMs * Math.Clamp((double)(x - Track.X) / Track.Width, 0, 1));

    static string Clock(double seconds) => TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds))).ToString(@"hh\:mm\:ss");

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var track = Track;
        float radius = track.Height / 2f;
        Theme.FillRound(g, Theme.Mix(BackColor, Theme.Text, 0.12f), track, radius);
        if (LengthMs <= 0) return;
        DrawTicks(g, track);
        foreach (var (startMs, endMs, color) in Ranges)
        {
            int x1 = X(startMs), x2 = X(endMs);
            Theme.FillRound(g, Color.FromArgb(120, color), new Rectangle(x1, track.Y - Px(4), Math.Max(Px(2), x2 - x1), track.Height + Px(8)), Px(2));
        }
        int x = X(ValueMs);
        if (x > track.X) Theme.FillRound(g, Theme.Accent, new Rectangle(track.X, track.Y, x - track.X, track.Height), radius);
        if (ReviewedMs > 0)
        {
            // Green line under the track up to the reviewed position.
            using var line = new SolidBrush(Color.FromArgb(200, Theme.Ok));
            g.FillRectangle(line, track.X, track.Bottom + Px(3), X(ReviewedMs) - track.X, Px(2));
        }
        int size = Px(9), top = track.Y - Px(15);
        foreach (var (ms, color) in Markers)
        {
            int mx = X(ms);
            using (var stem = new SolidBrush(color)) g.FillRectangle(stem, mx, top + size - 1, Math.Max(1, Px(1)), track.Y - top - size + 1);
            Theme.FillRound(g, color, new Rectangle(mx - size / 2, top, size, size), Px(2));
        }
        // Playhead: orange line with a knob above the markers.
        using (var head = new SolidBrush(Theme.Accent)) g.FillRectangle(head, x - Px(1), top - Px(3), Px(2), track.Bottom + Px(2) - top + Px(3));
        int knob = Px(12);
        Theme.FillRound(g, Theme.Accent, new Rectangle(x - knob / 2, top - Px(3) - knob / 2, knob, knob), knob / 2f);
    }

    // Labelled ticks at a round interval that leaves room for the labels; the length is labelled at the right end.
    void DrawTicks(Graphics g, Rectangle track)
    {
        var font = Theme.TimeSmall;
        int labelW = Theme.Width("00:00:00", font), labelTop = Px(3), labelH = font.Height;
        double seconds = LengthMs / 1000.0;
        int step = TickSteps.FirstOrDefault(s => track.Width * s / seconds >= labelW + Px(28));
        if (step == 0) step = TickSteps[^1];
        var color = Theme.Faint;
        using var tick = new SolidBrush(Theme.Mix(BackColor, Theme.Text, 0.22f));
        var end = Clock(seconds);
        int endW = Theme.Width(end, font), endX = track.Right - endW;
        TextRenderer.DrawText(g, end, font, new Rectangle(endX, labelTop, endW, labelH), color, Theme.Line);
        for (int s = 0; s < seconds; s += step)
        {
            int x = X(s * 1000L);
            int lx = s == 0 ? track.X : x - labelW / 2;
            if (lx + labelW > endX - Px(8)) break;
            g.FillRectangle(tick, x, track.Y - Px(4), 1, Px(3));
            TextRenderer.DrawText(g, Clock(s), font, new Rectangle(lx, labelTop, labelW, labelH), color, Theme.Line);
        }
    }

    void SeekTo(int x, bool force)
    {
        if (LengthMs <= 0) return;
        var ms = MsAt(x);
        ValueMs = ms;
        Invalidate();
        if (force || Environment.TickCount64 - lastSeekTick > 120)
        {
            lastSeekTick = Environment.TickCount64;
            SeekRequested?.Invoke(ms);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        HoverChanged?.Invoke(null);
        SeekTo(e.X, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) SeekTo(e.X, false);
        else if (LengthMs > 0) HoverChanged?.Invoke(MsAt(e.X));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) SeekTo(e.X, true);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        HoverChanged?.Invoke(null);
    }
}

// Owner-drawn list in the palette: the owner paints whole rows in DrawItem; rows are RowHeight logical pixels tall, no header,
// dark scroll bars; the last column fills the width (the others are only data). EmptyText shows while there are no rows.
class DarkListView : ListView
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowScrollBar(IntPtr hWnd, int bar, bool show);
    public string EmptyText = "";
    public int RowHeight = 36;
    public int TimeWidth, BadgeWidth;   // column widths measured when the rows are filled

    public DarkListView()
    {
        DoubleBuffered = true;
        OwnerDraw = true;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = false;
        HeaderStyle = ColumnHeaderStyle.None;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Raised;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Theme.DarkScrollBars(this);
        DrawColumnHeader += (s, e) => e.DrawDefault = true;
        DrawSubItem += (s, e) => { };
        ItemSelectionChanged += (s, e) => Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRowHeight();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyRowHeight();
    }

    // The row height follows the small image list.
    void ApplyRowHeight()
    {
        var old = SmallImageList;
        SmallImageList = new ImageList { ImageSize = new Size(1, Math.Min(256, Theme.Px(this, RowHeight))) };
        old?.Dispose();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitColumns();
    }

    // Rows filled while the page was hidden (zero-sized) can come up with a wrong scroll offset; reset it once shown.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible || !IsHandleCreated || Items.Count == 0) return;
        BeginInvoke(() =>
        {
            if (Items.Count == 0) return;
            FitColumns();
            if (Items[0].Bounds.Y > 0) EnsureVisible(0);
            if (SelectedItems.Count > 0) SelectedItems[0].EnsureVisible();
        });
    }

    public void FitColumns()
    {
        if (Columns.Count == 0) return;
        for (int i = 0; i < Columns.Count - 1; i++)
            if (Columns[i].Width != 0) Columns[i].Width = 0;
        int w = Math.Max(10, ClientSize.Width);
        if (Columns[^1].Width != w) Columns[^1].Width = w;
        if (Columns[^1].DisplayIndex != 0) Columns[^1].DisplayIndex = 0;   // the empty data columns sit past the right edge
        if (IsHandleCreated) ShowScrollBar(Handle, 0, false);              // SB_HORZ: rows always fit the width
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F) return;   // WM_PAINT
        // The themed list draws column separators in the empty area below the rows; cover them.
        using var g = CreateGraphics();
        int top = Items.Count > 0 ? GetItemRect(Items.Count - 1).Bottom : 0;
        if (top < ClientSize.Height)
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, 0, Math.Max(0, top), ClientSize.Width, ClientSize.Height - Math.Max(0, top));
        if (Items.Count > 0 || EmptyText.Length == 0) return;
        int pad = Theme.Px(this, 14);
        TextRenderer.DrawText(g, EmptyText, Theme.Small, new Rectangle(pad, pad, ClientSize.Width - 2 * pad, ClientSize.Height - 2 * pad), Theme.Faint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}

// The Kısayollar tab: one block per group, the keys as keycaps on the left, the action wrapping beside them.
sealed class ShortcutView : Control
{
    readonly (string Title, (string Keys, string Action)[] Rows)[] groups;
    static readonly Regex Separators = new(@"( / | \+ |, (?=\w))");
    static readonly Font TitleFont = Theme.Semi(8.25f);

    public ShortcutView((string Title, (string Keys, string Action)[] Rows)[] groups)
    {
        this.groups = groups;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Small;
        AutoSize = true;
    }

    int Px(float v) => Theme.Px(this, v);

    static bool IsSeparator(string token) => token is " / " or " + " or ", ";

    // Key names become keycaps; mouse actions and words ("Çift tık", "Hepsi", "Dışa aktar ▾") stay text.
    static bool IsKey(string token) => token != "Hepsi" && !token.Contains('▾') && !Regex.IsMatch(token, @" \p{Ll}");

    IEnumerable<string> Tokens(string keys) => Separators.Split(keys).Where(t => t.Length > 0);

    int TokenWidth(string t) =>
        IsSeparator(t) ? Theme.Width(t.Trim(), Font) + Px(8) : IsKey(t) ? Theme.KeycapWidth(this, t) + Px(2) : Theme.Width(t, Font) + Px(2);

    int KeysWidth(string keys) => Tokens(keys).Sum(TokenWidth);

    public override Size GetPreferredSize(Size proposed)
    {
        int w = proposed.Width > 1 ? proposed.Width : Width;
        return new Size(w, Arrange(w, null));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        Arrange(Width, e.Graphics);
    }

    // Lays the groups out for the width; draws when g is set. Returns the height.
    int Arrange(int width, Graphics g)
    {
        int y = 0, gap = Px(14), line = Px(24);
        int keyCol = Math.Min(width / 2, groups.SelectMany(gr => gr.Rows).Max(r => KeysWidth(r.Keys)));
        foreach (var (title, rows) in groups)
        {
            if (y > 0) y += Px(16);
            if (g != null) TextRenderer.DrawText(g, title, TitleFont, new Point(0, y), Theme.Mix(Theme.Accent, Theme.Text, 0.35f), TextFormatFlags.NoPrefix);
            y += TitleFont.Height + Px(8);
            foreach (var (keys, action) in rows)
            {
                int descX = Math.Max(keyCol, KeysWidth(keys)) + gap, descW = Math.Max(Px(60), width - descX);
                int textTop = (line - Font.Height) / 2;
                int descH = TextRenderer.MeasureText(action, Font, new Size(descW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                int rowH = Math.Max(line, textTop + descH) + Px(4);
                if (g != null)
                {
                    int x = 0, cy = y + line / 2;
                    foreach (var t in Tokens(keys))
                    {
                        if (IsSeparator(t)) TextRenderer.DrawText(g, t.Trim(), Font, new Rectangle(x, y, TokenWidth(t), line), Theme.Faint, Theme.Line | TextFormatFlags.HorizontalCenter);
                        else if (IsKey(t)) Theme.DrawKeycap(g, this, t, x, cy, Theme.Text);
                        else TextRenderer.DrawText(g, t, Font, new Rectangle(x, y, TokenWidth(t), line), Theme.Text, Theme.Line);
                        x += TokenWidth(t);
                    }
                    TextRenderer.DrawText(g, action, Font, new Rectangle(descX, y + textTop, descW, descH), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
                y += rowH;
            }
        }
        return y + Px(8);
    }
}
