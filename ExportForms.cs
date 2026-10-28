namespace KareNot;

// Base of the dark dialogs (export, settings, note editing, session info): content goes into Grid (two columns), buttons into
// the bottom row. Same palette and dark title bar as the main window.
class DarkDialog : Form
{
    protected readonly TableLayoutPanel Grid = new() { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
    readonly FlowLayoutPanel buttons = new() { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 16, 0, 0) };

    protected DarkDialog(string title)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Padding = new Padding(0, 0, 18, 16);
        var outer = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(18, 16) };
        outer.Controls.Add(Grid, 0, 0);
        outer.Controls.Add(buttons, 0, 1);
        Controls.Add(outer);
        var cancel = AddButton("Vazgeç", DialogResult.Cancel);
        CancelButton = cancel;
        ResumeLayout(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected Button AddButton(string text, DialogResult result)
    {
        var b = new DialogButton(text) { DialogResult = result, Primary = result == DialogResult.OK };
        buttons.Controls.Add(b);
        if (result == DialogResult.OK) AcceptButton = b;
        return b;
    }

    protected static Button StyledButton(string text) => new DialogButton(text) { Margin = new Padding(6, 3, 0, 3) };

    protected static Label Lbl(string text, Color? color = null, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? (bold ? Theme.Text : Theme.Muted), Anchor = AnchorStyles.Left, UseMnemonic = false,
        Margin = new Padding(3, bold ? 12 : 7, 14, bold ? 4 : 3), Font = bold ? Theme.Title : null,
    };

    protected void AddRow(Control left, Control right = null)
    {
        int row = Grid.RowCount++;
        Grid.Controls.Add(left, 0, row);
        if (right != null) Grid.Controls.Add(right, 1, row);
        else Grid.SetColumnSpan(left, 2);
    }

    protected static T Dark<T>(T c) where T : Control
    {
        if (c is TextBox or NumericUpDown or ComboBox) { c.BackColor = Theme.Field; c.ForeColor = Theme.Text; }
        if (c is TextBox t) t.BorderStyle = BorderStyle.FixedSingle;
        if (c is NumericUpDown n) n.BorderStyle = BorderStyle.FixedSingle;
        if (c is ComboBox) Theme.DarkScrollBars(c, "DarkMode_CFD");   // dark closed box and drop-down list
        if (c is ButtonBase b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Mix(Theme.Surface, Theme.Text, 0.4f);
            b.FlatAppearance.CheckedBackColor = Theme.Surface;
        }
        if (c is CheckBox or RadioButton) { c.ForeColor = Theme.Text; c.AutoSize = true; c.Margin = new Padding(3, 5, 3, 3); }
        return c;
    }

    // A text box in the rounded dark field frame.
    protected static FieldHost Field(TextBox box) => new(box) { BaseHeight = 30 };
}

// Focusable dialog button (Enter / Esc work through AcceptButton / CancelButton), rounded; Primary fills it with the accent.
sealed class DialogButton : Button
{
    bool hover;
    public bool Primary;

    public DialogButton(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Text = text;
        AutoSize = true;
        MinimumSize = new Size(88, 32);
        Padding = new Padding(12, 0, 12, 0);
        Margin = new Padding(8, 0, 0, 0);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
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
        float s = DeviceDpi / 96f;
        var r = new Rectangle(0, 0, Width, Height);
        var fill = Primary ? (hover ? Theme.Mix(Theme.Accent, Color.White, 0.15f) : Theme.Accent) : Theme.Mix(back, Theme.Text, hover ? 0.1f : 0.055f);
        Theme.FillRound(g, fill, r, 6 * s);
        if (!Primary) Theme.DrawRound(g, Theme.Mix(back, Theme.Text, 0.15f), r, 6 * s);
        if (Focused && ShowFocusCues) Theme.DrawRound(g, Primary ? Theme.Text : Theme.Accent, Rectangle.Inflate(r, -2, -2), 5 * s, 1.5f * s);
        TextRenderer.DrawText(g, Text, Font, r, !Enabled ? Theme.Faint : Primary ? Theme.OnAccent : Theme.Text, Theme.Line | TextFormatFlags.HorizontalCenter);
    }
}

// Options of the findings package (ZIP).
sealed class PackageDialog : DarkDialog
{
    readonly RadioButton thisVideo = Dark(new RadioButton { Text = "Bu video" });
    readonly RadioButton allVideos = Dark(new RadioButton { Text = "Tüm videolar" });
    readonly CheckBox gameOnly = Dark(new CheckBox { Text = "Yalnız oyun testi notları (kurgu işaretleri hariç)" });
    readonly CheckBox openOnly = Dark(new CheckBox { Text = "Yalnız açık olanlar (düzeltilenler hariç)" });
    readonly CheckBox media = Dark(new CheckBox { Text = "SS ve klipleri ekle", Checked = true });

