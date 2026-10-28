namespace KareNot;

// Layout helpers of the dark UI. Sizes given to them are logical (96 dpi) pixels and are scaled with the control's DPI.

// Plain container that never takes focus.
class Pane : Control
{
    public Pane()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    protected int Px(float v) => Theme.Px(this, v);

    // Docked children respect the padding (a plain Control ignores it).
    public override Rectangle DisplayRectangle
    {
        get
        {
            var r = ClientRectangle;
            return new Rectangle(r.X + Padding.Left, r.Y + Padding.Top, Math.Max(0, r.Width - Padding.Horizontal), Math.Max(0, r.Height - Padding.Vertical));
        }
    }
}

sealed class Spacer : Pane
{
    public Spacer(int height) => Height = height;
}

// Image scaled smoothly into the control (the toolbar logo).
sealed class ImageView : Pane
{
    readonly Image image;

    public ImageView(Image image) => this.image = image;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (image == null) return;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(image, ClientRectangle);
    }
}

// Stack of docked rows; with AutoSize it is as tall as its visible Top / Bottom rows plus padding.
class Column : Pane
{
    public override Size GetPreferredSize(Size proposed)
    {
        int w = proposed.Width > 1 ? proposed.Width : Width, h = Padding.Vertical;
        foreach (Control c in Controls)
            if (c.Dock is DockStyle.Top or DockStyle.Bottom && (c.Visible || !Visible))
                h += c.AutoSize ? c.GetPreferredSize(new Size(w - Padding.Horizontal, 1)).Height : c.Height;
        return new Size(w, h);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (AutoSize && Dock is DockStyle.Top or DockStyle.Bottom && Parent != null && GetPreferredSize(new Size(Width, 1)).Height != Height)
            Parent.PerformLayout();
    }
}

// Rounded surface with a thin border holding docked rows.
sealed class Card : Column
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint, true);
        BackColor = Theme.Raised;
        Padding = new Padding(12);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var r = new Rectangle(0, 0, Width, Height);
        Theme.FillRound(g, BackColor, r, Px(8));
        Theme.DrawRound(g, Theme.Border, r, Px(8));
    }
}

// Row with Left controls from the left edge and Right controls from the right edge, vertically centered. Elastic (from either
// group) fills the free width. When it would get less than ElasticMin, the Compactable buttons drop their text one by one, then
// (with Wrap) the right group moves to a second line. AutoSize controls are measured with GetPreferredSize, others by Size.
sealed class SplitRow : Pane
{
    public readonly List<Control> LeftItems = new(), RightItems = new();
    public readonly List<FlatButton> Compactable = new();
    public Control Elastic;
    public Func<int> ElasticMin = () => 0;
    public bool Wrap = true;
    public int Gap = 8, RowHeight;

    public SplitRow(int rowHeight = 32)
    {
        RowHeight = rowHeight;
        AutoSize = true;
    }

    public SplitRow L(params Control[] controls)
    {
        LeftItems.AddRange(controls);
        Controls.AddRange(controls);
        return this;
    }

    public SplitRow R(params Control[] controls)
    {
        RightItems.AddRange(controls);
        Controls.AddRange(controls);
        return this;
    }

    static Size Pref(Control c) => c.AutoSize ? c.GetPreferredSize(Size.Empty) : c.Size;

    int GroupWidth(List<Control> group) =>
        group.Sum(c => (c == Elastic ? 0 : Pref(c).Width) + c.Margin.Horizontal) + Math.Max(0, group.Count - 1) * Px(Gap);

    public override Size GetPreferredSize(Size proposed)
    {
        int w = proposed.Width > 1 ? proposed.Width : Width;
        return new Size(w, Arrange(w, false));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int h = Arrange(Width, true);
        if (AutoSize && h != Height && Parent != null) Parent.PerformLayout();
    }

