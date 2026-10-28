namespace KareNot;

// Button that never takes keyboard focus, so Space/arrows always reach the player shortcuts.
sealed class FlatButton : Button
{
    public FlatButton(string text, Color? accent = null)
    {
        SetStyle(ControlStyles.Selectable, false);
        Text = text;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = accent ?? Color.FromArgb(90, 90, 90);
        FlatAppearance.BorderSize = 1;
        BackColor = Color.FromArgb(45, 45, 48);
        ForeColor = accent.HasValue ? ControlPaint.Light(accent.Value, 0.6f) : Color.Gainsboro;
        Font = new Font("Segoe UI", 9f, accent.HasValue ? FontStyle.Bold : FontStyle.Regular);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(4, 2, 4, 2);
        Margin = new Padding(2);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }
}

// Timeline with colored note markers; click or drag to seek.
sealed class SeekBar : Control
{
    public long LengthMs;
    public long ValueMs;
    public readonly List<(long ms, Color color)> Markers = new();
    public event Action<long> SeekRequested;
    long lastSeekTick;

    public SeekBar()
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 26;
        Cursor = Cursors.Hand;
    }

    Rectangle Track => new(6, Height / 2 - 4, Math.Max(1, Width - 12), 8);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(30, 30, 30));
        var track = Track;
        using (var b = new SolidBrush(Color.FromArgb(70, 70, 70))) g.FillRectangle(b, track);
        if (LengthMs > 0)
        {
            int x = track.X + (int)(track.Width * Math.Clamp((double)ValueMs / LengthMs, 0, 1));
            using (var b = new SolidBrush(Color.FromArgb(200, 200, 200))) g.FillRectangle(b, track.X, track.Y, x - track.X, track.Height);
            foreach (var (ms, color) in Markers)
            {
                int mx = track.X + (int)(track.Width * Math.Clamp((double)ms / LengthMs, 0, 1));
                using var mb = new SolidBrush(color);
                g.FillRectangle(mb, mx - 1, 1, 3, Height - 2);
            }
            using var knob = new SolidBrush(Color.White);
            g.FillEllipse(knob, x - 6, Height / 2 - 6, 12, 12);
        }
    }

    void SeekTo(int x, bool force)
    {
        if (LengthMs <= 0) return;
        var track = Track;
        var ms = (long)(LengthMs * Math.Clamp((double)(x - track.X) / track.Width, 0, 1));
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
        if (e.Button == MouseButtons.Left) SeekTo(e.X, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) SeekTo(e.X, false);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) SeekTo(e.X, true);
    }
}

// Small dialog to change a note's type and text.
sealed class EditNoteDialog : Form
{
    readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly TextBox text = new() { Width = 420 };

    public string Kind => Kinds.All[kind.SelectedIndex];
    public string NoteText => text.Text;

    public EditNoteDialog(Note note)
    {
        Text = "Notu düzenle — " + NoteStore.Fmt(note.T);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(10);

        foreach (var k in Kinds.All) kind.Items.Add(Kinds.Label(k));
        kind.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds.All, note.Kind));
        text.Text = note.Text;

        var ok = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "Tür:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(kind, 1, 0);
        layout.Controls.Add(new Label { Text = "Not:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(text, 1, 1);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 1, 2);
        Controls.Add(layout);
    }
}