    public bool AllVideos => allVideos.Checked;
    public bool GameOnly => gameOnly.Checked;
    public bool OpenOnly => openOnly.Checked;
    public bool IncludeMedia => media.Checked;

    public PackageDialog(string currentVideo)
        : base("Bulgu paketi (ZIP)")
    {
        thisVideo.Enabled = currentVideo != null;
        if (currentVideo != null) thisVideo.Text = "Bu video: " + currentVideo;
        (currentVideo != null ? thisVideo : allVideos).Checked = true;
        var scope = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0) };
        scope.Controls.AddRange(new Control[] { thisVideo, allVideos });
        AddRow(Lbl("Kapsam", bold: true));
        AddRow(scope);
        AddRow(Lbl("Süzgeç", bold: true));
        AddRow(gameOnly);
        AddRow(openOnly);
        AddRow(media);
        AddRow(Lbl("İçerik: rapor.md, rapor.html, notlar.csv, agent_gorevleri.md, kurgu notları (JSON + md) ve ss\\ dosyaları.\n" +
            "Kayıt yeri: bu video için KareNot\\<video>\\cikti, tüm videolar için KareNot\\cikti", Theme.Faint));
        AddButton("Paketi oluştur", DialogResult.OK);
    }
}

// Edits AppSettings; the caller saves.
sealed class SettingsDialog : DarkDialog
{
    readonly AppSettings settings;
    readonly NumericUpDown clipBefore = Num(), clipAfter = Num(), loopBefore = Num(), loopAfter = Num(), beep = Num(0.1m, 10), deadAir = Num(1, 600);
    readonly TextBox ffmpeg = Dark(new TextBox { Width = 380 }), whisper = Dark(new TextBox { Width = 380 }), model = Dark(new TextBox { Width = 380 });
    readonly TextBox dataFolder = Dark(new TextBox { Width = 380 });
    readonly Label ffmpegHint = Lbl("", Theme.Faint), whisperHint = Lbl("", Theme.Faint), dataHint = Lbl("", Theme.Faint);

