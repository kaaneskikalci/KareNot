using System.Text.Json.Nodes;

namespace KareNot;

// Review features: resume position, "reviewed up to here", the Videolar tab, jumping between marks, looping around a note,
// watching with the cuts skipped and the frame preview over the seek bar. Progress lives in ReviewStore (inceleme.json).
sealed partial class MainForm
{
    // Resume: the saved position is applied once the length is known; positions are saved every 5 s and on close.
    bool resumePending;
    double savedPosition = -1;
    long positionSavedAt;

    // Loop around a note and cut skipping; the checks ignore the player for a moment after our own seeks.
    bool looping, skipCuts;
    long loopStartMs, loopEndMs, reviewGraceUntil;
    List<(double Start, double End)> cutRanges = new();
    ToolStripMenuItem skipCutsItem;
    Note lastJump;

    // Videolar tab
    readonly VideoListView videoList = new() { ShowItemToolTips = true, RowHeight = 48 };
    readonly Card videosCard = new();
    readonly Label videosFolder = new() { AutoSize = false, Height = 20, AutoEllipsis = true, ForeColor = Theme.Faint, Font = Theme.Small, UseMnemonic = false };
    readonly Label videosHint = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Faint, Font = Theme.Body,
        Text = "Bir video açınca klasöründeki videolar burada listelenir:\ndurum, not sayıları ve inceleme ilerlemesi.",
    };
    string lastBuild = "", lastTester = "";

    // Seek bar hover preview
    readonly System.Windows.Forms.Timer hoverTimer = new() { Interval = 150 };
    readonly Dictionary<int, Bitmap> frameCache = new();   // whole second -> frame (null: ffmpeg failed)
    readonly Queue<int> frameOrder = new();
    SeekPreviewPopup preview;
    long? hoverMs;
    bool frameBusy, ffmpegFound;

    partial void RegisterReview()
    {
        VideoOpened += ReviewVideoOpened;
        VideoClosing += ReviewVideoClosing;
        Tick += ReviewTick;
        NotesChanged += () =>
        {
            RefreshSeekRanges();
            if (videosPage.Visible) RefreshVideos();
        };

        featureKeys[Keys.Home] = () =>
        {
            if (player?.HasMedia != true) return;
            ClearRegion();
            player.Seek(0);
            SetStatus("Baştan.");
        };
        featureKeys[Keys.Control | Keys.Left] = () => JumpMark(-1);
        featureKeys[Keys.Control | Keys.Right] = () => JumpMark(1);
        featureChars['['] = () => JumpMark(-1);
        featureChars[']'] = () => JumpMark(1);
        featureChars['i'] = featureChars['ı'] = MarkReviewed;
        featureChars['l'] = ToggleLoop;
        featureChars['x'] = ToggleSkipCuts;

        gameMenu.Items.Insert(1, Item("Döngüde izle", "L", () => StartLoop(SelectedNote)));
        editMenu.Items.Insert(1, Item("Döngüde izle", "L", () => StartLoop(SelectedNote)));
        skipCutsItem = Item("Kesilmiş hâli izle", "X", ToggleSkipCuts);
        toolsMenu.Items.Add(skipCutsItem);

        BuildVideosPage();
        seek.HoverChanged += SeekHover;
        hoverTimer.Tick += (s, e) =>
        {
            hoverTimer.Stop();
            RequestFrame();
        };
        Deactivate += (s, e) => HidePreview();
        FormClosed += (s, e) => hoverTimer.Stop();
    }

    void ReviewVideoOpened()
    {
        StopLoop(null);
        lastJump = null;
        resumePending = true;
        savedPosition = -1;
        positionSavedAt = Environment.TickCount64;
        seek.ReviewedMs = (long)(ReviewStore.Reviewed(currentVideo) * 1000);
        ClearFrameCache();
        RefreshSeekRanges();
        TryResume();
        if (videosPage.Visible) RefreshVideos();
    }

    void ReviewVideoClosing()
    {
        if (player?.HasMedia == true && !resumePending) SavePosition();
        StopLoop(null);
        hoverMs = null;
        HidePreview();
    }

    void ReviewTick()
    {
        if (player?.HasMedia != true || currentVideo == null) return;
        TryResume();
        if (!resumePending && Environment.TickCount64 - positionSavedAt > 5000) SavePosition();
        if (Environment.TickCount64 < reviewGraceUntil || RegionActive) return;

        long t = player.TimeMs;
        if (looping)
        {
            if (t < loopStartMs - 1000 || t > loopEndMs + 1000) StopLoop("Döngü kapandı.");
            else if (player.IsPlaying && t >= loopEndMs)
            {
                ReviewSeek(loopStartMs);
                return;
            }
        }
        if (skipCuts && player.IsPlaying)
        {
            double s = t / 1000.0;
            foreach (var (a, b) in cutRanges)
            {
                if (s < a || s >= b - 0.05) continue;
                if (player.LengthMs > 0 && b * 1000 >= player.LengthMs - 300)
                {
                    player.Pause();
                    SetStatus("Kesim videonun sonuna kadar sürüyor; durduruldu.");
                }
                else
                {
                    ReviewSeek((long)(b * 1000) + 10);
                    SetStatus($"Kesim atlandı: {NoteStore.Fmt(a)}–{NoteStore.Fmt(b)} (X: kapat)");
                }
                break;
            }
        }
    }

    void ReviewSeek(long ms)
    {
        player.Seek(ms);
        reviewGraceUntil = Environment.TickCount64 + 600;
    }

    // ---------- Resume and review progress ----------

    void TryResume()
    {
        if (!resumePending || player?.HasMedia != true) return;
        long length = player.LengthMs;
        // VLC may ignore a position set while it is still opening.
        if (length <= 0 || player.State is not (LibVlc.StatePlaying or LibVlc.StatePaused)) return;
        resumePending = false;
        ReviewStore.Set(currentVideo, "length", Math.Round(length / 1000.0, 1));
        if (videosPage.Visible) RefreshVideos();
        double pos = ReviewStore.Position(currentVideo);
        savedPosition = pos;
        if (pos < 5 || pos > length / 1000.0 - 5) return;
        ReviewSeek((long)(pos * 1000));
        SetStatus($"Kaldığın yerden devam: {NoteStore.Fmt(pos)} (Home: baştan)");
    }

    void SavePosition()
    {
        positionSavedAt = Environment.TickCount64;
        double pos = player.TimeMs / 1000.0;
        if (Math.Abs(pos - savedPosition) < 0.5) return;
        savedPosition = pos;
        ReviewStore.SetPosition(currentVideo, pos);
    }

    void MarkReviewed()
    {
        if (player?.HasMedia != true)
        {
            SetStatus("Önce bir video aç.");
            return;
        }
        double t = player.TimeMs / 1000.0, length = player.LengthMs / 1000.0;
        ReviewStore.Set(currentVideo, "reviewed", Math.Round(t, 1));
        seek.ReviewedMs = (long)(t * 1000);
        seek.Invalidate();
        if (length > 0 && t >= length - 2)
        {
            ReviewStore.Set(currentVideo, "done", true);
            SetStatus("Video sonuna kadar incelendi: tamamlandı olarak işaretlendi.");
        }
        else SetStatus($"Buraya kadar incelendi: {NoteStore.Fmt(t)}" + (length > 0 ? $" (%{Math.Round(100 * t / length)})" : ""));
        if (videosPage.Visible) RefreshVideos();
    }

    // ---------- Marks: jump, loop, skip cuts ----------

    void JumpMark(int direction)
    {
        if (player?.HasMedia != true) return;
        var notes = CurrentNotes().Where(InNoteTab).ToList();
        double t = player.TimeMs / 1000.0;
        // Right after a jump the player stands 1 s before the mark; count from the mark itself so the next press moves on.
        double from = lastJump != null && notes.Contains(lastJump) && Math.Abs(t - Math.Max(0, lastJump.T - 1)) < 1.5 ? lastJump.T : t;
        var n = direction > 0 ? notes.FirstOrDefault(x => x.T > from + 0.3) : notes.LastOrDefault(x => x.T < from - 0.3);
        if (n == null)
        {
            SetStatus(direction > 0 ? "Sonrasında işaret yok." : "Öncesinde işaret yok.");
            return;
        }
        lastJump = n;
        ClearRegion();
        player.Seek((long)(Math.Max(0, n.T - 1) * 1000));
        foreach (ListViewItem item in ActiveList.Items)
        {
            if (item.Tag != n) continue;
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            break;
        }
        SetStatus($"{(direction > 0 ? "Sonraki" : "Önceki")} işaret: {NoteStore.Fmt(n.T)} {Kinds.Label(n.Kind)}" + (n.Text.Length > 0 ? " — " + n.Text : ""));
    }

    void ToggleLoop()
    {
        if (looping) StopLoop("Döngü kapatıldı.");
        else StartLoop(SelectedNote);
    }

    // Loops from LoopBefore seconds before the note to LoopAfter seconds after its end (a cut mark: its whole cut).
    void StartLoop(Note n)
    {
        if (player?.HasMedia != true)
        {
            SetStatus("Önce bir video aç.");
            return;
        }
        var notes = CurrentNotes();
        double t = player.TimeMs / 1000.0;
        n ??= notes.Where(InNoteTab).OrderBy(x => Math.Abs(x.T - t)).FirstOrDefault();
        if (n == null)
        {
            SetStatus("Döngü için listeden bir not seç.");
            return;
        }
        double a = n.T, b = n.End ?? n.T;
        if (n.Kind is "KES_BASLA" or "KES_BITIR")
        {
            var cut = Kinds.Cuts(notes).FirstOrDefault(c => c.Start == n || c.End == n);
            a = cut.Start?.T ?? n.T;
            b = cut.End?.T ?? n.T;
        }
        loopStartMs = (long)(Math.Max(0, a - settings.LoopBefore) * 1000);
        loopEndMs = (long)((b + settings.LoopAfter) * 1000);
        if (player.LengthMs > 0) loopEndMs = Math.Min(loopEndMs, player.LengthMs - 300);
        loopEndMs = Math.Max(loopEndMs, loopStartMs + 500);
        looping = true;
        ClearRegion();
        ReviewSeek(loopStartMs);
        if (!player.IsPlaying) player.Play();
        SetStatus($"Döngü: {NoteStore.Fmt(loopStartMs / 1000.0)}–{NoteStore.Fmt(loopEndMs / 1000.0)} (L: kapat)");
    }

    void StopLoop(string message)
    {
        if (!looping) return;
        looping = false;
        if (message != null) SetStatus(message);
    }

    void ToggleSkipCuts()
    {
        skipCuts = !skipCuts;
        skipCutsItem.Checked = skipCuts;
        RefreshSeekRanges();
        if (!skipCuts) SetStatus("Kesimleri atlama kapalı.");
        else if (cutRanges.Count == 0) SetStatus("Kesilmiş hâli izle açık, ama bu videoda tamamlanmış kesim yok (X: kapat).");
        else SetStatus($"Kesilmiş hâli izleniyor: {cutRanges.Count} kesim atlanacak (X: kapat).");
    }

    // Cut ranges (also used by the cut skipping) and bleep ranges as bands on the seek bar while the Kurgu tab is the notes tab.
    void RefreshSeekRanges()
    {
        var notes = CurrentNotes();
        cutRanges = Kinds.CutRanges(notes);
        seek.Ranges.Clear();
        bool edit = noteTab == PanelTab.Edit;
        if (edit || skipCuts)
            foreach (var (s, e) in cutRanges) seek.Ranges.Add(((long)(s * 1000), (long)(e * 1000), Kinds.Color("KES_BASLA")));
        if (edit)
            foreach (var (s, e) in Kinds.BeepRanges(notes, settings.BeepDefault)) seek.Ranges.Add(((long)(s * 1000), (long)(e * 1000), Kinds.Color("BIP")));
        seek.Invalidate();
    }

    // ---------- Videolar tab ----------

    void BuildVideosPage()
    {
        videoList.Columns.Add("Video");
        videoList.Columns.Add("Durum");
        videoList.Columns.Add("Notlar");
        videoList.Columns.Add("İnceleme");
        videoList.DoubleClick += (s, e) => OpenSelectedVideo();
        videoList.EnterPressed += OpenSelectedVideo;
        videoList.DrawItem += DrawVideoRow;
        var open = Btn("Aç", OpenSelectedVideo, "Seçili videoyu aç (Enter ya da çift tık)", Theme.Glyph.OpenFolder);
        var done = Btn("Tamamlandı", ToggleDone, "Seçili videoyu tamamlandı olarak işaretle / işareti kaldır", Theme.Glyph.Check);
        var info = Btn("Oturum bilgisi…", EditSessionInfo, "Seçili kaydın build'i ve testçisi");
        var actions = new SplitRow(34) { Gap = 6 }.L(open, done, info);
        actions.Compactable.AddRange(new[] { done, open });
        AddCard(videosCard);
        Stack(videosCard, videoList,
            new Control[] { Tall(new SplitRow(24).L(Heading("Klasördeki videolar"))), Tall(new Spacer(2)), videosFolder, new Spacer(8) },
            new Control[] { new Spacer(8), actions });
        videosPage.Controls.Add(videosCard);
        videosCard.Dock = DockStyle.Fill;
        videosPage.Controls.Add(videosHint);
        videosPage.VisibleChanged += (s, e) =>
        {
            // After the page is laid out: filled while still zero-sized, the list keeps a wrong scroll offset.
            if (videosPage.Visible) BeginInvoke(RefreshVideos);
            else if (videoList.Focused) ActiveControl = null;
        };
        RefreshVideos();
    }

    void RefreshVideos()
    {
        var path = player?.FilePath;
        videosHint.Visible = path == null;
        videosCard.Visible = path != null;
        if (path == null) return;
        var dir = Path.GetDirectoryName(path);
        List<string> files;
        try
        {
            files = Directory.GetFiles(dir).Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { files = new List<string> { path }; }
        videosFolder.Text = dir;
        tips.SetToolTip(videosFolder, dir);

        var byVideo = store.All.GroupBy(n => n.Video).ToDictionary(g => g.Key, g => g.ToList());
        var lengths = StoredLengths();
        var selected = SelectedVideoPath();
        videoList.BeginUpdate();
        videoList.Items.Clear();
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            var notes = byVideo.GetValueOrDefault(name) ?? new List<Note>();
            bool isOpen = string.Equals(f, path, StringComparison.OrdinalIgnoreCase);
            double length = isOpen && player.LengthMs > 0 ? player.LengthMs / 1000.0 : lengths.GetValueOrDefault(name);
            double reviewed = ReviewStore.Reviewed(name);
            var state = ReviewStore.State(name, notes.Count > 0);
            var item = new ListViewItem(name) { Tag = f, UseItemStyleForSubItems = false };
            item.SubItems.Add(state);
            item.SubItems.Add(NoteSummary(notes));
            item.SubItems.Add(length > 0 && reviewed > 0 ? "%" + (int)Math.Round(100 * Math.Min(1, reviewed / length)) : "");
            var info = new[] { ReviewStore.Build(name) is { Length: > 0 } b ? "Build: " + b : null, ReviewStore.Tester(name) is { Length: > 0 } te ? "Testçi: " + te : null };
            item.ToolTipText = string.Join(" · ", info.Where(x => x != null));
            videoList.Items.Add(item);
            if (string.Equals(f, selected, StringComparison.OrdinalIgnoreCase))
            {
                item.Selected = true;
                item.EnsureVisible();
            }
        }
        videoList.EndUpdate();
        videoList.FitColumns();
    }

    // Video row: name (the open one marked with an orange dot) and review state; note counts and review progress below.
    void DrawVideoRow(object sender, DrawListViewItemEventArgs e)
    {
        var g = e.Graphics;
        var r = e.Bounds;
        var item = e.Item;
        DrawRowBack(g, r, item.Selected);
        bool isOpen = string.Equals(item.Tag as string, player?.FilePath, StringComparison.OrdinalIgnoreCase);
        int pad = Px(10), gap = Px(10), half = r.Height / 2, x = r.X + pad, right = r.Right - pad;
        var top = new Rectangle(x, r.Y + Px(3), 0, half - Px(3));
        var bottom = new Rectangle(x, r.Y + half, 0, half - Px(4));
        var state = item.SubItems[1].Text;
        var stateColor = state switch { "Tamamlandı" => Theme.Ok, "Devam ediyor" => Theme.Warn, _ => Theme.Faint };
        int sw = Theme.Width(state, Theme.Small);
        Theme.Draw(g, state, Theme.Small, new Rectangle(right - sw, top.Y, sw, top.Height), stateColor);
        if (isOpen)
        {
            int d = Px(7);
            Theme.FillRound(g, Theme.Accent, new Rectangle(x, top.Y + (top.Height - d) / 2, d, d), d / 2f);
            x += d + Px(7);
        }
        Theme.Draw(g, item.Text, isOpen ? Theme.Strong : Theme.Body, new Rectangle(x, top.Y, Math.Max(0, right - sw - gap - x), top.Height), Theme.Text);

        // Progress: a short bar with the percentage.
        var progress = item.SubItems[3].Text;
        int barRight = right;
        if (progress.Length > 0)
        {
            int pw = Theme.Width("%100", Theme.Small), bw = Px(48), bh = Px(4);
            Theme.Draw(g, progress, Theme.Small, new Rectangle(right - pw, bottom.Y, pw, bottom.Height), Theme.Muted, TextFormatFlags.Right);
            var bar = new Rectangle(right - pw - Px(6) - bw, bottom.Y + (bottom.Height - bh) / 2, bw, bh);
            Theme.FillRound(g, Theme.Mix(Theme.Raised, Theme.Text, 0.12f), bar, bh / 2f);
            if (int.TryParse(progress.TrimStart('%'), out var p) && p > 0)
                Theme.FillRound(g, Theme.Ok, new Rectangle(bar.X, bar.Y, Math.Max(bh, bw * Math.Min(p, 100) / 100), bh), bh / 2f);
            barRight = bar.X - gap;
        }
        var summary = item.SubItems[2].Text;
        Theme.Draw(g, summary.Length > 0 ? summary : "not yok", Theme.Small, new Rectangle(r.X + pad, bottom.Y, Math.Max(0, barRight - r.X - pad), bottom.Height),
            summary.Length > 0 ? Theme.Muted : Theme.Faint);
    }

    static string NoteSummary(List<Note> notes)
    {
        var parts = new List<string>();
        void Part(int count, string label)
        {
            if (count > 0) parts.Add(count + " " + label);
        }
        Part(notes.Count(n => n.Kind == "BUG"), "bug");
        Part(notes.Count(n => n.Kind == "DEGISIKLIK"), "değ.");
        Part(notes.Count(n => n.Kind == "NOT"), "not");
        Part(notes.Count(n => Kinds.IsEdit(n.Kind)), "kurgu");
        return string.Join(" · ", parts);
    }

    // Video lengths stored by earlier openings ("length" in inceleme.json), for the progress of videos that are not open.
    static Dictionary<string, double> StoredLengths()
    {
        var result = new Dictionary<string, double>();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(Paths.ReviewFile)) is JsonObject root)
                foreach (var (video, entry) in root)
                    if ((entry as JsonObject)?["length"] is JsonValue v && v.TryGetValue<double>(out var d)) result[video] = d;
        }
        catch { }
        return result;
    }

    string SelectedVideoPath() => videoList.SelectedItems.Count > 0 ? videoList.SelectedItems[0].Tag as string : player?.FilePath;

    void OpenSelectedVideo()
    {
        var f = SelectedVideoPath();
        if (f == null) return;
        ActiveControl = null;
        if (string.Equals(f, player?.FilePath, StringComparison.OrdinalIgnoreCase)) SetStatus("Bu video zaten açık.");
        else OpenVideo(f);
    }

    void ToggleDone()
    {
        var f = SelectedVideoPath();
        if (f == null)
        {
            SetStatus("Listeden bir video seç.");
            return;
        }
        var name = Path.GetFileName(f);
        bool done = !ReviewStore.Done(name);
        ReviewStore.Set(name, "done", done);
        RefreshVideos();
        SetStatus(done ? "Tamamlandı olarak işaretlendi: " + name : "Tamamlandı işareti kaldırıldı: " + name);
    }

    void EditSessionInfo()
    {
        var f = SelectedVideoPath();
        if (f == null)
        {
            SetStatus("Listeden bir video seç.");
            return;
        }
        var name = Path.GetFileName(f);
        // Empty fields start with the values entered last, as one session usually covers several videos.
        var build = ReviewStore.Build(name) is { Length: > 0 } b ? b : lastBuild;
        var tester = ReviewStore.Tester(name) is { Length: > 0 } t ? t : lastTester;
        using var dlg = new SessionInfoDialog(name, build, tester);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        lastBuild = NoteStore.Clean(dlg.Build);
        lastTester = NoteStore.Clean(dlg.Tester);
        ReviewStore.Set(name, "build", lastBuild);
        ReviewStore.Set(name, "tester", lastTester);
        RefreshVideos();
        SetStatus("Oturum bilgisi kaydedildi: " + name);
    }

    // ---------- Seek bar hover preview ----------

    void SeekHover(long? ms)
    {
        if (ms == null || player?.HasMedia != true)
        {
            hoverMs = null;
            hoverTimer.Stop();
            HidePreview();
            return;
        }
        if (hoverMs == null) ffmpegFound = Ffmpeg.Find() != null;
        hoverMs = ms;
        ShowPreview();
        hoverTimer.Stop();
        hoverTimer.Start();
    }

    // Shows the time at once, with the frame when it is cached (a placeholder while ffmpeg fetches it).
    void ShowPreview()
    {
        if (hoverMs is not long ms || IsDisposed) return;
        bool cached = frameCache.TryGetValue((int)(ms / 1000), out var frame);
        if (preview == null || preview.IsDisposed) preview = new SeekPreviewPopup { Owner = this };
        var top = seek.PointToScreen(Point.Empty).Y;
        preview.ShowAt(frame, ffmpegFound && !cached, NoteStore.Fmt(ms / 1000.0), Cursor.Position.X, top - 6, Screen.FromControl(seek).Bounds);
    }

    void HidePreview()
    {
        if (preview != null && !preview.IsDisposed && preview.Visible) preview.Visible = false;
    }

    // One ffmpeg at a time on a worker thread; a result for another video or an old position is cached or dropped.
    async void RequestFrame()
    {
        if (hoverMs is not long ms || frameBusy || !ffmpegFound || player?.FilePath is not string path) return;
        int sec = (int)(ms / 1000);
        if (frameCache.ContainsKey(sec)) return;
        frameBusy = true;
        Bitmap frame;
        try { frame = await Task.Run(() => Ffmpeg.Frame(path, sec, 192)); }
        finally { frameBusy = false; }
        if (IsDisposed || path != player?.FilePath)
        {
            frame?.Dispose();
            return;
        }
        AddFrame(sec, frame);
        if (hoverMs is not long now) return;
        if ((int)(now / 1000) == sec) ShowPreview();
        else if (!hoverTimer.Enabled) RequestFrame();
    }

    void AddFrame(int sec, Bitmap frame)
    {
        if (frameCache.ContainsKey(sec))
        {
            frame?.Dispose();
            return;
        }
        frameCache[sec] = frame;
        frameOrder.Enqueue(sec);
        while (frameOrder.Count > 120)
        {
            if (frameCache.Remove(frameOrder.Dequeue(), out var old) && old != null && old != preview?.Image) old.Dispose();
        }
    }

    void ClearFrameCache()
    {
        HidePreview();
        preview?.Clear();
        foreach (var f in frameCache.Values) f?.Dispose();
        frameCache.Clear();
        frameOrder.Clear();
    }
}