    int Arrange(int width, bool apply)
    {
        int inner = width - Padding.Horizontal, gap = Px(Gap);
        bool both = LeftItems.Count > 0 && RightItems.Count > 0;
        bool Fits() => GroupWidth(LeftItems) + GroupWidth(RightItems) + (both ? gap : 0) + (Elastic != null ? ElasticMin() : 0) <= inner;
        foreach (var b in Compactable) b.Compact = false;
        foreach (var b in Compactable)
        {
            if (Fits()) break;
            b.Compact = true;
        }
        bool wrap = Wrap && both && !Fits();
        int elastic = Math.Max(0, !wrap ? inner - GroupWidth(LeftItems) - GroupWidth(RightItems) - (both ? gap : 0)
            : inner - GroupWidth(LeftItems.Contains(Elastic) ? LeftItems : RightItems));
        int LineHeight(IEnumerable<Control> line) => line.Select(c => Pref(c).Height + c.Margin.Vertical).Append(Px(RowHeight)).Max();
        int h1 = LineHeight(wrap ? LeftItems : LeftItems.Concat(RightItems)), h2 = wrap ? LineHeight(RightItems) : 0;
        if (apply)
        {
            int x = Padding.Left, top = Padding.Top;
            foreach (var c in LeftItems)
            {
                var size = Pref(c);
                int w = c == Elastic ? elastic : size.Width;
                c.SetBounds(x + c.Margin.Left, top + (h1 - size.Height) / 2, w, size.Height);
                x += c.Margin.Horizontal + w + gap;
            }
            if (wrap) top += h1 + Px(4);
            int lineH = wrap ? h2 : h1;
            x = width - Padding.Right;
            for (int i = RightItems.Count - 1; i >= 0; i--)
            {
                var c = RightItems[i];
                var size = Pref(c);
                int w = c == Elastic ? elastic : size.Width;
                x -= c.Margin.Right + w;
                c.SetBounds(x, top + (lineH - size.Height) / 2, w, size.Height);
                x -= c.Margin.Left + gap;
            }
        }
        return Padding.Vertical + h1 + (wrap ? Px(4) + h2 : 0);
    }
}

// Controls side by side with equal widths (the note kind buttons).
sealed class EqualRow : Pane
{
    public int Gap = 8, RowHeight;

    public EqualRow(int rowHeight, params Control[] controls)
    {
        RowHeight = rowHeight;
        AutoSize = true;
        foreach (var c in controls) c.AutoSize = false;
        Controls.AddRange(controls);
    }

    public override Size GetPreferredSize(Size proposed) => new(proposed.Width > 1 ? proposed.Width : Width, Px(RowHeight) + Padding.Vertical);

    protected override void OnLayout(LayoutEventArgs e)
    {
        int n = Controls.Count, gap = Px(Gap), inner = Width - Padding.Horizontal, h = Px(RowHeight);
        if (n == 0) return;
        int w = (inner - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
            Controls[i].SetBounds(Padding.Left + i * (w + gap), Padding.Top, i == n - 1 ? inner - (n - 1) * (w + gap) : w, h);
    }
}

// Player bar: Left group at the left edge, Center group centered (pushed aside rather than overlapping), Right group at the
// right edge. When the bar is too narrow, the Optional controls are hidden in their order.
sealed class ChromeBar : Pane
{
    public readonly List<Control> LeftItems = new(), CenterItems = new(), RightItems = new(), Optional = new();
    public int Gap = 4, GroupGap = 16;
    bool arranging;

    public void AddAll()
    {
        foreach (var c in LeftItems.Concat(CenterItems).Concat(RightItems)) Controls.Add(c);
    }

    static Size Pref(Control c) => c.AutoSize ? c.GetPreferredSize(Size.Empty) : c.Size;

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (arranging) return;
        arranging = true;
        try
        {
            int gap = Px(Gap), group = Px(GroupGap);
            var hidden = new HashSet<Control>();
            int Width(List<Control> list)
            {
                var shown = list.Where(c => !hidden.Contains(c)).ToList();
                return shown.Sum(c => Pref(c).Width) + Math.Max(0, shown.Count - 1) * gap;
            }
            int Need() => Padding.Horizontal + Width(LeftItems) + Width(CenterItems) + Width(RightItems) + 2 * group;
            foreach (var c in Optional)
            {
                if (Need() <= this.Width) break;
                hidden.Add(c);
            }
            foreach (var c in LeftItems.Concat(CenterItems).Concat(RightItems))
            {
                bool visible = !hidden.Contains(c);
                if (c.Visible != visible) c.Visible = visible;
            }
            int Place(List<Control> list, int x)
            {
                foreach (var c in list.Where(c => !hidden.Contains(c)))
                {
                    var size = Pref(c);
                    c.SetBounds(x, (Height - size.Height) / 2, size.Width, size.Height);
                    x += size.Width + gap;
                }
                return x;
            }
            int leftEnd = Place(LeftItems, Padding.Left) - gap;
            int rightStart = this.Width - Padding.Right - Width(RightItems);
            Place(RightItems, rightStart);
            int centerW = Width(CenterItems);
            int cx = Math.Clamp((this.Width - centerW) / 2, leftEnd + group, Math.Max(leftEnd + group, rightStart - group - centerW));
            Place(CenterItems, cx);
        }
        finally
        {
            arranging = false;
        }
    }
}

// Label that wraps to its width; as a Top-docked row it is as tall as its text.
sealed class WrapLabel : Pane
{
    public WrapLabel()
    {
        SetStyle(ControlStyles.UserPaint, true);
        AutoSize = true;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Parent?.PerformLayout();
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposed)
    {
        int w = proposed.Width > 1 ? proposed.Width : Width;
        var size = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, w - Padding.Horizontal), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return new Size(w, size.Height + Padding.Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical), ForeColor,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