    static NumericUpDown Num(decimal min = 0, decimal max = 120) =>
        Dark(new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = 1, Increment = 0.5m, Width = 80, Margin = new Padding(3, 4, 3, 2) });

    static void Set(NumericUpDown n, double v) => n.Value = Math.Clamp((decimal)v, n.Minimum, n.Maximum);

    public SettingsDialog(AppSettings settings)
        : base("Ayarlar")
    {
        this.settings = settings;
        Set(clipBefore, settings.ClipBefore);
        Set(clipAfter, settings.ClipAfter);
        Set(loopBefore, settings.LoopBefore);
        Set(loopAfter, settings.LoopAfter);
        Set(beep, settings.BeepDefault);
        Set(deadAir, settings.DeadAirMin);
        ffmpeg.Text = settings.FfmpegPath;
        whisper.Text = settings.WhisperPath;
        model.Text = settings.WhisperModel;
        dataFolder.Text = settings.DataFolder.Length > 0 ? settings.DataFolder : Paths.DefaultRoot;

        AddRow(Lbl("Notlar", bold: true));
        AddRow(Lbl("Notların kaydedileceği klasör"), BrowseFolder(dataFolder));
        AddRow(new Label(), dataHint);
        dataFolder.TextChanged += (s, e) => UpdateHints();
        Shown += (s, e) => dataFolder.Select(0, 0);
        AddRow(Lbl("Klip ve döngü (saniye)", bold: true));
        AddRow(Lbl("Klip: nottan önce / sonra"), Pair(clipBefore, clipAfter));
        AddRow(Lbl("Döngü: nottan önce / sonra"), Pair(loopBefore, loopAfter));
        AddRow(Lbl("Kurgu", bold: true));
        AddRow(Lbl("Bitişi olmayan BİP uzunluğu (sn)"), beep);
        AddRow(Lbl("Ölü an için en az (sn)"), deadAir);
        AddRow(Lbl("Araçlar", bold: true));
        AddRow(Lbl("ffmpeg.exe (boşsa PATH)"), Browse(ffmpeg, "ffmpeg|ffmpeg.exe|Programlar|*.exe"));
        AddRow(new Label(), ffmpegHint);
        AddRow(Lbl("whisper-cli.exe (whisper.cpp)"), Browse(whisper, "whisper-cli|whisper-cli.exe|Programlar|*.exe"));
        AddRow(Lbl("Whisper modeli (ggml .bin)"), Browse(model, "ggml model|*.bin|Tüm dosyalar|*.*"));
        AddRow(new Label(), whisperHint);
        ffmpeg.TextChanged += (s, e) => UpdateHints();
        whisper.TextChanged += (s, e) => UpdateHints();
        model.TextChanged += (s, e) => UpdateHints();
        UpdateHints();
        AddButton("Kaydet", DialogResult.OK);
    }

    static FlowLayoutPanel Pair(Control a, Control b)
    {
        var f = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        f.Controls.AddRange(new[] { a, Lbl("/"), b });
        return f;
    }

    FlowLayoutPanel Browse(TextBox box, string filter)
    {
        var b = StyledButton("Gözat…");
        b.Click += (s, e) =>
        {
            using var dlg = new OpenFileDialog { Filter = filter };
            try { if (File.Exists(box.Text)) dlg.InitialDirectory = Path.GetDirectoryName(box.Text); } catch { }
            if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
        };
        var f = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        var field = Field(box);
        f.Controls.AddRange(new Control[] { field, b });
        return f;
    }

    FlowLayoutPanel BrowseFolder(TextBox box)
    {
        var pick = StyledButton("Seç…");
        pick.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Notların kaydedileceği klasör", UseDescriptionForTitle = true };
            try { if (Directory.Exists(box.Text)) dlg.InitialDirectory = box.Text; } catch { }
            if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
        };
        var reset = StyledButton("Varsayılan");
        reset.Click += (s, e) => box.Text = Paths.DefaultRoot;
        var f = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        f.Controls.AddRange(new Control[] { Field(box), pick, reset });
        return f;
    }

    // The chosen notes folder, "" for the default one.
    string ChosenDataFolder()
    {
        var path = dataFolder.Text.Trim();
        if (path.Length > 0 && !Path.IsPathFullyQualified(path)) return path;
        try { return path.Length == 0 || Paths.SamePath(path, Paths.DefaultRoot) ? "" : Path.GetFullPath(path); }
        catch { return path; }
    }

    void UpdateHints()
    {
        var chosen = ChosenDataFolder();
        bool rooted = chosen.Length == 0 || Path.IsPathFullyQualified(chosen);
        SetHint(dataHint, rooted ? null : false, !rooted ? "Tam bir klasör yolu yaz (örneğin D:\\Notlar)." : chosen.Length == 0
            ? (Paths.SamePath(Paths.DefaultRoot, Paths.AppRoot) ? "Varsayılan: uygulamanın yanındaki KareNot Notlar klasörü." : "Varsayılan (uygulamanın klasörüne yazılamıyor).")
            : "Her video bu klasörün içinde kendi adıyla bir klasör alır.");
        var path = ffmpeg.Text.Trim();
        if (path.Length > 0)
            SetHint(ffmpegHint, File.Exists(path), File.Exists(path) ? "ffmpeg bulundu." : "Bu dosya yok.");
        else
        {
            var found = Ffmpeg.FindOnPath();
            SetHint(ffmpegHint, found != null, found != null ? "PATH'te bulundu: " + found : "PATH'te ffmpeg yok. Kur: winget install Gyan.FFmpeg");
        }
        var w = whisper.Text.Trim();
        var m = model.Text.Trim();
        if (w.Length == 0 && m.Length == 0) SetHint(whisperHint, null, "Konuşma tanıma için whisper-cli.exe ve bir ggml modeli seç (isteğe bağlı).");
        else if (!File.Exists(w)) SetHint(whisperHint, false, "whisper-cli.exe bulunamadı.");
        else if (!File.Exists(m)) SetHint(whisperHint, false, "Model dosyası bulunamadı.");
        else SetHint(whisperHint, true, "Whisper hazır.");
    }

    static void SetHint(Label l, bool? ok, string text)
    {
        l.Text = text;
        l.ForeColor = ok switch { true => Theme.Ok, false => Theme.Mix(Theme.Error, Theme.Text, 0.2f), _ => Theme.Faint };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult != DialogResult.OK) return;
        var chosen = ChosenDataFolder();
        if (chosen.Length > 0 && !Path.IsPathFullyQualified(chosen))
        {
            dataFolder.Focus();
            e.Cancel = true;
            return;
        }
        settings.DataFolder = chosen;
        settings.ClipBefore = (double)clipBefore.Value;
        settings.ClipAfter = (double)clipAfter.Value;
        settings.LoopBefore = (double)loopBefore.Value;
        settings.LoopAfter = (double)loopAfter.Value;
        settings.BeepDefault = (double)beep.Value;
        settings.DeadAirMin = (double)deadAir.Value;
        settings.FfmpegPath = ffmpeg.Text.Trim();
        settings.WhisperPath = whisper.Text.Trim();
        settings.WhisperModel = model.Text.Trim();
    }
}
