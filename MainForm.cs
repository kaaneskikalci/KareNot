using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace KareNot;

sealed partial class MainForm : Form
{
    static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".ts", ".m4v", ".wmv" };
    static readonly float[] Rates = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f };
    static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoNotlari");
    const string RegionHint = "Alan için videoda sürükle";

    readonly NoteStore store = new();
    readonly UndoHistory history = new();
    readonly string startVideo;
    readonly (string Message, bool Failed) migration;   // moving the data of the layout before v1.3 (Notes.cs)
    VlcPlayer player;
    readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 100 };
    readonly ToolTip tips = Theme.Tooltip();

    // Layout: toolbar on top; below it the video column (status badges, video, timeline, player bar) and the side panel.
    readonly SplitContainer split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, BackColor = Theme.Border, SplitterWidth = 3 };
    readonly SplitRow toolbar = new(56) { Wrap = false, Padding = new Padding(16, 0, 12, 0), BackColor = Theme.Surface };
    readonly Pane toolbarLine = new() { Height = 1, BackColor = Theme.Border };
    readonly Label fileLabel = new()
    {
        AutoSize = false, AutoEllipsis = true, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Mono(10f), ForeColor = Theme.Faint,
        UseMnemonic = false, Text = "Video açılmadı",
    };
    readonly BadgeStrip badges = new() { Height = 44, Padding = new Padding(16, 0, 16, 0) };
    readonly Panel videoHost = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    readonly Panel videoPanel = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    readonly FrameOverlay overlay = new() { Dock = DockStyle.Fill, Visible = false };
    readonly Label hint = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, BackColor = Theme.Rgb(0x0F1012),
        Font = Theme.Ui(12f), Text = "Video aç (Ctrl+O) ya da videoyu buraya sürükle-bırak\n\nKısayollar: F1",
    };
    readonly SeekBar seek = new();
    readonly Column chrome = new() { AutoSize = true, BackColor = Theme.Surface };
    readonly ChromeBar playerBar = new() { Height = 52, Padding = new Padding(12, 0, 12, 0) };
    readonly Label timeNow = new() { AutoSize = true, Font = Theme.Time, ForeColor = Theme.Text, Text = "00:00:00.0", Margin = Padding.Empty };
    readonly Label timeTotal = new() { AutoSize = true, Font = Theme.Time, ForeColor = Theme.Faint, Text = "/ 00:00:00.0", Margin = Padding.Empty };
    readonly Label hintLine = new()
    {
        Height = 26, Padding = new Padding(16, 0, 16, 4), ForeColor = Theme.Faint, Font = Theme.Tiny, TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true, UseMnemonic = false, Text = "Boşluk: oynat / durdur   ·   ← →: 3 sn   ·   , .: kare   ·   F1: kısayollar",
    };
    readonly DropButton rateBox = new() { Padding = new Padding(8, 0, 6, 0) };
    readonly VolumeSlider volume = new();
    FlatButton playButton, panelButton, fullscreenButton, muteButton;

    readonly TextBox noteBox = new() { Font = Theme.Ui(10.5f), PlaceholderText = "Notu yaz, sonra türünü seç" };
    readonly CheckToggle shotCheck = new("SS ekle") { Checked = true };
    readonly Label regionLabel = new()
    {
        AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Faint, Font = Theme.Small,
        UseMnemonic = false, Text = RegionHint,
    };
    readonly Label gameTime = TimeCode(), editTime = TimeCode();
    readonly DarkListView gameList = NoteList("Bu videoda henüz oyun testi notu yok.\nNotu yaz, sonra BUG / DEĞİŞİKLİK / NOT seç."),
        editList = NoteList("Bu videoda henüz kurgu işareti yok.\nK: BİP · 1: Kesim BAŞLA · 2: Kesim BİTİR");
    readonly TextBox searchBox = new() { Font = Theme.Body, PlaceholderText = "Ara (Ctrl+F) — kelime ya da #etiket" };
    readonly DropButton filterBox = new() { Padding = new Padding(10, 0, 8, 0) };
    readonly Label filterInfo = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = Padding.Empty };
    readonly Label gameSummary = Summary(), editSummary = Summary();
    readonly Label status = new() { AutoSize = false, Height = 34, AutoEllipsis = true, ForeColor = Theme.Muted, Font = Theme.Small, UseMnemonic = false };
    readonly SaveIndicator saveIndicator = new();
    readonly TabStrip tabStrip;

    // Side panel tabs: game test notes, video editing marks, the folder's videos and the shortcut reference.
    enum PanelTab { Game, Edit, Videos, Shortcuts }
    readonly Pane gamePage = Page(), editPage = Page(), videosPage = Page();
    readonly Panel shortcutsPage = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Visible = false, Padding = new Padding(16, 14, 12, 8) };

    // Extension points for the feature files (MainForm.Review.cs, MainForm.Export.cs, MainForm.Analysis.cs):
    // each implements its own Register* partial method, subscribes to the events and fills the keys and menus.
    readonly AppSettings settings = AppSettings.Load();
    readonly Dictionary<Keys, Action> featureKeys = new();    // keys outside the text box, checked after the built-in ones
    readonly Dictionary<char, Action> featureChars = new();   // layout-independent characters outside the text box
    readonly ContextMenuStrip gameMenu = new(), editMenu = new(), exportMenu = new(), toolsMenu = new();
    event Action VideoOpened;     // after a video is opened (currentVideo and player are set)
    event Action VideoClosing;    // before another video opens and when the window closes
    event Action Tick;            // every 100 ms
    event Action NotesChanged;    // after the lists were rebuilt
    partial void RegisterReview();
    partial void RegisterExport();
    partial void RegisterAnalysis();
    void OnVideoOpened() => VideoOpened?.Invoke();
    void OnVideoClosing() => VideoClosing?.Invoke();
    void OnTick() => Tick?.Invoke();
    void OnNotesChanged() => NotesChanged?.Invoke();
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
    bool shownPlaying;
    int volumeBeforeMute = 100;

    public MainForm(string startVideo)
    {
        this.startVideo = startVideo;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "KareNot";
        Size = new Size(1500, 860);
        MinimumSize = new Size(900, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        ToolStripManager.Renderer = new DarkMenuRenderer();
        tabStrip = new TabStrip(tips) { Height = 44 };

        Ffmpeg.PathOverride = settings.FfmpegPath;
        Paths.Init(settings);
        migration = Migration.Run();
        ReviewStore.Reload();
        BuildLayout();
        store.Load();
        RegisterReview();
        RegisterExport();
        RegisterAnalysis();
        ResumeLayout(false);
        PerformLayout();
    }

    // Call before changing the open video's notes outside AddNote, so Ctrl+Z can restore them.
    void RecordUndo(string label) => history.Record(store, currentVideo, label);

    int Px(float v) => Theme.Px(this, v);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    // The logical start size may not fit a small or highly scaled screen.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        if (Width <= area.Width && Height <= area.Height) return;
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        CenterToScreen();
    }

    void BuildLayout()
    {
        // Toolbar: logo and name, the open video's file name; open video and save report on the right.
        var logo = new ImageView(LoadLogo()) { Size = new Size(28, 28), Margin = new Padding(0, 0, 10, 0) };
        var name = new Label { Text = "KareNot", AutoSize = true, Font = Theme.Semi(12.5f), ForeColor = Theme.Text, Margin = Padding.Empty };
        var separator = new Pane { Size = new Size(1, 24), BackColor = Theme.Border, Margin = new Padding(14, 0, 14, 0) };
        var openButton = Btn("Video aç", OpenDialog, "Video aç (Ctrl+O) — videoyu pencereye sürükleyip bırakmak da olur", Theme.Glyph.OpenFolder);
        var reportButton = Btn("Raporu kaydet", SaveReport, "Raporu kaydet (Ctrl+S): rapor.md ve rapor.html açık videonun klasörüne\n" +
            "(<not klasörü>\\<video>); video açık değilse tüm videoların raporu not klasörüne", Theme.Glyph.Report);
        reportButton.Primary = true;
        openButton.BaseHeight = reportButton.BaseHeight = 36;
        openButton.Padding = reportButton.Padding = new Padding(14, 0, 14, 0);
        toolbar.L(logo, name, separator, fileLabel).R(openButton, reportButton);
        toolbar.Elastic = fileLabel;
        toolbar.Compactable.Add(openButton);

        // Video area
        videoHost.Controls.Add(videoPanel);
        videoHost.Controls.Add(overlay);
        videoHost.Controls.Add(hint);
        hint.BringToFront();
        var videoFrame = new Pane { BackColor = Theme.Canvas, Padding = new Padding(16, 0, 16, 16) };
        videoFrame.Controls.Add(videoHost);
        foreach (Control c in new Control[] { videoPanel, overlay, hint })
        {
            c.MouseDown += VideoMouseDown;
            c.MouseMove += VideoMouseMove;
            c.MouseUp += VideoMouseUp;
            c.MouseWheel += (s, e) => ChangeVolume(e.Delta > 0 ? 10 : -10);
        }
        videoPanel.MouseDoubleClick += (s, e) => ToggleFullscreen();
        hint.MouseDoubleClick += (s, e) => OpenDialog();

        // Timeline and player bar: previous / next video, ±3 s and frame steps around the play button; speed, volume, fullscreen,
        // panel and help on the right.
        seek.SeekRequested += ms => { ClearRegion(); player?.Seek(ms); };
        playButton = Btn("", () => { ClearRegion(); player?.TogglePause(); }, "Oynat / durdur (Boşluk)");
        playButton.Primary = true;
        playButton.BaseHeight = 40;
        playButton.Painter = PaintPlayPause;
        var back3 = Btn("−3 sn", () => SeekBy(-3000), "3 sn geri (←; Shift ile 10 sn)");
        var forward3 = Btn("+3 sn", () => SeekBy(3000), "3 sn ileri (→; Shift ile 10 sn)");
        back3.Padding = forward3.Padding = new Padding(9, 0, 9, 0);
        foreach (var r in Rates) rateBox.Items.Add(r.ToString("0.##", CultureInfo.InvariantCulture) + "x");
        rateBox.SelectedIndex = rateIndex;
        rateBox.SelectionChangeCommitted += () => SetRate(rateBox.SelectedIndex);
        tips.SetToolTip(rateBox, "Oynatma hızı (- / +)");
        muteButton = IconBtn(Theme.Glyph.Volume, "Ses", "Sesi kapat / aç (ses düzeyi: ↑ / ↓ ya da videoda fare tekerleği)", ToggleMute);
        volume.ValueChanged += v =>
        {
            if (player?.HasMedia != true) return;
            player.Volume = v;
            SetStatus("Ses: %" + v);
        };
        tips.SetToolTip(volume, "Ses düzeyi (↑ / ↓)");
        fullscreenButton = IconBtn(Theme.Glyph.FullScreen, "Tam", "Tam ekran (F, F11 ya da videoya çift tık; Esc çıkar)", ToggleFullscreen);
        panelButton = IconBtn(Theme.Glyph.Pane, "Panel", "Paneli gizle (Tab)", TogglePanel);
        panelButton.Pushed = true;
        var helpButton = IconBtn(Theme.Glyph.Help, "?", "Kısayollar (F1)", ShowHelp);
        foreach (var b in new[] { muteButton, fullscreenButton, panelButton, helpButton }) b.Quiet = true;
        playerBar.LeftItems.AddRange(new Control[] { timeNow, timeTotal });
        playerBar.CenterItems.AddRange(new Control[]
        {
            IconBtn(Theme.Glyph.PrevVideo, "|◀", "Klasördeki önceki video (Page Up)", () => StepVideo(-1)),
            back3,
            IconBtn(Theme.Glyph.PrevFrame, "◁", "Bir kare geri ( , )", () => { ClearRegion(); player?.StepFrame(-1); }),
            playButton,
            IconBtn(Theme.Glyph.NextFrame, "▷", "Bir kare ileri ( . )", () => { ClearRegion(); player?.StepFrame(1); }),
            forward3,
            IconBtn(Theme.Glyph.NextVideo, "▶|", "Klasördeki sonraki video (Page Down)", () => StepVideo(1)),
        });
        playerBar.RightItems.AddRange(new Control[] { rateBox, muteButton, volume, fullscreenButton, panelButton, helpButton });
        playerBar.Optional.AddRange(new Control[] { volume, helpButton, timeTotal, muteButton });
        playerBar.AddAll();
        Stack(chrome, null, new Control[] { new Pane { Height = 1, BackColor = Theme.Border }, seek, playerBar, hintLine });
        split.Panel1.BackColor = Theme.Canvas;
        Stack(split.Panel1, videoFrame, new Control[] { badges }, new Control[] { chrome });

        // Side panel: game test page with the "Yeni not" card (text, SS option, kind buttons) and the list card.
        split.Panel2.BackColor = Theme.Surface;
        var templatesButton = Btn("Hazır metin", null, "Hazır not metinleri (listeyi Araçlar ▾ ile düzenleyebilirsin)");
        templatesButton.Quiet = templatesButton.Chevron = true;
        templatesButton.Padding = new Padding(8, 0, 6, 0);
        templatesButton.Click += (s, e) => ShowTemplates(templatesButton);
        noteBox.AutoCompleteMode = AutoCompleteMode.Suggest;
        noteBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        noteBox.Leave += (s, e) => RefreshTemplates();
        tips.SetToolTip(shotCheck, "BUG / DEĞİŞİKLİK / NOT ile o anki kareyi de kaydet (alan seçiliyse her zaman)");
        var gameComposer = AddCard(new Card { AutoSize = true });
        Stack(gameComposer, null, new Control[]
        {
            Tall(new SplitRow(24).L(Heading("Yeni not")).R(gameTime)),
            Tall(new Spacer(8)),
            Dense(new FieldHost(noteBox) { BaseHeight = 40 }, (c, d) => c.BaseHeight = d ? 34 : 40),
            new Spacer(4),
            Dense(new SplitRow(32) { Elastic = regionLabel, ElasticMin = () => Px(40) }.L(shotCheck, templatesButton).R(regionLabel), (c, d) => c.RowHeight = d ? 28 : 32),
            new Spacer(8),
            Dense(new EqualRow(38,
                KindButton("BUG", "BUG", "B", "BUG olarak kaydet (B; yazı kutusundayken Ctrl+B)", () => AddNote("BUG", false)),
                KindButton("DEGISIKLIK", "DEĞİŞİKLİK", "D", "DEĞİŞİKLİK olarak kaydet (D; yazı kutusundayken Ctrl+D)", () => AddNote("DEGISIKLIK", false)),
                KindButton("NOT", "NOT", "N", "NOT olarak kaydet (N; yazı kutusundayken Enter) — önce yazı gerekir", () => AddNote("NOT", true))), (c, d) => c.RowHeight = d ? 34 : 38),
        });

        foreach (var f in Filters) filterBox.Items.Add(f.Label);
        filterBox.SelectedIndex = 0;
        filterBox.SelectionChangeCommitted += () => RefreshList(SelectedNote);
        tips.SetToolTip(filterBox, "Listeyi süz: tür, durum, önem");
        searchBox.TextChanged += (s, e) => RefreshList(SelectedNote);
        var searchField = new FieldHost(searchBox, Theme.Glyph.Search) { BaseHeight = 32 };
        var searchRow = new SplitRow(32) { Elastic = searchField, ElasticMin = () => Px(80), Wrap = false, Gap = 6 }.L(searchField).R(filterInfo, filterBox);

        gameList.Columns.Add("Zaman");
        gameList.Columns.Add("Tür");
        gameList.Columns.Add("Durum");
        gameList.Columns.Add("SS");
        gameList.Columns.Add("Not");
        gameList.ShowItemToolTips = true;
        gameMenu.Items.Add(Item("Git (−2 sn)", "G", GotoSelected));
        gameMenu.Items.Add(Item("Düzenle", "F2", EditSelected));
        var statusMenu = new ToolStripMenuItem("Durum");
        foreach (var st in Statuses.All) statusMenu.DropDownItems.Add(Statuses.Label(st), null, (s, e) => SetStatusOf(SelectedNote, st));
        var severityMenu = new ToolStripMenuItem("Önem");
        foreach (var sv in Severities.All) severityMenu.DropDownItems.Add(sv == null ? "Yok" : Severities.Label(sv), null, (s, e) => SetSeverityOf(SelectedNote, sv));
        gameMenu.Items.Add(statusMenu);
        gameMenu.Items.Add(severityMenu);
        gameMenu.Items.Add(Item("SS'i aç", null, OpenSelectedShot));
        gameMenu.Items.Add(Item("Seçilene SS ekle", "S", ShotToSelected));
        gameMenu.Items.Add(new ToolStripSeparator());
        gameMenu.Items.Add(Item("Sil", "Del", DeleteSelected));
        var undoItems = AddUndoItems(gameMenu);
        gameMenu.Opening += (s, e) =>
        {
            var n = SelectedNote;
            foreach (ToolStripMenuItem i in statusMenu.DropDownItems) i.Checked = n != null && Statuses.Label(n.Status) == i.Text;
            foreach (ToolStripMenuItem i in severityMenu.DropDownItems) i.Checked = n != null && (n.Severity == null ? "Yok" : Severities.Label(n.Severity)) == i.Text;
            undoItems();
        };
        gameList.ContextMenuStrip = gameMenu;
        gameList.DrawItem += (s, e) => DrawNoteRow(gameList, e, true);
        var gameGoto = Btn("Git −2 sn", GotoSelected, "Seçili nota git, 2 sn öncesinden (G, çift tık ya da Enter)", Theme.Glyph.Rewind);
        var gameEdit = Btn("Düzenle", EditSelected, "Seçili notu düzenle: tür, yazı, durum, önem (F2)", Theme.Glyph.Edit);
        var gameShot = Btn("SS ekle", ShotToSelected, "Seçili nota o anki kareyi SS olarak ekle (S)", Theme.Glyph.Camera);
        var gameMore = MenuBtn(gameMenu, "Diğer: durum, önem, döngüde izle, klip, görev metni, sil, geri al, yinele (listede sağ tık da olur)");
        var gameActions = Dense(new SplitRow(34) { Gap = 6 }, (c, d) => c.RowHeight = d ? 32 : 34).L(gameGoto, gameEdit, gameShot).R(gameMore);
        gameActions.Compactable.AddRange(new[] { gameShot, gameEdit, gameGoto });
        var gameListCard = AddCard(new Card());
        Stack(gameListCard, gameList,
            new Control[] { Tall(new SplitRow(24) { Elastic = gameSummary }.L(Heading("Bu videonun notları"), gameSummary)), Tall(new Spacer(8)), searchRow, new Spacer(8) },
            new Control[] { new Spacer(8), gameActions });
        Stack(gamePage, gameListCard, new Control[] { gameComposer, CardGap() });

        // Side panel: video editing page (no text box, no screenshots).
        var editComposer = AddCard(new Card { AutoSize = true });
        Stack(editComposer, null, new Control[]
        {
            Tall(new SplitRow(24).L(Heading("Kurgu işareti")).R(editTime)),
            Tall(new Spacer(4)),
            Tall(new WrapLabel { Text = "O anki süreye kaydedilir; yazı kutusu ve SS yok. K basılı tutulursa bıraktığın yere kadar BİP aralığı olur.", ForeColor = Theme.Muted, Font = Theme.Small }),
            Tall(new Spacer(8)),
            Dense(new EqualRow(38,
                KindButton("BIP", "BİP", "K", "BİP (küfür) işareti (K; basılı tutarsan bıraktığın yere kadar aralık olur)", () => AddNote("BIP", false)),
                KindButton("KES_BASLA", "Kesim BAŞLA", "1", "Kesim başlangıcı (1)", () => AddNote("KES_BASLA", false)),
                KindButton("KES_BITIR", "Kesim BİTİR", "2", "Kesim bitişi (2)", () => AddNote("KES_BITIR", false))), (c, d) => c.RowHeight = d ? 34 : 38),
        });
        editList.Columns.Add("Zaman");
        editList.Columns.Add("Tür");
        editList.Columns.Add("Not");
        editList.ShowItemToolTips = true;
        editMenu.Items.Add(Item("Git (−2 sn)", "G", GotoSelected));
        editMenu.Items.Add(Item("Düzenle", "F2", EditSelected));
        editMenu.Items.Add(new ToolStripSeparator());
        editMenu.Items.Add(Item("Sil", "Del", DeleteSelected));
        var editUndoItems = AddUndoItems(editMenu);
        editMenu.Opening += (s, e) => editUndoItems();
        editList.ContextMenuStrip = editMenu;
        editList.DrawItem += (s, e) => DrawNoteRow(editList, e, false);
        var editGoto = Btn("Git −2 sn", GotoSelected, "Seçili işarete git, 2 sn öncesinden (G, çift tık ya da Enter)", Theme.Glyph.Rewind);
        var editEdit = Btn("Düzenle", EditSelected, "Seçili işareti düzenle: tür, yazı, BİP süresi (F2)", Theme.Glyph.Edit);
        var editMore = MenuBtn(editMenu, "Diğer: döngüde izle, sil, geri al, yinele (listede sağ tık da olur)");
        var editActions = Dense(new SplitRow(34) { Gap = 6 }, (c, d) => c.RowHeight = d ? 32 : 34).L(editGoto, editEdit).R(editMore);
        editActions.Compactable.AddRange(new[] { editEdit, editGoto });
        var editListCard = AddCard(new Card());
        Stack(editListCard, editList,
            new Control[] { Tall(new SplitRow(24) { Elastic = editSummary }.L(Heading("Kurgu işaretleri"), editSummary)), Tall(new Spacer(8)) },
            new Control[] { new Spacer(8), editActions });
        Stack(editPage, editListCard, new Control[] { editComposer, CardGap() });

        foreach (var l in new[] { gameList, editList }) l.DoubleClick += (s, e) => GotoSelected();

        // Tabs, pages and the footer (status line, save state, exports, tools, folder).
        BuildShortcutsPage();
        tabStrip.Add("Oyun testi", "Oyun testi notları: BUG, DEĞİŞİKLİK, NOT");
        tabStrip.Add("Kurgu", "Kurgu işaretleri: BİP ve kesimler");
        tabStrip.Add("Videolar", "Klasördeki videolar ve inceleme durumu");
        tabStrip.Add("Kısayollar", "Kısayollar (F1)");
        tabStrip.TabClicked += i => ShowTab((PanelTab)i);
        var exportButton = Btn("Dışa aktar", null, "CSV, görev listesi, kurgu notları, Resolve işaretleri, bulgu paketi (Ctrl+P), bipli / kesilmiş video", Theme.Glyph.Export);
        exportButton.Click += (s, e) => exportMenu.Show(exportButton, new Point(0, exportButton.Height));
        var toolsButton = Btn("Araçlar", null, "Ayarlar, hazır metinler ve küfür listesi, kesilmiş hâli izle, küfür adayları, ölü anlar", Theme.Glyph.Tools);
        toolsButton.Click += (s, e) => toolsMenu.Show(toolsButton, new Point(0, toolsButton.Height));
        var folderButton = Btn("Klasörü aç", OpenFolder, "Açık videonun klasörünü aç (<not klasörü>\\<video>); video yoksa not klasörünü", Theme.Glyph.Folder);
        var notesFileButton = Btn("Kayıt dosyası", OpenNotesFile,
            "Açık videonun kayıt dosyasını (notlar.txt) metin düzenleyicide aç. Kaydedip KareNot'a dönünce notlar yeniden okunur.", Theme.Glyph.File);
        foreach (var b in new[] { exportButton, toolsButton, folderButton, notesFileButton })
        {
            b.Quiet = true;
            b.Font = Theme.Small;
            b.Padding = new Padding(8, 0, 8, 0);
        }
        exportButton.Chevron = toolsButton.Chevron = true;
        var footerRow = new SplitRow(32) { Elastic = saveIndicator, ElasticMin = () => saveIndicator.MinWidth, Gap = 2 }
            .L(saveIndicator).R(exportButton, toolsButton, notesFileButton, folderButton);
        footerRow.Compactable.AddRange(new[] { folderButton, notesFileButton, toolsButton, exportButton });
        tips.SetToolTip(saveIndicator, "Notlar her değişiklikte kaydedilir: " + Path.Combine(Paths.Root, "<video>", "notlar.txt"));
        footer = new Column { AutoSize = true, Padding = new Padding(12, 6, 10, 4) };
        Stack(footer, null, new Control[] { status, footerRow });
        foreach (var page in new Control[] { gamePage, editPage, videosPage, shortcutsPage }) split.Panel2.Controls.Add(page);
        Stack(split.Panel2, null, new Control[] { tabStrip }, new Control[] { new Pane { Height = 1, BackColor = Theme.Border }, footer });
        ShowTab(PanelTab.Game);

        Stack(this, split, new Control[] { toolbar, toolbarLine });
        split.SizeChanged += (s, e) => FitPanel();
        // Only a drag by the user changes the preferred panel width (SplitterMoving is raised for drags only).
        bool splitterDrag = false;
        split.SplitterMoving += (s, e) => splitterDrag = true;
        split.SplitterMoved += (s, e) =>
        {
            if (splitterDrag && !fittingPanel && panelLaidOut && !split.Panel2Collapsed)
                panelWidth = (int)Math.Round((split.Width - split.SplitterDistance - split.SplitterWidth) * 96f / DeviceDpi);
            splitterDrag = false;
        };
        DpiChanged += (s, e) => BeginInvoke(() =>
        {
            FitPanel();
            RefreshList(SelectedNote);
        });

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
    }

    // Docks fill (added first, so it takes the rest), then the top rows (top to bottom) and bottom rows (top to bottom).
    static void Stack(Control parent, Control fill, Control[] top, Control[] bottom = null)
    {
        if (fill != null)
        {
            fill.Dock = DockStyle.Fill;
            parent.Controls.Add(fill);
        }
        foreach (var c in bottom ?? Array.Empty<Control>())
        {
            c.Dock = DockStyle.Bottom;
            parent.Controls.Add(c);
        }
        foreach (var c in Enumerable.Reverse(top))
        {
            c.Dock = DockStyle.Top;
            parent.Controls.Add(c);
        }
    }

    FlatButton Btn(string text, Action action, string tip = null, string glyph = null)
    {
        var b = new FlatButton(text) { Glyph = glyph };
        if (action != null) b.Click += (s, e) => action();
        if (tip != null) tips.SetToolTip(b, tip);
        return b;
    }

    FlatButton IconBtn(string glyph, string fallback, string tip, Action action)
    {
        var b = Btn("", action, tip, glyph);
        b.Fallback = fallback;
        return b;
    }

    // "⋯" button that opens a list's menu (the same one as the right click).
    FlatButton MenuBtn(ContextMenuStrip menu, string tip)
    {
        var b = IconBtn(Theme.Glyph.More, "⋯", tip, null);
        b.Click += (s, e) => menu.Show(b, new Point(0, b.Height));
        return b;
    }

    FlatButton KindButton(string kind, string text, string key, string tip, Action action)
    {
        var b = new FlatButton(text, Theme.Kind(kind)) { Glyph = Theme.KindGlyph(kind), Keycap = key, GlyphSize = 10f, Padding = new Padding(8, 0, 8, 0) };
        b.Click += (s, e) => action();
        tips.SetToolTip(b, tip);
        return b;
    }

    static ToolStripMenuItem Item(string text, string keys, Action action) => new(text, null, (s, e) => action()) { ShortcutKeyDisplayString = keys };

    // Undo / redo at the end of a list menu; the returned action updates their state when the menu opens.
    Action AddUndoItems(ContextMenuStrip menu)
    {
        var undo = Item("Geri al", "Ctrl+Z", Undo);
        var redo = Item("Yinele", "Ctrl+Y", Redo);
        menu.Items.Add(undo);
        menu.Items.Add(redo);
        return () =>
        {
            undo.Enabled = history.CanUndo;
            redo.Enabled = history.CanRedo;
        };
    }

    static Label Heading(string text) => new() { Text = text, AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Margin = Padding.Empty, UseMnemonic = false };

    static Label TimeCode() => new() { AutoSize = true, Font = Theme.Time, ForeColor = Theme.Muted, Text = "00:00:00.0", Margin = Padding.Empty };

    static Label Summary() => new()
    {
        AutoSize = false, AutoEllipsis = true, Height = 22, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted, Font = Theme.Small, UseMnemonic = false,
    };

    static Pane Page() => new() { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(12, 12, 12, 12), Visible = false };

    // ---------- Short windows ----------

    // On a short window (900 × 500 at the minimum) the headings, hints and some spacing go, so the lists keep a few rows.
    Column footer;
    bool dense;
    readonly List<Control> tallOnly = new(), cards = new(), cardGaps = new();

    readonly List<Action<bool>> densityChanges = new();

    T Dense<T>(T c, Action<T, bool> apply) where T : Control
    {
        densityChanges.Add(d => apply(c, d));
        return c;
    }

    T Tall<T>(T c) where T : Control
    {
        tallOnly.Add(c);
        return c;
    }

    Card AddCard(Card c)
    {
        c.Padding = new Padding(12, 12, 12, 10);
        cards.Add(c);
        return c;
    }

    Spacer CardGap()
    {
        var s = new Spacer(12);
        cardGaps.Add(s);
        return s;
    }

    void UpdateDensity()
    {
        bool d = ClientSize.Height < Px(640);
        if (d == dense || ClientSize.Height == 0) return;
        dense = d;
        SuspendLayout();
        foreach (var c in tallOnly) c.Visible = !d;
        toolbar.RowHeight = d ? 46 : 56;
        tabStrip.Height = Px(d ? 36 : 44);
        status.Height = Px(d ? 17 : 34);
        footer.Padding = d ? new Padding(Px(12), Px(4), Px(10), Px(2)) : new Padding(Px(12), Px(6), Px(10), Px(4));
        foreach (var page in new Control[] { gamePage, editPage, videosPage }) page.Padding = new Padding(Px(d ? 8 : 12));
        foreach (var c in cards) c.Padding = d ? new Padding(Px(10), Px(8), Px(10), Px(8)) : new Padding(Px(12), Px(12), Px(12), Px(10));
        foreach (var s in cardGaps) s.Height = Px(d ? 6 : 12);
        foreach (var change in densityChanges) change(d);
        foreach (var c in cards) c.PerformLayout();
        toolbar.PerformLayout();
        ResumeLayout(true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated && WindowState != FormWindowState.Minimized) UpdateDensity();
    }

    static DarkListView NoteList(string empty) => new() { EmptyText = empty, RowHeight = 36 };

    static Image LoadLogo()
    {
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("KareNot.karenot.png");
            if (s != null)
                using (var img = Image.FromStream(s))
                    return new Bitmap(img);
        }
        catch { }
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch { return null; }
    }

    void PaintPlayPause(Graphics g, Rectangle r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(c);
        float s = DeviceDpi / 96f, cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        if (shownPlaying)
        {
            g.FillRectangle(b, cx - 6 * s, cy - 7 * s, 4 * s, 14 * s);
            g.FillRectangle(b, cx + 2 * s, cy - 7 * s, 4 * s, 14 * s);
        }
        else g.FillPolygon(b, new[] { new PointF(cx - 5 * s, cy - 8 * s), new PointF(cx + 8 * s, cy), new PointF(cx - 5 * s, cy + 8 * s) });
    }

    protected override void OnShown(EventArgs e)
    {
        LayoutPanel();
        Resize += (s, ev) => { if (!panelLaidOut) LayoutPanel(); };
        gameList.FitColumns();
        editList.FitColumns();
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
        if (migration.Message != null) SetStatus(migration.Message);
        if (migration.Failed) MessageBox.Show(this, migration.Message, "KareNot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (startVideo != null && File.Exists(startVideo)) OpenVideo(startVideo);
        base.OnShown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        uiTimer.Stop();
        if (currentVideo != null) OnVideoClosing();
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
        if (currentVideo != null) OnVideoClosing();
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
        fileLabel.Text = currentVideo;
        fileLabel.ForeColor = Theme.Text;
        tips.SetToolTip(fileLabel, path);
        SaveSetting("son_klasor.txt", Path.GetDirectoryName(path));
        lastAdded = null;
        RefreshList();
        OnVideoOpened();
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

    void ToggleMute()
    {
        if (player == null || !player.HasMedia) return;
        if (player.Volume > 0)
        {
            volumeBeforeMute = player.Volume;
            player.Volume = 0;
            SetStatus("Ses kapalı.");
        }
        else
        {
            player.Volume = volumeBeforeMute > 0 ? volumeBeforeMute : 100;
            SetStatus("Ses: %" + player.Volume);
        }
    }

    static void SetText(Control c, string text)
    {
        if (c.Text != text) c.Text = text;
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
        var now = NoteStore.Fmt(t / 1000.0);
        SetText(timeNow, now);
        SetText(timeTotal, "/ " + NoteStore.Fmt(length / 1000.0));
        SetText(gameTime, now);
        SetText(editTime, now);
        OnTick();
        bool playing = player.IsPlaying;
        if (playing != shownPlaying)
        {
            shownPlaying = playing;
            playButton.Invalidate();
        }
        if (player.HasMedia && !volume.Dragging && player.Volume is int v && v >= 0)
        {
            volume.Value = v;
            muteButton.Glyph = v == 0 ? Theme.Glyph.Mute : Theme.Glyph.Volume;
        }
        UpdateBadges();
    }

    // Badges above the video: paused, loop, cut skipping and the area selection.
    void UpdateBadges()
    {
        var list = new List<(string, string, Color, bool)>();
        if (player?.HasMedia == true && !player.IsPlaying) list.Add((Theme.Glyph.Pause, "Duraklatıldı", Theme.Muted, false));
        if (looping) list.Add((Theme.Glyph.Rewind, "Döngüde · L ile kapat", Theme.Accent, false));
        if (skipCuts) list.Add((Theme.Glyph.Cut, "Kesilmiş hâli · X ile kapat", Theme.Kind("KES_BASLA"), false));
        if (RegionActive) list.Add((null, "Alan seçili · Esc ile iptal", Theme.Error, true));
        badges.Set(list);
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
        toolbar.Visible = toolbarLine.Visible = hintLine.Visible = !fullscreen;
        fullscreenButton.Glyph = fullscreen ? Theme.Glyph.ExitFullScreen : Theme.Glyph.FullScreen;
        tips.SetToolTip(fullscreenButton, fullscreen ? "Tam ekrandan çık (Esc, F, F11)" : "Tam ekran (F, F11 ya da videoya çift tık; Esc çıkar)");
        ResumeLayout();
    }

    void TogglePanel()
    {
        split.Panel2Collapsed = !split.Panel2Collapsed;
        panelButton.Pushed = !split.Panel2Collapsed;
        tips.SetToolTip(panelButton, split.Panel2Collapsed ? "Paneli göster (Tab)" : "Paneli gizle (Tab)");
        if (split.Panel2Collapsed && (noteBox.Focused || ListFocused)) ActiveControl = null;
        if (!split.Panel2Collapsed) FitPanel();
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
        regionLabel.Text = $"Alan seçili ({s.Width} × {s.Height})";
        regionLabel.ForeColor = Theme.Mix(Theme.Error, Theme.Text, 0.25f);
        tips.SetToolTip(regionLabel, "Alan seçili: notu yaz ve türünü seç; SS'te kırmızı çerçeveyle işaretlenir (Esc: iptal)");
        UpdateBadges();
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
        regionLabel.Text = RegionHint;
        regionLabel.ForeColor = Theme.Faint;
        tips.SetToolTip(regionLabel, null);
        UpdateBadges();
    }

    // ---------- Notes ----------

    static bool KindTakesShot(string kind) => kind is "BUG" or "DEGISIKLIK" or "NOT";

    // Runs a change that writes the note files; the footer dot turns green after a successful save, red with the error otherwise.
    bool Saved(Action save)
    {
        try
        {
            save();
            saveIndicator.Set(true, "Notlar kaydedildi", "Kaydedildi");
            tips.SetToolTip(saveIndicator, $"Son kayıt {DateTime.Now:HH:mm:ss}: {(currentVideo != null ? Paths.NotesFile(currentVideo) : Paths.Root)}");
            return true;
        }
        catch (Exception ex)
        {
            saveIndicator.Set(false, "Kaydedilemedi: " + ex.Message, "Kaydedilemedi");
            tips.SetToolTip(saveIndicator, "Notlar kaydedilemedi: " + ex.Message);
            SetStatus("HATA: notlar kaydedilemedi: " + ex.Message);
            return false;
        }
    }

    // Saves the current frame (or the selected area marked on the frozen frame); returns the file name or null.
    string MakeShot(string kind, double t, out string error)
    {
        error = null;
        Directory.CreateDirectory(Paths.ShotsDir(currentVideo));
        var name = string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyyMMdd_HHmmss}_{2}s_{3:00}.png",
            kind.ToLowerInvariant(), DateTime.Now, (int)t, ++shotCounter);
        var path = Path.Combine(Paths.ShotsDir(currentVideo), name);
        if (RegionActive)
        {
            using var marked = overlay.RenderMarked();
            marked.Save(path, ImageFormat.Png);
            return name;
        }
        return player.TakeSnapshot(path, out error) ? name : null;
    }

    Note AddNote(string kind, bool requireText)
    {
        if (player == null || !player.HasMedia)
        {
            SetStatus("Önce bir video aç.");
            return null;
        }
        // Editing marks only take the text box when it is being typed in, so a game note draft is not swallowed.
        bool useBox = !Kinds.IsEdit(kind) || noteBox.Focused;
        var text = useBox ? NoteStore.Clean(noteBox.Text) : "";
        if (requireText && text.Length == 0)
        {
            SetStatus("NOT için önce kutuya yaz (Enter), sonra N / Enter.");
            FocusNoteBox();
            return null;
        }

        double t = (RegionActive ? frozenMs : player.TimeMs) / 1000.0;
        string shot = null, shotError = null;
        if (KindTakesShot(kind) && (shotCheck.Checked || RegionActive)) shot = MakeShot(kind, t, out shotError);

        RecordUndo(Kinds.Label(kind) + " eklendi");
        Note added = null;
        bool saved = Saved(() => added = store.Add(t, kind, text, currentVideo, shot));
        lastAdded = added ?? store.All[^1];   // a failed save still keeps the note in memory
        if (useBox) noteBox.Clear();
        ClearRegion();
        if (noteBox.Focused) ActiveControl = null;
        ShowTab(Kinds.IsEdit(kind) ? PanelTab.Edit : PanelTab.Game);
        RefreshList(lastAdded);
        if (saved)
            SetStatus(shotError != null
                ? "Not kaydedildi, SS yok: " + shotError
                : $"{Kinds.Label(kind)} eklendi — {NoteStore.Fmt(t)}{(shot != null ? " (SS)" : "")}");
        return lastAdded;
    }

    // Holding K turns the BIP into a range that ends where the key is released.
    Note heldBip;
    bool bipKeyDown;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode != Keys.K || e.Modifiers != Keys.None || Typing) return;
        e.SuppressKeyPress = true;
        if (bipKeyDown) return;   // key repeat
        bipKeyDown = true;
        heldBip = AddNote("BIP", false);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode != Keys.K) return;
        bipKeyDown = false;
        var n = heldBip;
        heldBip = null;
        if (n == null || player == null || !store.All.Contains(n)) return;
        double end = player.TimeMs / 1000.0;
        if (end - n.T < 0.4) return;
        n.End = end;
        bool saved = Saved(() => store.Save(n.Video));
        RefreshList(n);
        if (saved) SetStatus($"BİP aralığı: {NoteStore.Fmt(n.T)}–{NoteStore.Fmt(end)}");
    }

    void Undo()
    {
        (string Label, string Video)? done = null;
        if (!Saved(() => done = history.Undo(store))) { RefreshList(); return; }
        AfterHistory(done, "Geri alındı");
    }

    void Redo()
    {
        (string Label, string Video)? done = null;
        if (!Saved(() => done = history.Redo(store))) { RefreshList(); return; }
        AfterHistory(done, "Yinelendi");
    }

    void AfterHistory((string Label, string Video)? done, string verb)
    {
        if (done == null)
        {
            SetStatus(verb == "Geri alındı" ? "Geri alınacak bir şey yok." : "Yinelenecek bir şey yok.");
            return;
        }
        lastAdded = null;
        RefreshList();
        SetStatus($"{verb}: {done.Value.Label}" + (done.Value.Video != currentVideo ? $" ({done.Value.Video})" : ""));
    }

    void SetStatusOf(Note n, string status)
    {
        if (n == null || Kinds.IsEdit(n.Kind)) return;
        RecordUndo("Durum değişti");
        n.Status = status;
        bool saved = Saved(() => store.Save(n.Video));
        RefreshList(n);
        if (saved) SetStatus($"Durum: {Statuses.Label(status)} — {NoteStore.Fmt(n.T)}");
    }

    void SetSeverityOf(Note n, string severity)
    {
        if (n == null || Kinds.IsEdit(n.Kind)) return;
        RecordUndo("Önem değişti");
        n.Severity = severity;
        bool saved = Saved(() => store.Save(n.Video));
        RefreshList(n);
        if (saved) SetStatus($"Önem: {(severity == null ? "yok" : Severities.Label(severity))} — {NoteStore.Fmt(n.T)}");
    }

    ListView ActiveList => noteTab == PanelTab.Edit ? editList : gameList;
    bool ListFocused => gameList.Focused || editList.Focused || videoList.Focused;
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
        RecordUndo("SS eklendi");
        n.Shot = shot;
        bool saved = Saved(() => store.Save(n.Video));
        ClearRegion();
        RefreshList(n);
        if (saved) SetStatus("SS seçili nota eklendi.");
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
        RecordUndo("Not düzenlendi");
        n.Kind = dlg.Kind;
        n.Text = NoteStore.Clean(dlg.NoteText);
        n.Status = dlg.Status;
        n.Severity = dlg.Severity;
        n.End = dlg.End(n.T);
        bool saved = Saved(() => store.Save(n.Video));
        RefreshList(n);
        if (saved) SetStatus("Not güncellendi.");
    }

    void OpenSelectedShot()
    {
        var n = SelectedNote;
        var file = n == null ? null : NoteStore.ResolveShot(n);
        if (file == null)
        {
            SetStatus("Seçili notun SS'i yok.");
            return;
        }
        Process.Start(new ProcessStartInfo(Path.Combine(Paths.ShotsDir(n.Video), file)) { UseShellExecute = true });
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
        RecordUndo("Not silindi");
        // A cut row stands for both of its marks.
        if (cutPartner.TryGetValue(n, out var partner)) store.All.Remove(partner);
        bool saved = Saved(() => store.Remove(n));
        if (n == lastAdded || partner == lastAdded) lastAdded = null;
        RefreshList();
        if (list.Items.Count > 0) list.Items[Math.Min(index, list.Items.Count - 1)].Selected = true;
        if (saved) SetStatus((partner != null ? "Kesim silindi: " : "Not silindi: ") + NoteStore.Fmt(n.T) + " " + Kinds.Label(n.Kind));
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
        RecordUndo("Not silindi");
        bool saved = Saved(() => store.Remove(n));
        lastAdded = null;
        RefreshList();
        if (saved) SetStatus("Not silindi: " + NoteStore.Fmt(n.T) + " " + Kinds.Label(n.Kind));
    }

    List<Note> CurrentNotes() => currentVideo == null ? new List<Note>() : store.All.Where(n => n.Video == currentVideo).OrderBy(n => n.T).ToList();

    bool InNoteTab(Note n) => Kinds.IsEdit(n.Kind) == (noteTab == PanelTab.Edit);

    // Filters of the game list; the search box narrows it further (all words, #tags included).
    static readonly (string Label, Func<Note, bool> Match)[] Filters =
    {
        ("Tümü", n => true),
        ("Buglar", n => n.Kind == "BUG"),
        ("Değişiklikler", n => n.Kind == "DEGISIKLIK"),
        ("Notlar", n => n.Kind == "NOT"),
        ("Açık olanlar", n => n.Status == null),
        ("Tekrar test", n => n.Status == "TEKRAR_TEST"),
        ("Düzeltilenler", n => n.Status == "DUZELTILDI"),
        ("Kritik", n => n.Severity == "KRITIK"),
    };
    static readonly CultureInfo Turkish = new("tr-TR");

    bool MatchesSearch(Note n)
    {
        var terms = searchBox.Text.Trim().ToLower(Turkish).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hay = (n.Text + " " + Kinds.Label(n.Kind) + " " + Statuses.Label(n.Status) + " " + Severities.Label(n.Severity)).ToLower(Turkish);
        return terms.All(hay.Contains);
    }

    void RefreshList(Note select = null)
    {
        var notes = CurrentNotes();
        var filter = Filters[Math.Max(0, filterBox.SelectedIndex)].Match;
        var game = notes.Where(n => !Kinds.IsEdit(n.Kind)).ToList();
        var shown = game.Where(n => filter(n) && MatchesSearch(n)).ToList();
        FillGameList(shown, select);
        filterInfo.Text = shown.Count == game.Count ? "" : $"{shown.Count} / {game.Count}";
        FillEditList(notes.Where(n => Kinds.IsEdit(n.Kind)).ToList(), select);
        int Count(string kind) => notes.Count(n => n.Kind == kind);
        gameSummary.Text = string.Join(" · ", new[] { (Count("BUG"), "bug"), (Count("DEGISIKLIK"), "değişiklik"), (Count("NOT"), "not") }
            .Where(p => p.Item1 > 0).Select(p => $"{p.Item1} {p.Item2}"));
        int cuts = Kinds.Cuts(notes).Count;
        editSummary.Text = Count("BIP") + cuts == 0 ? "" : $"{Count("BIP")} bip · {cuts} kesim";
        RefreshMarkers();
        RefreshTemplates();

        if (currentVideo == null)
            SetStatus($"Toplam {store.All.Count} not. Not dosyaları: {Path.Combine(Paths.Root, "<video>", "notlar.txt")}");
        else
            SetStatus($"{notes.Count(n => !Kinds.IsEdit(n.Kind))} not ({Count("BUG")} bug, {Count("DEGISIKLIK")} değişiklik) · " +
                $"kurgu: {Count("BIP")} bip, {Count("KES_BASLA")} kesim — bu video");
        OnNotesChanged();
    }

    static ListViewItem Row(string time, Note tag, string kindText, Color kindColor)
    {
        var item = new ListViewItem(time) { Tag = tag, UseItemStyleForSubItems = false };
        item.SubItems.Add(kindText).ForeColor = kindColor;
        return item;
    }

    static void Add(ListView list, ListViewItem item, bool selected)
    {
        list.Items.Add(item);
        if (!selected) return;
        item.Selected = true;
        item.EnsureVisible();
    }

    void FillGameList(List<Note> notes, Note select)
    {
        gameList.BeginUpdate();
        gameList.Items.Clear();
        foreach (var n in notes)
        {
            var item = Row(NoteStore.Fmt(n.T), n, Kinds.Label(n.Kind), Theme.Kind(n.Kind));
            var parts = new List<string>();
            if (n.Severity != null) parts.Add(Severities.Label(n.Severity));
            if (n.Status != null) parts.Add(Statuses.Label(n.Status));
            var state = string.Join(" · ", parts);
            item.SubItems.Add(state).ForeColor = StateColor(n);
            item.SubItems.Add(n.Shot != null || n.Clip != null ? (n.Clip != null ? "▶" : "SS") : "");
            item.SubItems.Add(n.Text);
            item.ToolTipText = string.Join(" — ", new[] { state, n.Text }.Where(s => s.Length > 0));
            Add(gameList, item, n == select);
        }
        gameList.EndUpdate();
        MeasureRows(gameList);
    }

    static Color StateColor(Note n) =>
        n.Status == "DUZELTILDI" ? Theme.Ok : n.Status == "TEKRAR_TEST" || n.Severity == "ORTA" ? Theme.Warn : n.Severity == "KRITIK" ? Theme.Error : Theme.Muted;

    // Cut marks are shown in pairs ("a–b · 24 sn"); a row's Tag is its first mark and cutPartner maps it to the other one.
    readonly Dictionary<Note, Note> cutPartner = new();
    static readonly Color Warn = Theme.Warn;

    void FillEditList(List<Note> notes, Note select)
    {
        cutPartner.Clear();
        var rows = new List<(double T, ListViewItem Item, bool Selected)>();
        foreach (var n in notes.Where(n => n.Kind == "BIP"))
        {
            var time = n.End is double e && e > n.T ? $"{NoteStore.Fmt(n.T)}–{NoteStore.Fmt(e)}" : NoteStore.Fmt(n.T);
            var item = Row(time, n, Kinds.Label(n.Kind), Theme.Kind(n.Kind));
            item.SubItems.Add(n.Text);
            rows.Add((n.T, item, n == select));
        }
        foreach (var (start, end) in Kinds.Cuts(notes))
        {
            var first = start ?? end;
            ListViewItem item;
            if (start != null && end != null)
            {
                cutPartner[start] = end;
                item = Row($"{NoteStore.Fmt(start.T)}–{NoteStore.Fmt(end.T)}", start, "KES", Theme.Kind("KES_BASLA"));
                var text = string.Join(" · ", new[] { start.Text, end.Text }.Where(t => t.Length > 0));
                item.SubItems.Add($"{end.T - start.T:0.0} sn" + (text.Length > 0 ? " · " + text : ""));
            }
            else
            {
                item = Row(NoteStore.Fmt(first.T), first, Kinds.Label(first.Kind), Theme.Kind(first.Kind));
                var sub = item.SubItems.Add((start != null ? "⚠ bitişi yok" : "⚠ başı yok") + (first.Text.Length > 0 ? " · " + first.Text : ""));
                sub.ForeColor = Warn;
            }
            rows.Add((first.T, item, select != null && (select == start || select == end)));
        }
        editList.BeginUpdate();
        editList.Items.Clear();
        foreach (var r in rows.OrderBy(r => r.T))
        {
            r.Item.ToolTipText = r.Item.SubItems[2].Text;
            Add(editList, r.Item, r.Selected);
        }
        editList.EndUpdate();
        MeasureRows(editList);
    }

    // ---------- List rows ----------

    static readonly Font StrikeFont = Theme.Ui(9.75f, FontStyle.Strikeout);

    // Badge labels of the list rows (the item keeps Kinds.Label / "KES").
    static string BadgeText(string kindLabel) => kindLabel switch
    {
        "KES" => "KESİM",
        "KES ▶" => "BAŞLA",
        "KES ■" => "BİTİR",
        _ => kindLabel,
    };

    // Time and badge columns are as wide as the widest entry, so the note texts line up.
    void MeasureRows(DarkListView list)
    {
        var items = list.Items.Cast<ListViewItem>().ToList();
        list.TimeWidth = items.Select(i => Theme.Width(i.Text, Theme.Time)).Append(Theme.Width("00:00:00.0", Theme.Time)).Max();
        list.BadgeWidth = items.Select(i => Theme.Badge(null, list, Theme.KindGlyph((i.Tag as Note)?.Kind), BadgeText(i.SubItems[1].Text), Color.Empty, Color.Empty, 0, 0))
            .DefaultIfEmpty(0).Max();
        list.FitColumns();
        list.Invalidate();
    }

    void DrawRowBack(Graphics g, Rectangle r, bool selected)
    {
        using (var b = new SolidBrush(Theme.Raised)) g.FillRectangle(b, r);
        if (selected)
        {
            var s = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            Theme.FillRound(g, Theme.SelectedRow, s, Px(6));
            Theme.DrawRound(g, Theme.Mix(Theme.Raised, Theme.Accent, 0.6f), s, Px(6));
        }
        else
        {
            using var p = new Pen(Theme.Divider);
            g.DrawLine(p, r.X + Px(6), r.Bottom - 1, r.Right - Px(6), r.Bottom - 1);
        }
    }

    // A note row: time, kind badge, note text; game notes also show severity / status and an SS or clip glyph. In a narrow list the
    // badge keeps only its glyph and the state becomes a colored dot (the tooltip has the text).
    void DrawNoteRow(DarkListView list, DrawListViewItemEventArgs e, bool game)
    {
        var g = e.Graphics;
        var r = e.Bounds;
        var item = e.Item;
        var n = item.Tag as Note;
        DrawRowBack(g, r, item.Selected);
        var back = item.Selected ? Theme.SelectedRow : Theme.Raised;
        int pad = Px(10), gap = Px(10), cy = r.Y + r.Height / 2;
        bool narrow = r.Width < Px(400) && Theme.HasIcons;
        bool done = game && n?.Status == "DUZELTILDI";
        int x = r.X + pad;
        Theme.Draw(g, item.Text, Theme.Time, new Rectangle(x, r.Y, list.TimeWidth + 1, r.Height), done ? Theme.Faint : Theme.Text);
        x += list.TimeWidth + gap;
        var kindGlyph = Theme.KindGlyph(n?.Kind);
        var color = Theme.Kind(n?.Kind);
        if (narrow) x += Theme.Badge(g, list, kindGlyph, "", color, back, x, cy) + gap;
        else
        {
            Theme.Badge(g, list, kindGlyph, BadgeText(item.SubItems[1].Text), color, back, x, cy);
            x += list.BadgeWidth + gap;
        }
        int right = r.Right - pad;
        if (game && n != null)
        {
            string attach = n.Clip != null ? Theme.Glyph.Clip : n.Shot != null ? Theme.Glyph.Camera : null;
            if (attach != null && Theme.HasIcons)
                TextRenderer.DrawText(g, attach, Theme.Icons(10f), new Rectangle(right - Px(16), r.Y, Px(16), r.Height), Theme.Muted, Theme.Line | TextFormatFlags.HorizontalCenter);
            else if (attach != null) Theme.Draw(g, n.Clip != null ? "▶" : "SS", Theme.Small, new Rectangle(right - Px(18), r.Y, Px(18), r.Height), Theme.Muted);
            right -= Px(26);
            var state = item.SubItems[2].Text;
            if (state.Length > 0)
            {
                var stateColor = StateColor(n);
                if (narrow)
                {
                    int d = Px(7);
                    Theme.FillRound(g, stateColor, new Rectangle(right - d, cy - d / 2, d, d), d / 2f);
                    right -= d + gap;
                }
                else
                {
                    int w = Math.Min(Theme.Width(state, Theme.Small), Math.Max(0, (right - x) / 2));
                    Theme.Draw(g, state, Theme.Small, new Rectangle(right - w, r.Y, w, r.Height), stateColor);
                    right -= w + gap;
                }
            }
        }
        var textItem = item.SubItems[item.SubItems.Count - 1];
        var textColor = done ? Theme.Faint : !game && textItem.ForeColor == Warn ? Theme.Warn : Theme.Text;
        Theme.Draw(g, textItem.Text, done ? StrikeFont : Theme.Body, new Rectangle(x, r.Y, Math.Max(0, right - x), r.Height), textColor);
    }

    // The timeline shows the marks of the open notes tab only; the Kurgu tab also paints cut and bleep ranges.
    void RefreshMarkers()
    {
        seek.Markers.Clear();
        foreach (var n in CurrentNotes().Where(InNoteTab)) seek.Markers.Add(((long)(n.T * 1000), Theme.Kind(n.Kind)));
        RefreshSeekRanges();
    }

    // Note templates for the text box: a "Hazır" menu and suggestions from earlier notes.
    void RefreshTemplates()
    {
        if (noteBox.Focused) return;   // changing the suggestion source while its list is open can crash
        var texts = TextLists.Templates().Concat(store.All.Where(n => !Kinds.IsEdit(n.Kind) && n.Text.Length > 0).Select(n => n.Text))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var source = new AutoCompleteStringCollection();
        source.AddRange(texts);
        noteBox.AutoCompleteCustomSource = source;
    }

    void ShowTemplates(Control anchor)
    {
        var menu = new ContextMenuStrip();
        foreach (var t in TextLists.Templates())
            menu.Items.Add(t, null, (s, e) =>
            {
                noteBox.Text = t;
                FocusNoteBox();
                noteBox.SelectionStart = noteBox.Text.Length;
            });
        if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Listeyi düzenle…", null, (s, e) =>
        {
            TextLists.Templates();
            Process.Start(new ProcessStartInfo(TextLists.TemplatesFile) { UseShellExecute = true });
        });
        menu.Closed += (s, e) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    // ---------- Side panel width ----------

    bool panelLaidOut, fittingPanel;
    int panelWidth = 470;   // logical width of the side panel; follows the splitter when it is dragged

    // Needs a real window size; a minimized start lays the panel out on the first restore.
    void LayoutPanel()
    {
        if (split.Width < Px(600)) return;
        split.Panel1MinSize = Px(200);
        split.Panel2MinSize = Px(340);
        panelLaidOut = true;
        FitPanel();
    }

    // Keeps the panel at its width while the video column gets at least ~560 px, but never below the panel's minimum.
    void FitPanel()
    {
        if (!panelLaidOut || split.Panel2Collapsed) return;
        int w = Math.Max(Px(340), Math.Min(Px(panelWidth), split.Width - split.SplitterWidth - Px(560)));
        int distance = split.Width - split.SplitterWidth - w;
        if (distance < split.Panel1MinSize || distance == split.SplitterDistance) return;
        fittingPanel = true;
        try { split.SplitterDistance = distance; } catch { }
        fittingPanel = false;
    }

    void SaveReport() => SaveReportOf(currentVideo);

    // The open video's report in its folder; all videos (video null) in the KareNot root.
    void SaveReportOf(string video)
    {
        if (video == null && store.All.Count == 0) { SetStatus("Rapora girecek not yok."); return; }
        try
        {
            var path = store.WriteReport(video, out var missing);
            SetStatus("Rapor kaydedildi: " + path + (missing > 0 ? $" ({missing} SS bulunamadı)" : ""));
        }
        catch (Exception ex)
        {
            SetStatus("HATA: rapor yazılamadı: " + ex.Message);
        }
    }

    void OpenFolder()
    {
        var dir = Paths.Dir(currentVideo);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    // Opens the open video's notlar.txt in the text editor; edits saved there are read back when KareNot is activated again.
    void OpenNotesFile()
    {
        if (currentVideo == null)
        {
            SetStatus("Önce bir video aç; kayıt dosyası videonun klasöründe: " + Path.Combine(Paths.Root, "<video>", "notlar.txt"));
            return;
        }
        var file = Paths.NotesFile(currentVideo);
        if (!File.Exists(file))
        {
            SetStatus("Bu videoda henüz not yok; ilk notla birlikte oluşur: " + file);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            SetStatus("Kayıt dosyası açıldı. Değiştirip kaydedersen KareNot'a dönünce notlar yeniden okunur: " + file);
        }
        catch (Exception ex) { SetStatus("HATA: kayıt dosyası açılamadı: " + ex.Message); }
    }

    // Reads the notes again when notlar.txt of the open video was changed in another program (OpenNotesFile).
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (currentVideo == null || !store.ChangedOnDisk(currentVideo)) return;
        ReloadNotes();
        SetStatus("Kayıt dosyası dışarıda değişmiş; notlar yeniden okundu.");
    }

    // After the notes folder or a notes file changed outside the store.
    void ReloadNotes()
    {
        store.Load();
        ReviewStore.Reload();
        RefreshList();
        RefreshSeekRanges();
        tips.SetToolTip(saveIndicator, "Notlar her değişiklikte kaydedilir: " + Path.Combine(Paths.Root, "<video>", "notlar.txt"));
    }

    void SetStatus(string text)
    {
        status.Text = text;
        tips.SetToolTip(status, text);
    }

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
            ("Home", "Videonun başına git (açılınca kaldığın yerden devam eder)"),
            ("[ / ]", "Açık sekmenin önceki / sonraki işareti (Ctrl + ← / → da olur)"),
            ("L", "Seçili notun öncesini ve sonrasını döngüde izle / kapat"),
            ("I", "Buraya kadar inceledim (sona yakınsa video tamamlanır)"),
            ("Zaman çubuğu", "Üzerine gelince o anın küçük karesi görünür (ffmpeg gerekir)"),
            ("Videolar sekmesi", "Klasördeki videolar ve inceleme durumu; çift tık / Enter açar"),
        }),
        ("OYUN TESTİ (yazı kutusu dışındayken)", new[]
        {
            ("B", "BUG"),
            ("D", "DEĞİŞİKLİK"),
            ("N", "NOT (önce kutuya yazılmalı)"),
            ("S", "Seçili nota o anki kareyi SS ekle"),
            ("C", "Seçili not için kısa video klibi çıkar"),
            ("Enter", "Videoyu durdur, yazı kutusuna geç"),
            ("Ctrl+F", "Notlarda ara (#etiket de olur)"),
            ("Videoda sürükle", "Video durur, alan kırmızı çerçeveyle işaretlenir; notu yaz ve türünü seç"),
            ("Esc", "Alan seçimini iptal et"),
            ("Sağ tık", "Durum (Açık / Düzeltildi / Tekrar test) ve önem"),
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
            ("K", "BİP (küfür); basılı tutarsan bıraktığın yere kadar aralık olur"),
            ("1", "Kesim BAŞLA"),
            ("2", "Kesim BİTİR"),
            ("X", "Kesilmiş hâli izle (kesim aralıklarını atlar) aç / kapat"),
        }),
        ("LİSTE (açık sekmenin listesi)", new[]
        {
            ("Çift tık, G", "Seçili nota git (2 sn önce)"),
            ("F2", "Düzenle (tür, yazı, durum, önem, BİP süresi)"),
            ("Del", "Seçileni sil (seçim yoksa son notu)"),
            ("Ctrl+Z", "Geri al"),
            ("Ctrl+Y", "Yinele"),
            ("Sağ tık, ⋯", "Menü (durum, önem, SS'i aç, döngü, klip, görev metni, sil)"),
        }),
        ("SEKMELER", new[]
        {
            ("F1", "Kısayollar sekmesini aç / kapat"),
            ("Hepsi", "Kısayollar her sekmede çalışır; eklenen not kendi sekmesini açar"),
        }),
        ("RAPOR", new[]
        {
            ("Ctrl+S", "Raporu kaydet (rapor.md ve rapor.html, <not klasörü>\\<video>\\; video yoksa tüm videolar)"),
            ("Ctrl+P", "Bulgu paketi (ZIP): rapor, görev listesi, SS'ler ve klipler"),
            ("Dışa aktar ▾", "CSV, görev listesi, kurgu notları (JSON), DaVinci Resolve işaretleri, bipli/kesilmiş video"),
            ("Araçlar ▾", "Ayarlar, küfür adayları, ölü anlar, listeleri düzenle"),
        }),
    };

    void BuildShortcutsPage()
    {
        shortcutsPage.Controls.Add(new ShortcutView(ShortcutGroups) { Dock = DockStyle.Top });
        Theme.DarkScrollBars(shortcutsPage);
    }

    // Switches the side panel page; does not open a hidden panel.
    void ShowTab(PanelTab show)
    {
        if (show != tab && (noteBox.Focused || ListFocused)) ActiveControl = null;
        tab = show;
        if (show is PanelTab.Game or PanelTab.Edit && show != noteTab)
        {
            noteTab = show;
            RefreshMarkers();
        }
        gamePage.Visible = show == PanelTab.Game;
        editPage.Visible = show == PanelTab.Edit;
        videosPage.Visible = show == PanelTab.Videos;
        shortcutsPage.Visible = show == PanelTab.Shortcuts;
        tabStrip.Selected = (int)show;
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

    bool Typing => noteBox.Focused || searchBox.Focused;

    void FocusSearch()
    {
        if (split.Panel2Collapsed) TogglePanel();
        ShowTab(PanelTab.Game);
        searchBox.Focus();
        searchBox.SelectAll();
    }

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
            case Keys.Control | Keys.F: FocusSearch(); return true;
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
                if (searchBox.Focused) ActiveControl = gameList;
                else AddNote("NOT", true);
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
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
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
        if (featureKeys.TryGetValue(keyData, out var feature))
        {
            feature();
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
            case '1': AddNote("KES_BASLA", false); break;
            case '2': AddNote("KES_BITIR", false); break;
            case 's': ShotToSelected(); break;
            case 'g': GotoSelected(); break;
            case 'f': ToggleFullscreen(); break;
            case '.': ClearRegion(); player?.StepFrame(1); break;
            case ',': ClearRegion(); player?.StepFrame(-1); break;
            case '+': SetRate(rateIndex + 1); break;
            case '-': SetRate(rateIndex - 1); break;
            default:
                if (featureChars.TryGetValue(char.ToLowerInvariant(e.KeyChar), out var feature)) feature();
                else e.Handled = false;
                break;
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