// Video list of the Videolar tab: Enter opens, arrows / Home / End / Page keys move in the list instead of driving the player.
sealed class VideoListView : DarkListView
{
    public event Action EnterPressed;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Enter:
                EnterPressed?.Invoke();
                return true;
            case Keys.Escape:
                if (FindForm() is Form form) form.ActiveControl = null;
                return true;
            case Keys.Up:
            case Keys.Down:
            case Keys.Home:
            case Keys.End:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Delete:
                return false;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

// Borderless window above the seek bar with the hovered frame and its time; never takes focus.
sealed class SeekPreviewPopup : Form
{
    static readonly Font TimeFont = Theme.Mono(9f);
    Bitmap image;
    bool placeholder;
    string time = "";
    Size imageSize = new(192, 108);

    public SeekPreviewPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Raised;
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundCorners(this);
    }

    public Bitmap Image => image;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80;   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21)   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        {
            m.Result = 3;
            return;
        }
        base.WndProc(ref m);
    }

    public void Clear()
    {
        image = null;
        Invalidate();
    }

    // Frame (or a dark placeholder while loading, or only the time) centered above screenX with its bottom at bottomY.
    public void ShowAt(Bitmap frame, bool loading, string text, int screenX, int bottomY, Rectangle screen)
    {
        image = frame;
        placeholder = frame == null && loading;
        time = text;
        if (frame != null) imageSize = frame.Size;
        float s = DeviceDpi / 96f;
        var size = frame != null || placeholder ? new Size(imageSize.Width + 8, imageSize.Height + (int)(28 * s)) : new Size((int)(96 * s), (int)(26 * s));
        int x = Math.Clamp(screenX - size.Width / 2, screen.Left, Math.Max(screen.Left, screen.Right - size.Width));
        var bounds = new Rectangle(x, Math.Max(screen.Top, bottomY - size.Height), size.Width, size.Height);
        if (Bounds != bounds) Bounds = bounds;
        Invalidate();
        if (!Visible) Visible = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int textTop = 0;
        if (image != null || placeholder)
        {
            var r = new Rectangle(4, 4, imageSize.Width, imageSize.Height);
            if (image != null) g.DrawImage(image, r);
            else using (var b = new SolidBrush(Theme.Field)) g.FillRectangle(b, r);
            textTop = r.Bottom;
        }
        TextRenderer.DrawText(g, time, TimeFont, new Rectangle(0, textTop, Width, Height - textTop), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var border = new Pen(Theme.Border);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

// Build and tester of one recording, kept in inceleme.json.
sealed class SessionInfoDialog : DarkDialog
{
    readonly TextBox build = new() { Width = 300 }, tester = new() { Width = 300 };

    public string Build => build.Text;
    public string Tester => tester.Text;

    public SessionInfoDialog(string video, string buildText, string testerText)
        : base("Oturum bilgisi — " + video)
    {
        build.Text = buildText;
        tester.Text = testerText;
        AddRow(Lbl("Build"), Field(build));
        AddRow(Lbl("Testçi"), Field(tester));
        AddButton("Kaydet", DialogResult.OK);
    }
}
