using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;

namespace KareNot;

sealed partial class MainForm : Form
{
    static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".ts", ".m4v", ".wmv" };
    static readonly float[] Rates = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f };
    static readonly Color Back = Color.FromArgb(30, 30, 30);
    static readonly Color PanelBack = Color.FromArgb(37, 37, 38);
    static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoNotlari");

    readonly NoteStore store = new();
    readonly string startVideo;
    VlcPlayer player;
    readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 100 };

    // Layout
    readonly SplitContainer split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, BackColor = Color.FromArgb(60, 60, 60), SplitterWidth = 5 };
    readonly Panel videoHost = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    readonly Panel videoPanel = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    readonly FrameOverlay overlay = new() { Dock = DockStyle.Fill, Visible = false };
    readonly Label hint = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gray, BackColor = Color.Black,
        Font = new Font("Segoe UI", 13f), Text = "Video aç (Ctrl+O) ya da videoyu buraya sürükle-bırak\n\nKısayollar: F1",
    };
    readonly SeekBar seek = new() { Dock = DockStyle.Top };
    readonly Label timeLabel = new() { AutoSize = true, ForeColor = Color.Gainsboro, Font = new Font("Consolas", 10.5f), Margin = new Padding(8, 7, 8, 0) };
    readonly ComboBox rateBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62, Margin = new Padding(4, 4, 4, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.Gainsboro };
    FlatButton playButton, panelButton;

    readonly TextBox noteBox = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), BackColor = Color.FromArgb(50, 50, 52), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly CheckBox shotCheck = new() { Text = "SS ekle", Checked = true, AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(3, 6, 10, 0) };
    readonly Label regionLabel = new() { AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(3, 6, 3, 0), Text = "Alan seçmek için videoda sürükle" };
    readonly ListView gameList = NoteList(), editList = NoteList();
    readonly Label status = new() { Dock = DockStyle.Bottom, ForeColor = Color.Silver, AutoSize = false, Height = 44, Padding = new Padding(2, 6, 2, 2) };

    // Side panel tabs: game test notes, video editing marks and the shortcut reference.
    enum PanelTab { Game, Edit, Shortcuts }
    readonly TableLayoutPanel gamePage = NotePage(), editPage = NotePage();
    readonly Panel shortcutsPage = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = PanelBack, Visible = false };
    readonly Dictionary<PanelTab, FlatButton> tabButtons = new();
    PanelTab tab = PanelTab.Game;
    PanelTab noteTab = PanelTab.Game;   // last notes tab; picks the list and timeline markers while the shortcuts show

    // State
    string currentVideo;
    bool fullscreen;
    FormWindowState windowStateBeforeFullscreen;
    int rateIndex = 3;
    int shotCounter;
    Point dragStart;
    bool mouseDown, dragging;
    long frozenMs;
    Note lastAdded;

    public MainForm(string startVideo)
    {
        this.startVideo = startVideo;
        Text = "KareNot";
        Size = new Size(1500, 860);
        MinimumSize = new Size(900, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Back;
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildLayout();
        store.Load();
    }

    void BuildLayout()
    {
        // Video area
        videoHost.Controls.Add(videoPanel);
        videoHost.Controls.Add(overlay);
        videoHost.Controls.Add(hint);
        hint.BringToFront();
        split.Panel1.Controls.Add(videoHost);
        split.Panel1.BackColor = Color.Black;
        split.Panel2.BackColor = PanelBack;
        split.Panel2.Padding = new Padding(8);

        foreach (Control c in new Control[] { videoPanel, overlay, hint })
        {
            c.MouseDown += VideoMouseDown;
            c.MouseMove += VideoMouseMove;
            c.MouseUp += VideoMouseUp;
            c.MouseWheel += (s, e) => ChangeVolume(e.Delta > 0 ? 10 : -10);
        }
        videoPanel.MouseDoubleClick += (s, e) => ToggleFullscreen();
        hint.MouseDoubleClick += (s, e) => OpenDialog();

        // Side panel: game test page
        AddRow(gamePage, new Label { Text = "Notu yaz, sonra türünü seç (o anki süreye kaydedilir):", AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(3, 0, 3, 3) });
        noteBox.Margin = new Padding(3, 0, 3, 3);
        AddRow(gamePage, noteBox);
        AddRow(gamePage, Flow(shotCheck, regionLabel));
        AddRow(gamePage, Flow(
            Btn("BUG  [B]", () => AddNote("BUG", false), Kinds.Color("BUG")),
            Btn("DEĞİŞİKLİK  [D]", () => AddNote("DEGISIKLIK", false), Kinds.Color("DEGISIKLIK")),
            Btn("NOT  [N]", () => AddNote("NOT", true), Kinds.Color("NOT"))));
        AddRow(gamePage, Flow(Btn("Seçilene SS ekle  [S]", ShotToSelected)));
        gameList.Columns.Add("Zaman", 82);
        gameList.Columns.Add("Tür", 100);
        gameList.Columns.Add("SS", 34);
        gameList.Columns.Add("Not", 200);
        var gameMenu = new ContextMenuStrip();
        gameMenu.Items.Add("Git (-2 sn)", null, (s, e) => GotoSelected());
        gameMenu.Items.Add("Düzenle (F2)", null, (s, e) => EditSelected());
        gameMenu.Items.Add("SS'i aç", null, (s, e) => OpenSelectedShot());
        gameMenu.Items.Add("Seçilene SS ekle (S)", null, (s, e) => ShotToSelected());
        gameMenu.Items.Add(new ToolStripSeparator());
        gameMenu.Items.Add("Sil (Del)", null, (s, e) => DeleteSelected());
        gameList.ContextMenuStrip = gameMenu;
        AddRow(gamePage, gameList, SizeType.Percent, 100);
        AddRow(gamePage, ListButtons());

        // Side panel: video editing page (no text box, no screenshots)
        AddRow(editPage, new Label { Text = "Kurgu işaretleri o anki süreye kaydedilir:", AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(3, 0, 3, 3) });
        AddRow(editPage, Flow(
            Btn("BİP (küfür)  [K]", () => AddNote("BIP", false), Kinds.Color("BIP")),
            Btn("Kesim BAŞLA  [1]", () => AddNote("KES_BASLA", false), Kinds.Color("KES_BASLA")),
            Btn("Kesim BİTİR  [2]", () => AddNote("KES_BITIR", false), Kinds.Color("KES_BITIR"))));
        editList.Columns.Add("Zaman", 82);
        editList.Columns.Add("Tür", 100);
        editList.Columns.Add("Not", 200);
        var editMenu = new ContextMenuStrip();
        editMenu.Items.Add("Git (-2 sn)", null, (s, e) => GotoSelected());
        editMenu.Items.Add("Düzenle (F2)", null, (s, e) => EditSelected());
        editMenu.Items.Add(new ToolStripSeparator());
        editMenu.Items.Add("Sil (Del)", null, (s, e) => DeleteSelected());
        editList.ContextMenuStrip = editMenu;
        AddRow(editPage, editList, SizeType.Percent, 100);
        AddRow(editPage, ListButtons());

        foreach (var l in new[] { gameList, editList })
        {
            l.DoubleClick += (s, e) => GotoSelected();
            l.Resize += (s, e) => FitNoteColumn((ListView)s);
        }

        // Fill pages first so the docked tab strip, footer and status line are laid out around them.
        BuildShortcutsPage();
        tabButtons[PanelTab.Game] = Btn("Oyun testi", () => ShowTab(PanelTab.Game));
        tabButtons[PanelTab.Edit] = Btn("Kurgu", () => ShowTab(PanelTab.Edit));
        tabButtons[PanelTab.Shortcuts] = Btn("Kısayollar  [F1]", () => ShowTab(PanelTab.Shortcuts));
        var tabs = Flow(tabButtons.Values.ToArray());
        tabs.Dock = DockStyle.Top;
        tabs.Padding = new Padding(0, 0, 0, 6);
        var footer = Flow(Btn("Raporu kaydet  [Ctrl+S]", SaveReport), Btn("Klasörü aç", OpenFolder));
        footer.Dock = DockStyle.Bottom;
        split.Panel2.Controls.Add(gamePage);
        split.Panel2.Controls.Add(editPage);
        split.Panel2.Controls.Add(shortcutsPage);
        split.Panel2.Controls.Add(tabs);
        split.Panel2.Controls.Add(footer);
        split.Panel2.Controls.Add(status);
        ShowTab(PanelTab.Game);

        // Bottom bar
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Back };
        seek.SeekRequested += ms => { ClearRegion(); player?.Seek(ms); };
        playButton = Btn("▶", () => { ClearRegion(); player?.TogglePause(); });
        playButton.Font = new Font("Segoe UI Symbol", 10f, FontStyle.Bold);
        playButton.MinimumSize = new Size(44, 0);
        foreach (var r in Rates) rateBox.Items.Add(r.ToString("0.##", CultureInfo.InvariantCulture) + "x");
        rateBox.SelectedIndex = rateIndex;
        rateBox.SelectionChangeCommitted += (s, e) => { SetRate(rateBox.SelectedIndex); ActiveControl = null; };
        panelButton = Btn("Paneli gizle  [Tab]", TogglePanel);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Back, WrapContents = false, Padding = new Padding(4, 2, 4, 0) };
        controls.Controls.AddRange(new Control[]
        {
            Btn("Aç  [Ctrl+O]", OpenDialog),
            Btn("◀ Önceki video", () => StepVideo(-1)),
            playButton,
            Btn("Sonraki video ▶", () => StepVideo(1)),
            Btn("-3 sn", () => SeekBy(-3000)),
            Btn("+3 sn", () => SeekBy(3000)),
            Btn("◁ kare", () => { ClearRegion(); player?.StepFrame(-1); }),
            Btn("kare ▷", () => { ClearRegion(); player?.StepFrame(1); }),
            timeLabel,
            new Label { Text = "Hız:", AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(8, 8, 0, 0) },
            rateBox,
            panelButton,
            Btn("Tam ekran  [F]", ToggleFullscreen),
            Btn("?", ShowHelp),
        });
        bottom.Controls.Add(controls);
        bottom.Controls.Add(seek);

        Controls.Add(split);
        Controls.Add(bottom);

        // Drag and drop of video files anywhere on the window.
        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            c.DragDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) OpenVideo(files[0]);
            };
            foreach (Control child in c.Controls) EnableDrop(child);
        }
        EnableDrop(this);

        noteBox.Enter += (s, e) => player?.Pause();
        timeLabel.Text = "00:00:00.0 / 00:00:00.0";
    }

    static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), BackColor = Color.Transparent };
        flow.Controls.AddRange(controls);
        return flow;
    }

    static FlatButton Btn(string text, Action action, Color? accent = null)
    {
        var b = new FlatButton(text, accent);
        b.Click += (s, e) => action();
        return b;
    }

    static TableLayoutPanel NotePage() => new() { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = PanelBack, Visible = false };

    static ListView NoteList() => new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
        BackColor = Color.FromArgb(28, 28, 30), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None,
        Font = new Font("Segoe UI", 9.5f), HeaderStyle = ColumnHeaderStyle.Nonclickable, Margin = new Padding(3, 6, 3, 3),
    };

    static void AddRow(TableLayoutPanel page, Control c, SizeType type = SizeType.AutoSize, float height = 0)
    {
        page.RowStyles.Add(new RowStyle(type, height));
        page.Controls.Add(c, 0, page.RowStyles.Count - 1);
    }

    FlowLayoutPanel ListButtons() => Flow(
        Btn("Git (-2 sn)  [G]", GotoSelected),
        Btn("Düzenle  [F2]", EditSelected),
        Btn("Seçileni sil  [Del]", DeleteSelected),
        Btn("Son notu sil  [Ctrl+Z]", DeleteLast));

    protected override void OnShown(EventArgs e)
    {
        LayoutPanel();
        Resize += (s, ev) => { if (!panelLaidOut) LayoutPanel(); };
        FitNoteColumn(gameList);
        FitNoteColumn(editList);
        try
        {
            player = new VlcPlayer(videoPanel.Handle);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "KareNot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        uiTimer.Tick += (s, ev) => UpdatePlayerUi();
        uiTimer.Start();
        RefreshList();
        if (startVideo != null && File.Exists(startVideo)) OpenVideo(startVideo);
        base.OnShown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        uiTimer.Stop();
        player?.Dispose();
        player = null;
        base.OnFormClosed(e);
    }

    // ---------- Player ----------

    void OpenDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Video aç",
            Filter = "Videolar|" + string.Join(";", VideoExtensions.Select(x => "*" + x)) + "|Tüm dosyalar|*.*",
        };
        var last = LoadSetting("son_klasor.txt");
        if (last != null && Directory.Exists(last)) dlg.InitialDirectory = last;
        else dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenVideo(dlg.FileName);
    }

    void OpenVideo(string path)
    {
        if (player == null || !File.Exists(path)) return;
        ClearRegion();
        try
        {
            player.Open(path);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            return;
        }
        player.SetRate(Rates[rateIndex]);
        currentVideo = Path.GetFileName(path);
        hint.Visible = false;
        Text = "KareNot — " + currentVideo;
        SaveSetting("son_klasor.txt", Path.GetDirectoryName(path));
        lastAdded = null;
        RefreshList();
    }

    void StepVideo(int direction)
    {
        if (player?.FilePath == null) return;
        var dir = Path.GetDirectoryName(player.FilePath);
        var files = Directory.GetFiles(dir)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        int i = files.FindIndex(f => string.Equals(f, player.FilePath, StringComparison.OrdinalIgnoreCase));
        int next = i + direction;
        if (next < 0 || next >= files.Count)
        {
            SetStatus(direction > 0 ? "Klasörde sonraki video yok." : "Klasörde önceki video yok.");
            return;
        }
        OpenVideo(files[next]);
    }

    void SeekBy(long deltaMs)
    {
        if (player == null) return;
        ClearRegion();
        player.Seek(player.TimeMs + deltaMs);
    }

    void SetRate(int index)
    {
        rateIndex = Math.Clamp(index, 0, Rates.Length - 1);
        rateBox.SelectedIndex = rateIndex;
        player?.SetRate(Rates[rateIndex]);
        SetStatus("Hız: " + rateBox.Text);
    }

    void ChangeVolume(int delta)
    {
        if (player == null || !player.HasMedia) return;
        player.Volume += delta;
        SetStatus("Ses: %" + player.Volume);
    }

    void UpdatePlayerUi()
    {
        if (player == null) return;
        player.Tick();
        long t = overlay.Visible ? frozenMs : player.TimeMs;
        long length = player.LengthMs;
        seek.LengthMs = length;
        seek.ValueMs = t;
        seek.Invalidate();
        timeLabel.Text = NoteStore.Fmt(t / 1000.0) + " / " + NoteStore.Fmt(length / 1000.0);
        var text = player.IsPlaying ? "❚❚" : "▶";
        if (playButton.Text != text) playButton.Text = text;
    }

    void ToggleFullscreen()
    {
        fullscreen = !fullscreen;
        SuspendLayout();
        if (fullscreen)
        {
            windowStateBeforeFullscreen = WindowState;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = windowStateBeforeFullscreen;
        }
        ResumeLayout();
    }

    void TogglePanel()
    {
        split.Panel2Collapsed = !split.Panel2Collapsed;
        panelButton.Text = split.Panel2Collapsed ? "Paneli göster  [Tab]" : "Paneli gizle  [Tab]";
        if (split.Panel2Collapsed && (noteBox.Focused || ListFocused)) ActiveControl = null;
    }

    // ---------- Area selection on the video ----------

    void VideoMouseDown(object sender, MouseEventArgs e)
    {
        ActiveControl = null;
        if (e.Button != MouseButtons.Left || player == null || !player.HasMedia) return;
        mouseDown = true;
        dragStart = e.Location;
        if (overlay.Visible)
        {
            dragging = true;
            overlay.Selection = Rectangle.Empty;
            overlay.Invalidate();
        }
    }

    void VideoMouseMove(object sender, MouseEventArgs e)
    {
        if (!mouseDown) return;
        if (!dragging)
        {
            if (Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) < 8) return;
            if (!FreezeFrame()) { mouseDown = false; return; }
            dragging = true;
        }
        UpdateSelection(dragStart, e.Location);
    }

    void VideoMouseUp(object sender, MouseEventArgs e)
    {
        if (!mouseDown) return;
        mouseDown = false;
        if (!dragging) return;
        dragging = false;
        UpdateSelection(dragStart, e.Location);
        var s = overlay.Selection;
        if (s.Width < 4 || s.Height < 4)
        {
            ClearRegion();
            return;
        }
        regionLabel.Text = "Alan seçili (Esc: iptal)";
        regionLabel.ForeColor = Color.FromArgb(255, 90, 90);
        SetStatus("Alan seçildi: notu yaz ve türünü seç. SS'te kırmızı çerçeveyle işaretlenir.");
        if (!split.Panel2Collapsed) FocusNoteBox();
    }

    void UpdateSelection(Point a, Point b)
    {
        var p1 = overlay.ClientToImage(a);
        var p2 = overlay.ClientToImage(b);
        overlay.Selection = Rectangle.FromLTRB(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
        overlay.Invalidate();
    }

    // Pauses and shows the current frame as a still image to draw the selection on.
    bool FreezeFrame()
    {
        player.Pause();
        var temp = Path.Combine(Path.GetTempPath(), "karenot_kare.png");
        if (!player.TakeSnapshot(temp, out var error))
        {
            SetStatus("Alan seçilemedi: " + error);
            return false;
        }
        using (var fs = File.OpenRead(temp))
        using (var img = Image.FromStream(fs))
        {
            overlay.Frame?.Dispose();
            overlay.Frame = new Bitmap(img);
        }
        frozenMs = player.TimeMs;
        overlay.Selection = Rectangle.Empty;
        overlay.Visible = true;
        overlay.BringToFront();
        return true;
    }

    bool RegionActive => overlay.Visible && overlay.Frame != null;

    void ClearRegion()
    {
        mouseDown = dragging = false;
        if (!overlay.Visible) return;
        overlay.Visible = false;
        overlay.Frame?.Dispose();
        overlay.Frame = null;
        overlay.Selection = Rectangle.Empty;
        regionLabel.Text = "Alan seçmek için videoda sürükle";
        regionLabel.ForeColor = Color.Silver;
    }

    // ---------- Notes ----------

    static bool KindTakesShot(string kind) => kind is "BUG" or "DEGISIKLIK" or "NOT";

    // Saves the current frame (or the selected area marked on the frozen frame); returns the file name or null.
    string MakeShot(string kind, double t, out string error)
    {
        error = null;
        Directory.CreateDirectory(Paths.ShotsDir);
        var name = string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyyMMdd_HHmmss}_{2}s_{3:00}.png",
            kind.ToLowerInvariant(), DateTime.Now, (int)t, ++shotCounter);
        var path = Path.Combine(Paths.ShotsDir, name);
        if (RegionActive)
        {
            using var marked = overlay.RenderMarked();
            marked.Save(path, ImageFormat.Png);
            return name;
        }
        return player.TakeSnapshot(path, out error) ? name : null;
    }

    void AddNote(string kind, bool requireText)
    {
        if (player == null || !player.HasMedia)
        {
            SetStatus("Önce bir video aç.");
            return;
        }
        // Editing marks only take the text box when it is being typed in, so a game note draft is not swallowed.
        bool useBox = !Kinds.IsEdit(kind) || Typing;
        var text = useBox ? NoteStore.Clean(noteBox.Text) : "";
        if (requireText && text.Length == 0)
        {
            SetStatus("NOT için önce kutuya yaz (Enter), sonra N / Enter.");
            FocusNoteBox();
            return;
        }

        double t = (RegionActive ? frozenMs : player.TimeMs) / 1000.0;
        string shot = null, shotError = null;
        if (KindTakesShot(kind) && (shotCheck.Checked || RegionActive)) shot = MakeShot(kind, t, out shotError);

        lastAdded = store.Add(t, kind, text, currentVideo, shot);
        if (useBox) noteBox.Clear();
        ClearRegion();
        if (noteBox.Focused) ActiveControl = null;
        ShowTab(Kinds.IsEdit(kind) ? PanelTab.Edit : PanelTab.Game);
        RefreshList(lastAdded);
        SetStatus(shotError != null
            ? "Not kaydedildi, SS yok: " + shotError
            : $"{Kinds.Label(kind)} eklendi — {NoteStore.Fmt(t)}{(shot != null ? " (SS)" : "")}");
    }

    ListView ActiveList => noteTab == PanelTab.Edit ? editList : gameList;
    bool ListFocused => gameList.Focused || editList.Focused;
    Note SelectedNote => ActiveList.SelectedItems.Count > 0 ? ActiveList.SelectedItems[0].Tag as Note : null;

    void ShotToSelected()
    {
        var n = SelectedNote;
        if (n == null)
        {
            SetStatus("Önce listeden bir not seç (istersen 'Git' ile o kareye git).");
            return;
        }
        if (Kinds.IsEdit(n.Kind))
        {
            SetStatus("Kurgu işaretlerine SS eklenmez.");
            return;
        }
        if (player == null || !player.HasMedia) return;
        double t = (RegionActive ? frozenMs : player.TimeMs) / 1000.0;
        var shot = MakeShot(n.Kind, t, out var error);
        if (shot == null)
        {
            SetStatus("SS eklenmedi: " + error);
            return;
        }
        n.Shot = shot;
        store.Save(n.Video);
        ClearRegion();
        RefreshList(n);
        SetStatus("SS seçili nota eklendi.");
    }

    void GotoSelected()
    {
        var n = SelectedNote;
        if (n == null || player == null) return;
        ClearRegion();
        player.Seek((long)((n.T - 2) * 1000));
    }

    void EditSelected()
    {
        var n = SelectedNote;
        if (n == null) return;
        using var dlg = new EditNoteDialog(n);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        n.Kind = dlg.Kind;
        n.Text = NoteStore.Clean(dlg.NoteText);
        store.Save(n.Video);
        RefreshList(n);
        SetStatus("Not güncellendi.");
    }

    void OpenSelectedShot()
    {
        var n = SelectedNote;
        var file = n == null ? null : NoteStore.ResolveShot(n.Shot);
        if (file == null)
        {
            SetStatus("Seçili notun SS'i yok.");
            return;
        }
        Process.Start(new ProcessStartInfo(Path.Combine(Paths.ShotsDir, file)) { UseShellExecute = true });
    }

    void DeleteSelected()
    {
        var n = SelectedNote;
        if (n == null)
        {
            SetStatus("Silmek için listeden bir not seç.");
            return;
        }
        var list = ActiveList;
        int index = list.SelectedIndices[0];
        store.Remove(n);
        if (n == lastAdded) lastAdded = null;
        RefreshList();
        if (list.Items.Count > 0) list.Items[Math.Min(index, list.Items.Count - 1)].Selected = true;
        SetStatus("Not silindi: " + NoteStore.Fmt(n.T) + " " + Kinds.Label(n.Kind));
    }

    // Removes the note added last in this session; otherwise the one of the open tab closest to the current position.
    void DeleteLast()
    {
        var notes = CurrentNotes();
        var n = lastAdded != null && notes.Contains(lastAdded) ? lastAdded : null;
        var tabNotes = notes.Where(InNoteTab).ToList();
        if (n == null && tabNotes.Count > 0 && player != null)
        {
            double t = player.TimeMs / 1000.0;
            n = tabNotes.OrderBy(x => Math.Abs(x.T - t)).First();
        }
        if (n == null) return;
        store.Remove(n);
        lastAdded = null;
        RefreshList();
        SetStatus("Not silindi: " + NoteStore.Fmt(n.T) + " " + Kinds.Label(n.Kind));
    }

    List<Note> CurrentNotes() => currentVideo == null ? new List<Note>() : store.All.Where(n => n.Video == currentVideo).OrderBy(n => n.T).ToList();

    bool InNoteTab(Note n) => Kinds.IsEdit(n.Kind) == (noteTab == PanelTab.Edit);

    void RefreshList(Note select = null)
    {
        var notes = CurrentNotes();
        FillList(gameList, notes.Where(n => !Kinds.IsEdit(n.Kind)), select, true);
        FillList(editList, notes.Where(n => Kinds.IsEdit(n.Kind)), select, false);
        RefreshMarkers();

        if (currentVideo == null)
            SetStatus($"Toplam {store.All.Count} not. Not dosyası: {Paths.CombinedFile}");
        else
            SetStatus($"{notes.Count(n => !Kinds.IsEdit(n.Kind))} not ({notes.Count(n => n.Kind == "BUG")} bug, {notes.Count(n => n.Kind == "DEGISIKLIK")} değişiklik) · " +
                $"kurgu: {notes.Count(n => n.Kind == "BIP")} bip, {notes.Count(n => n.Kind == "KES_BASLA")} kesim — bu video");
    }

    static void FillList(ListView list, IEnumerable<Note> notes, Note select, bool withShot)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var n in notes)
        {
            var item = new ListViewItem(NoteStore.Fmt(n.T)) { Tag = n, UseItemStyleForSubItems = false };
            var kind = item.SubItems.Add(Kinds.Label(n.Kind));
            kind.ForeColor = ControlPaint.Light(Kinds.Color(n.Kind), 0.5f);
            kind.Font = new Font(list.Font, FontStyle.Bold);
            if (withShot) item.SubItems.Add(n.Shot != null ? "SS" : "");
            item.SubItems.Add(n.Text);
            list.Items.Add(item);
            if (n == select)
            {
                item.Selected = true;
                item.EnsureVisible();
            }
        }
        list.EndUpdate();
    }

    // The timeline shows the marks of the open notes tab only.
    void RefreshMarkers()
    {
        seek.Markers.Clear();
        foreach (var n in CurrentNotes().Where(InNoteTab)) seek.Markers.Add(((long)(n.T * 1000), Kinds.Color(n.Kind)));
        seek.Invalidate();
    }

    bool panelLaidOut;

    // Needs a real window size; a minimized start lays the panel out on the first restore.
    void LayoutPanel()
    {
        if (split.Width < 800) return;
        split.Panel2MinSize = 320;
        split.SplitterDistance = Math.Max(200, split.Width - 470);
        panelLaidOut = true;
    }

    static void FitNoteColumn(ListView list)
    {
        int last = list.Columns.Count - 1, used = 0;
        for (int i = 0; i < last; i++) used += list.Columns[i].Width;
        list.Columns[last].Width = Math.Max(120, list.ClientSize.Width - used - 4);
    }

    void SaveReport()
    {
        try
        {
            var path = store.WriteReport(out var missing);
            SetStatus("Rapor kaydedildi: " + path + (missing > 0 ? $" ({missing} SS bulunamadı)" : ""));
        }
        catch (Exception ex)
        {
            SetStatus("HATA: rapor yazılamadı: " + ex.Message);
        }
    }

    void OpenFolder()
    {
        Directory.CreateDirectory(Paths.WorkDir);
        Process.Start(new ProcessStartInfo(Paths.WorkDir) { UseShellExecute = true });
    }

    void SetStatus(string text) => status.Text = text;

    // ---------- Side panel tabs ----------

    static readonly (string Title, (string Keys, string Action)[] Rows)[] ShortcutGroups =
    {
        ("OYNATICI", new[]
        {
            ("Boşluk", "Oynat / durdur"),
            ("← / →", "3 sn geri / ileri"),
            ("Shift + ← / →", "10 sn geri / ileri"),
            (", / .", "Bir kare geri / ileri"),
            ("- / +", "Hız (0.25x – 3x)"),
            ("↑ / ↓", "Ses (videoda fare tekerleği de olur)"),
            ("Page Up / Down", "Klasördeki önceki / sonraki video"),
            ("F, F11", "Tam ekran (videoya çift tık da olur)"),
            ("Esc", "Tam ekrandan çık"),
            ("Tab", "Not panelini aç / kapat"),
            ("Ctrl+O", "Video aç (sürükle-bırak da olur)"),
        }),
        ("OYUN TESTİ (yazı kutusu dışındayken)", new[]
        {
            ("B", "BUG"),
            ("D", "DEĞİŞİKLİK"),
            ("N", "NOT (önce kutuya yazılmalı)"),
            ("S", "Seçili nota o anki kareyi SS ekle"),
            ("Enter", "Videoyu durdur, yazı kutusuna geç"),
            ("Videoda sürükle", "Video durur, alan kırmızı çerçeveyle işaretlenir; notu yaz ve türünü seç"),
            ("Esc", "Alan seçimini iptal et"),
        }),
        ("YAZI KUTUSUNDAYKEN", new[]
        {
            ("Enter", "NOT olarak ekle"),
            ("Ctrl+B", "BUG olarak ekle"),
            ("Ctrl+D", "DEĞİŞİKLİK olarak ekle"),
            ("Ctrl+K", "Yazıyla birlikte BİP ekle"),
            ("Esc", "Kutudan çık"),
        }),
        ("KURGU", new[]
        {
            ("K", "BİP (küfür)"),
            ("1", "Kesim BAŞLA"),
            ("2", "Kesim BİTİR"),
        }),
        ("LİSTE (açık sekmenin listesi)", new[]
        {
            ("Çift tık, G", "Seçili nota git (2 sn önce)"),
            ("F2", "Düzenle (tür + yazı)"),
            ("Del", "Seçileni sil (seçim yoksa son notu)"),
            ("Ctrl+Z", "Son eklenen notu sil"),
            ("Sağ tık", "Menü (SS'i aç, düzenle, sil)"),
        }),
        ("SEKMELER", new[]
        {
            ("F1", "Kısayollar sekmesini aç / kapat"),
            ("Hepsi", "Kısayollar her sekmede çalışır; eklenen not kendi sekmesini açar"),
        }),
        ("RAPOR", new[]
        {
            ("Ctrl+S", "Markdown raporu kaydet (Masaüstü\\Video Notlari\\rapor.md)"),
        }),
    };

    void BuildShortcutsPage()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = PanelBack };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var keyFont = new Font("Segoe UI Semibold", 9.5f);
        var keyColor = Color.FromArgb(156, 220, 254);
        var keyLabels = new List<Label>();
        var actionLabels = new List<Label>();
        int row = 0;
        foreach (var (title, rows) in ShortcutGroups)
        {
            var header = new Label
            {
                Text = title, AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Margin = new Padding(3, row == 0 ? 2 : 14, 3, 4),
            };
            table.Controls.Add(header, 0, row);
            table.SetColumnSpan(header, 2);
            row++;
            foreach (var (keys, action) in rows)
            {
                var key = new Label { Text = keys, AutoSize = true, ForeColor = keyColor, Font = keyFont, Margin = new Padding(3, 2, 14, 2) };
                var text = new Label { Text = action, AutoSize = true, ForeColor = Color.Gainsboro, Margin = new Padding(3, 3, 3, 2) };
                table.Controls.Add(key, 0, row);
                table.Controls.Add(text, 1, row);
                keyLabels.Add(key);
                actionLabels.Add(text);
                row++;
            }
        }
        shortcutsPage.Controls.Add(table);

        // AutoSize labels only wrap with a maximum width; keep it in step with the panel width.
        void WrapActions()
        {
            int keyWidth = keyLabels.Max(l => l.PreferredWidth + l.Margin.Horizontal);
            int width = Math.Max(80, shortcutsPage.ClientSize.Width - keyWidth - SystemInformation.VerticalScrollBarWidth - 10);
            foreach (var l in actionLabels) l.MaximumSize = new Size(width, 0);
        }
        shortcutsPage.Resize += (s, e) => WrapActions();
        WrapActions();
    }

    // Switches the side panel page; does not open a hidden panel.
    void ShowTab(PanelTab show)
    {
        if (show != tab && (noteBox.Focused || ListFocused)) ActiveControl = null;
        tab = show;
        if (show != PanelTab.Shortcuts && show != noteTab)
        {
            noteTab = show;
            RefreshMarkers();
        }
        gamePage.Visible = show == PanelTab.Game;
        editPage.Visible = show == PanelTab.Edit;
        shortcutsPage.Visible = show == PanelTab.Shortcuts;
        foreach (var (key, button) in tabButtons) StyleTab(button, key == show);
    }

    static void StyleTab(FlatButton tab, bool active)
    {
        tab.BackColor = active ? Color.FromArgb(70, 70, 74) : Color.FromArgb(45, 45, 48);
        tab.FlatAppearance.BorderColor = active ? Color.Gainsboro : Color.FromArgb(90, 90, 90);
        tab.ForeColor = active ? Color.White : Color.Silver;
    }

    // "?" button and F1: show the shortcuts tab, or go back to the notes if it is already showing.
    void ShowHelp()
    {
        bool show = split.Panel2Collapsed || tab != PanelTab.Shortcuts;
        if (split.Panel2Collapsed) TogglePanel();
        ShowTab(show ? PanelTab.Shortcuts : noteTab);
    }

    void FocusNoteBox()
    {
        if (split.Panel2Collapsed) TogglePanel();
        ShowTab(PanelTab.Game);
        player?.Pause();
        noteBox.Focus();
    }

    // ---------- Keyboard ----------

    bool Typing => noteBox.Focused;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.O: OpenDialog(); return true;
            case Keys.Control | Keys.S: SaveReport(); return true;
            case Keys.Control | Keys.B: AddNote("BUG", false); return true;
            case Keys.Control | Keys.D: AddNote("DEGISIKLIK", false); return true;
            case Keys.Control | Keys.N: AddNote("NOT", true); return true;
            case Keys.Control | Keys.K: AddNote("BIP", false); return true;
            case Keys.Tab: TogglePanel(); return true;
            case Keys.F11: ToggleFullscreen(); return true;
            case Keys.F1: ShowHelp(); return true;
            case Keys.Escape:
                if (RegionActive) { ClearRegion(); SetStatus("Alan seçimi iptal."); }
                else if (Typing || ListFocused) ActiveControl = null;
                else if (fullscreen) ToggleFullscreen();
                return true;
        }

        if (Typing)
        {
            if (keyData == Keys.Enter)
            {
                AddNote("NOT", true);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        switch (keyData)
        {
            case Keys.Space: ClearRegion(); player?.TogglePause(); return true;
            case Keys.Left: SeekBy(-3000); return true;
            case Keys.Right: SeekBy(3000); return true;
            case Keys.Shift | Keys.Left: SeekBy(-10000); return true;
            case Keys.Shift | Keys.Right: SeekBy(10000); return true;
            case Keys.PageUp: StepVideo(-1); return true;
            case Keys.PageDown: StepVideo(1); return true;
            case Keys.Delete: if (SelectedNote != null) DeleteSelected(); else DeleteLast(); return true;
            case Keys.Control | Keys.Z: DeleteLast(); return true;
            case Keys.F2: EditSelected(); return true;
            case Keys.Up:
            case Keys.Down:
                if (ListFocused) break;
                ChangeVolume(keyData == Keys.Up ? 10 : -10);
                return true;
            case Keys.Enter:
                if (ListFocused) { GotoSelected(); return true; }
                FocusNoteBox();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (Typing || ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Alt)) return;
        e.Handled = true;
        switch (char.ToLowerInvariant(e.KeyChar))
        {
            case 'b': AddNote("BUG", false); break;
            case 'd': AddNote("DEGISIKLIK", false); break;
            case 'n': AddNote("NOT", true); break;
            case 'k': AddNote("BIP", false); break;
            case '1': AddNote("KES_BASLA", false); break;
            case '2': AddNote("KES_BITIR", false); break;
            case 's': ShotToSelected(); break;
            case 'g': GotoSelected(); break;
            case 'f': ToggleFullscreen(); break;
            case '.': ClearRegion(); player?.StepFrame(1); break;
            case ',': ClearRegion(); player?.StepFrame(-1); break;
            case '+': SetRate(rateIndex + 1); break;
            case '-': SetRate(rateIndex - 1); break;
            default: e.Handled = false; break;
        }
    }

    // ---------- Settings ----------

    static string LoadSetting(string name)
    {
        try { return File.ReadAllText(Path.Combine(SettingsDir, name)).Trim(); } catch { return null; }
    }

    static void SaveSetting(string name, string value)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(Path.Combine(SettingsDir, name), value);
        }
        catch { }
    }
}
