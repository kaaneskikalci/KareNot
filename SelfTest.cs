using System.Runtime.InteropServices;
using System.Text;

namespace KareNot;

// "--selftest <log> <video>": drives the real window and player through the main features and logs the results.
sealed partial class MainForm
{
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);

    public async Task RunSelfTest(string logPath, string video)
    {
        var log = new StringBuilder();
        int failures = 0;
        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "OK   " : "FAIL ") + what);
            if (!ok) failures++;
        }
        void Info(string what) => log.AppendLine("     " + what);
        async Task Wait(int ms)
        {
            var end = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < end)
            {
                UpdatePlayerUi();
                await Task.Delay(50);
            }
        }

        try
        {
            await Wait(500);
            OpenVideo(video);
            Info("status after open: " + status.Text + " / exists " + File.Exists(video) + " / player " + (player != null));
            await Wait(2000);
            if (player.State == 0) { log.AppendLine("ABORT: " + LibVlc.LastError()); throw new Exception("not playing"); }
            Check(player.IsPlaying, "plays " + Path.GetFileName(video) + " state=" + player.State);
            Check(player.HasVideo, "has video output");
            Check(player.LengthMs > 0, "length " + player.LengthMs + " ms");

            var children = new List<string>();
            EnumChildWindows(videoPanel.Handle, (h, l) =>
            {
                var name = new StringBuilder(128);
                GetClassName(h, name, name.Capacity);
                bool disabled = (GetWindowLong(h, -16) & 0x08000000) != 0;
                children.Add(name + (disabled ? " [disabled]" : " [ENABLED]"));
                return true;
            }, IntPtr.Zero);
            Info("VLC windows: " + string.Join(", ", children));
            Check(children.Count > 0 && children[0].StartsWith("VLC video main") && children[0].EndsWith("[disabled]"), "VLC top window disabled (clicks go to the app)");

            player.Pause();
            await Wait(500);
            player.Seek(3000);
            await Wait(800);
            Check(Math.Abs(player.TimeMs - 3000) < 400, "seek to 3 s -> " + player.TimeMs);

            noteBox.Text = "test bug notu";
            AddNote("BUG", false);
            var bug = store.All.LastOrDefault(n => n.Video == currentVideo && n.Kind == "BUG");
            Check(bug != null && bug.Text == "test bug notu" && Math.Abs(bug.T - 3) < 0.5, "BUG note at 3 s");
            Check(bug?.Shot != null && File.Exists(Path.Combine(Paths.ShotsDir, bug.Shot)), "BUG screenshot " + bug?.Shot);
            if (bug?.Shot != null)
                using (var img = Image.FromFile(Path.Combine(Paths.ShotsDir, bug.Shot)))
                    Info($"screenshot size {img.Width}x{img.Height}");

            // Area selection through the same mouse handlers the window uses.
            var size = videoPanel.ClientSize;
            var a = new Point(size.Width / 4, size.Height / 4);
            var b = new Point(size.Width / 2, size.Height / 2);
            // Real mouse drag over the playing video (checks that VLC lets the mouse through).
            Activate();
            player.Play();
            await Wait(500);
            var oldCursor = Cursor.Position;
            SetCursorPos(videoPanel.PointToScreen(a).X, videoPanel.PointToScreen(a).Y);
            await Wait(150);
            var under = WindowFromPoint(videoPanel.PointToScreen(a));
            var name = new StringBuilder(128);
            GetClassName(under, name, name.Capacity);
            Info($"before drag: foreground={(GetForegroundWindow() == Handle ? "KareNot" : "other")}, under cursor={name} {(under == videoPanel.Handle ? "(videoPanel)" : "")}");
            mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
            await Wait(150);
            SetCursorPos(videoPanel.PointToScreen(a).X + 30, videoPanel.PointToScreen(a).Y + 30);
            await Wait(600);
            SetCursorPos(videoPanel.PointToScreen(b).X, videoPanel.PointToScreen(b).Y);
            await Wait(300);
            mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
            await Wait(300);
            SetCursorPos(oldCursor.X, oldCursor.Y);
            Check(RegionActive && overlay.Selection.Width > 10, "real mouse drag selected area " + overlay.Selection + " at " + NoteStore.Fmt(frozenMs / 1000.0));
            Check(!player.IsPlaying, "video paused while selecting");
            if (!RegionActive)
            {
                VideoMouseDown(videoPanel, new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
                VideoMouseMove(videoPanel, new MouseEventArgs(MouseButtons.Left, 0, a.X + 20, a.Y + 20, 0));
                VideoMouseMove(videoPanel, new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
                VideoMouseUp(videoPanel, new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));
                Check(RegionActive && overlay.Selection.Width > 10, "area selected (simulated) " + overlay.Selection);
            }
            var selection = overlay.Selection;
            await Wait(300);
            CaptureWindow(Path.ChangeExtension(logPath, ".alan.png"));
            noteBox.Text = "alan secili degisiklik";
            AddNote("DEGISIKLIK", false);
            var change = store.All.LastOrDefault(n => n.Video == currentVideo && n.Kind == "DEGISIKLIK");
            Check(change?.Shot != null, "DEGISIKLIK with marked screenshot");
            if (change?.Shot != null)
                using (var img = new Bitmap(Path.Combine(Paths.ShotsDir, change.Shot)))
                {
                    var edge = img.GetPixel(selection.X + 1, selection.Y + selection.Height / 2);
                    Check(edge.R > 200 && edge.G < 60 && edge.B < 60, "red frame on screenshot at selection edge " + edge);
                }
            Check(!RegionActive, "selection cleared after note");

            player.Seek(5000);
            await Wait(700);
            noteBox.Text = "taslak";
            ActiveControl = null;
            AddNote("BIP", false);
            var firstBip = store.All.LastOrDefault(n => n.Video == currentVideo && n.Kind == "BIP");
            Check(tab == PanelTab.Edit && editPage.Visible && !gamePage.Visible, "editing mark opens the Kurgu tab");
            Check(firstBip?.Text == "" && noteBox.Text == "taslak", "editing mark keeps the game note draft");
            noteBox.Clear();
            AddNote("KES_BASLA", false);
            player.Seek(7000);
            await Wait(700);
            AddNote("KES_BITIR", false);
            int before = store.All.Count;
            AddNote("NOT", true);
            Check(store.All.Count == before, "empty NOT rejected");
            ActiveControl = null;
            noteBox.Text = "serbest not";
            AddNote("NOT", true);
            var kinds = CurrentNotes().Select(n => n.Kind).ToList();
            Check(kinds.Count == 6, "6 notes in list: " + string.Join(",", kinds));
            Check(store.All.Where(n => n.Kind is "BIP" or "KES_BASLA" or "KES_BITIR").All(n => n.Shot == null), "no screenshot for BIP/KES");
            Check(tab == PanelTab.Game && gameList.Items.Count == 3 && editList.Items.Count == 2, "game notes and editing rows (bip + cut pair) in separate lists");
            Check(seek.Markers.Count == 3, "timeline shows the game tab's markers");
            ShowTab(PanelTab.Edit);
            Check(seek.Markers.Count == 3 && seek.Ranges.Count == 2 && editList.Columns.Count == 3, "timeline shows the Kurgu tab's markers, the cut and the bleep range");
            var cutRow = editList.Items.Cast<ListViewItem>().FirstOrDefault(i => i.SubItems[1].Text == "KES");
            Check(cutRow != null && cutRow.Text.Contains('–') && cutRow.SubItems[2].Text.StartsWith("2.0 sn"), "cut marks paired into one row: " + cutRow?.Text + " " + cutRow?.SubItems[2].Text);
            await Wait(300);
            CaptureWindow(Path.ChangeExtension(logPath, ".kurgu.png"));

            // Screenshot to the selected note, go to, delete last.
            var bip = editList.Items.Cast<ListViewItem>().First(i => ((Note)i.Tag).Kind == "BIP");
            bip.Selected = true;
            ShotToSelected();
            Check(((Note)bip.Tag).Shot == null, "no screenshot for a selected editing mark");
            GotoSelected();
            await Wait(800);
            Check(Math.Abs(player.TimeMs - 3000) < 500, "go to selected (-2 s) -> " + player.TimeMs);
            DeleteLast();
            Check(CurrentNotes().Count == 5 && CurrentNotes().All(n => n.Text != "serbest not"), "delete last removed the last added note");
            Undo();
            Check(CurrentNotes().Count(n => n.Text == "serbest not") == 1, "undo brings the deleted note back");
            Undo();
            Check(CurrentNotes().Count == 5 && CurrentNotes().All(n => n.Text != "serbest not"), "second undo removes the added note again");
            Redo();
            Check(CurrentNotes().Count == 6, "redo adds it back");
            Undo();

            // Status, severity, filter and search on the game list.
            ShowTab(PanelTab.Game);
            var bugNote = CurrentNotes().First(n => n.Kind == "BUG");
            SetStatusOf(bugNote, "DUZELTILDI");
            SetSeverityOf(bugNote, "KRITIK");
            filterBox.SelectedIndex = 4;   // open ones
            RefreshList();
            Check(gameList.Items.Count == 1 && filterInfo.Text == "1 / 2", "filter 'Açık olanlar' hides the fixed bug: " + filterInfo.Text);
            filterBox.SelectedIndex = 0;
            searchBox.Text = "alan";
            Check(gameList.Items.Count == 1 && ((Note)gameList.Items[0].Tag).Kind == "DEGISIKLIK", "search narrows the list");
            searchBox.Text = "";
            var bipNote = CurrentNotes().First(n => n.Kind == "BIP");
            bipNote.End = bipNote.T + 0.8;
            store.Save(bipNote.Video);

            // Frame step and rate.
            long t0 = player.TimeMs;
            player.StepFrame(1);
            await Wait(400);
            player.StepFrame(1);
            await Wait(400);
            Info($"next frame x2: {t0} -> {player.TimeMs}");
            Check(player.TimeMs > t0, "frame step forward");
            SetRate(5);
            Check(Math.Abs(Rates[rateIndex] - 1.5f) < 0.01, "rate 1.5x");
            SetRate(3);

            // Seek again after the end of the video.
            player.Seek(player.LengthMs - 1000);
            player.Play();
            await Wait(3000);
            Info("state at end: " + player.State);
            player.Seek(2000);
            await Wait(1500);
            Check(Math.Abs(player.TimeMs - 2000) < 600 && player.HasVideo, "seek after the end -> " + player.TimeMs + " state " + player.State);

            // Files.
            var perVideo = Directory.GetFiles(Paths.NotesDir, "*.txt");
            Check(perVideo.Length == 1, "per-video note file " + string.Join(", ", perVideo.Select(Path.GetFileName)));
            var combined = File.ReadAllLines(Paths.CombinedFile);
            Check(combined.Length == 6 && combined[0].StartsWith("# saniye"), "combined file has header + 5 notes");
            Info("combined line: " + combined[1]);
            var report = store.WriteReport(out var missing);
            var md = File.ReadAllText(report);
            Check(missing == 0 && md.Contains("### Buglar") && md.Contains("![") && md.IndexOf("### Buglar") < md.IndexOf("### Bip"), "report written, bugs first, screenshots linked");
            Check(File.Exists(Paths.HtmlReportFile), "HTML report written");
            Check(Whisper.SelfCheck(), "whisper JSON word parsing");

            // Reload from disk (per-video file wins over the combined file); new columns survive.
            var reloaded = new NoteStore();
            reloaded.Load();
            Check(reloaded.All.Count == 5, "reload gives 5 notes");
            var bugBack = reloaded.All.First(n => n.Kind == "BUG");
            var bipBack = reloaded.All.First(n => n.Kind == "BIP");
            Check(bugBack.Status == "DUZELTILDI" && bugBack.Severity == "KRITIK" && bipBack.End is double be && Math.Abs(be - bipBack.T - 0.8) < 0.02,
                "status, severity and BIP end saved");
            var lines = File.ReadAllLines(Directory.GetFiles(Paths.NotesDir, "*.txt")[0]);
            Check(lines.Any(l => l.Split('\t').Length == 6) && lines.Any(l => l.Split('\t').Length == 8), "plain notes keep six columns, extended ones grow");

            // Next video in the folder.
            StepVideo(1);
            await Wait(2500);
            Info("after next video: " + currentVideo + " state " + player.State);
            Check(currentVideo != Path.GetFileName(video) && player.HasVideo, "next video in folder plays");
            Check(gameList.Items.Count == 0 && editList.Items.Count == 0, "lists show only the open video's notes");
            player.Pause();
            await Wait(500);
            Check(player.TakeSnapshot(Path.Combine(Paths.ShotsDir, "ikinci_video.png"), out var err2), "screenshot of second video " + err2);

            TogglePanel();
            Check(split.Panel2Collapsed, "panel hides");
            ShowHelp();
            Check(!split.Panel2Collapsed && tab == PanelTab.Shortcuts && shortcutsPage.Visible && !gamePage.Visible && !editPage.Visible, "help opens panel on shortcuts tab");
            await Wait(300);
            CaptureWindow(Path.ChangeExtension(logPath, ".kisayollar.png"));
            ShowTab(PanelTab.Videos);
            await Wait(400);
            Check(videoList.Items.Count == 2, "Videolar tab lists the folder's videos: " + videoList.Items.Count);
            CaptureWindow(Path.ChangeExtension(logPath, ".videolar.png"));
            FocusNoteBox();
            Check(tab == PanelTab.Game && gamePage.Visible && noteBox.Focused, "typing switches to the game tab");
            ActiveControl = null;
            ToggleFullscreen();
            await Wait(800);
            Check(FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Maximized && !split.Panel2Collapsed, "fullscreen keeps panel");
            CaptureWindow(Path.ChangeExtension(logPath, ".tamekran.png"));
            ToggleFullscreen();
            await Wait(500);
            StepVideo(-1);
            await Wait(1500);
            player.Pause();
            gameList.Items[gameList.Items.Count - 1].Selected = true;
            await Wait(500);
            CaptureWindow(Path.ChangeExtension(logPath, ".pencere.png"));

            // Smallest window: the player bar and the side panel must fit without overlapping.
            var bounds = Bounds;
            Size = MinimumSize;
            await Wait(800);
            Info($"minimum window {Width}x{Height}, client {ClientSize.Width}x{ClientSize.Height}, panel {split.Panel2.Width}, list {gameList.ClientSize.Height}, toolbar {toolbar.Height}, tabs {tabStrip.Height}");
            var barItems = playerBar.Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.Left).ToList();
            Check(barItems.Count > 0 && barItems.All(c => c.Left >= 0 && c.Right <= playerBar.Width) && barItems.Zip(barItems.Skip(1)).All(p => p.First.Right <= p.Second.Left),
                "player bar fits at the minimum size: " + string.Join(" ", barItems.Select(c => c.Left + "-" + c.Right)));
            Check(split.Panel2.Width >= Px(340) && videoHost.Height > 100 && gameList.ClientSize.Height > Px(30), "side panel and video visible at the minimum size");
            CaptureWindow(Path.ChangeExtension(logPath, ".kucuk.png"));
            ShowTab(PanelTab.Edit);
            await Wait(300);
            Check(editList.Items.Count == 0 || editList.Items[0].Bounds.Width <= editList.ClientSize.Width, "Kurgu rows fit the narrow list");
            CaptureWindow(Path.ChangeExtension(logPath, ".kucuk-kurgu.png"));
            ShowTab(PanelTab.Game);
            Bounds = bounds;
            await Wait(600);
            Check(split.Panel2.Width > Px(340) || Width < Px(1200), "side panel width comes back after the window grows: " + split.Panel2.Width);

            // Dark dialog and context menu.
            using (var dlg = new EditNoteDialog(CurrentNotes().First(n => n.Kind == "BUG")) { StartPosition = FormStartPosition.Manual })
            {
                dlg.Show(this);
                dlg.Location = new Point(Left + (Width - dlg.Width) / 2, Top + (Height - dlg.Height) / 2);
                await Wait(500);
                CaptureHandle(dlg.Handle, Path.ChangeExtension(logPath, ".dialog.png"));
                dlg.Close();
            }
            gameList.Items[0].Selected = true;
            gameMenu.Show(gameList, new Point(Px(40), Px(10)));
            await Wait(500);
            CaptureHandle(gameMenu.Handle, Path.ChangeExtension(logPath, ".menu.png"));
            gameMenu.Close();

            // A list filled while its page is hidden and has no size yet (a start with notes) shows its rows from the top.
            ShowTab(PanelTab.Game);
            editPage.Size = Size.Empty;
            RefreshList();
            ShowTab(PanelTab.Edit);
            await Wait(400);
            Check(editList.Items.Count > 0 && editList.Items[0].Bounds.Y == 0, "list filled while hidden starts at the top: " + editList.Items.Cast<ListViewItem>().FirstOrDefault()?.Bounds);
            Check(editList.Items[0].Bounds.Width == editList.ClientSize.Width, $"rows fill the list width: {editList.Items[0].Bounds.Width} / {editList.ClientSize.Width}");
        }
        catch (Exception ex)
        {
            failures++;
            log.AppendLine("EXCEPTION " + ex);
        }
        log.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        File.WriteAllText(logPath, log.ToString());
        Close();
    }

    void CaptureWindow(string path) => CaptureHandle(Handle, path);

    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rectangle rect, int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out Rectangle rect);

    // The whole window with its title bar, drawn by the window itself (PW_RENDERFULLCONTENT also gets the video), so windows on top
    // of it do not matter; the visible frame bounds are cut out of the window rectangle.
    static void CaptureHandle(IntPtr hwnd, string path)
    {
        GetWindowRect(hwnd, out var w);   // left, top, right, bottom
        var window = Rectangle.FromLTRB(w.Left, w.Top, w.Width, w.Height);
        var frame = DwmGetWindowAttribute(hwnd, 9, out var f, 16) == 0 ? Rectangle.FromLTRB(f.Left, f.Top, f.Width, f.Height) : window;
        using var bmp = new Bitmap(window.Width, window.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            bool ok = PrintWindow(hwnd, hdc, 2);
            g.ReleaseHdc(hdc);
            if (!ok) g.CopyFromScreen(window.Location, Point.Empty, window.Size);
        }
        var crop = new Rectangle(frame.X - window.X, frame.Y - window.Y, frame.Width, frame.Height);
        crop.Intersect(new Rectangle(Point.Empty, bmp.Size));
        using var cut = bmp.Clone(crop, bmp.PixelFormat);
        cut.Save(path);
    }
}
