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

    // "--profile-area <log> <video>": times the area selection on a real recording with simulated mouse events.
    public async Task ProfileArea(string logPath, string video)
    {
        var log = new StringBuilder();
        var sw = new System.Diagnostics.Stopwatch();
        void Step(string what) { log.AppendLine($"{sw.ElapsedMilliseconds,6} ms  {what}"); sw.Restart(); }
        try
        {
            await Task.Delay(500);
            OpenVideo(video);
            await Task.Delay(2500);
            player.Seek(50000);
            await Task.Delay(1000);
            sw.Start();
            var size = videoPanel.ClientSize;
            var a = new Point(size.Width / 4, size.Height / 4);
            VideoMouseDown(videoPanel, new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            Step("mouse down");
            VideoMouseMove(videoPanel, new MouseEventArgs(MouseButtons.Left, 0, a.X + 20, a.Y + 20, 0));
            Step($"first move (freeze frame {overlay.Frame?.Width}x{overlay.Frame?.Height}, region {RegionActive})");
            Update();
            Step("first paint");
            long movesMs = 0, max = 0;
            for (int i = 0; i < 60; i++)
            {
                var t0 = sw.ElapsedMilliseconds;
                VideoMouseMove(videoPanel, new MouseEventArgs(MouseButtons.Left, 0, a.X + 20 + i * 8, a.Y + 20 + i * 5, 0));
                overlay.Update();
                var d = sw.ElapsedMilliseconds - t0;
                movesMs += d;
                max = Math.Max(max, d);
            }
            Step($"60 moves with paint: avg {movesMs / 60.0:0.0} ms, max {max} ms");
            VideoMouseUp(videoPanel, new MouseEventArgs(MouseButtons.Left, 1, a.X + 500, a.Y + 300, 0));
            Step("mouse up (focus note box, badges)");
            for (int i = 0; i < 10; i++) { UpdatePlayerUi(); Application.DoEvents(); }
            Step("10 ui ticks while selected");
            noteBox.Text = "profil";
            AddNote("DEGISIKLIK", false);
            Step("add note with marked screenshot");
            Application.DoEvents();
            Step("pending messages");
        }
        catch (Exception ex) { log.AppendLine("EXCEPTION " + ex); }
        File.WriteAllText(logPath, log.ToString());
        Close();
    }

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
            Check(bug?.Shot != null && File.Exists(Path.Combine(Paths.ShotsDir(currentVideo), bug.Shot)), "BUG screenshot " + bug?.Shot);
            if (bug?.Shot != null)
                using (var img = Image.FromFile(Path.Combine(Paths.ShotsDir(currentVideo), bug.Shot)))
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
                using (var img = new Bitmap(Path.Combine(Paths.ShotsDir(currentVideo), change.Shot)))
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

            // Files: KareNot\<video>\notlar.txt, ss\ and the video's report.
            var videoDir = Path.Combine(Paths.Root, Path.GetFileNameWithoutExtension(currentVideo));
            var noteFiles = Directory.GetDirectories(Paths.Root).Select(d => Path.Combine(d, "notlar.txt")).Where(File.Exists).ToList();
            Check(noteFiles.Count == 1 && noteFiles[0] == Path.Combine(videoDir, "notlar.txt") && Paths.NotesFile(currentVideo) == noteFiles[0],
                "per-video note file " + string.Join(", ", noteFiles));
            var shots = CurrentNotes().Where(n => n.Shot != null).ToList();
            Check(Paths.ShotsDir(currentVideo) == Path.Combine(videoDir, "ss") && shots.Count >= 2 && shots.All(n => File.Exists(Path.Combine(videoDir, "ss", n.Shot))),
                $"{shots.Count} screenshots in the video's ss folder");
            var videoLines = File.ReadAllLines(noteFiles[0]);
            Check(videoLines.Length == 6 && videoLines[0].StartsWith("# saniye"), "note file has header + 5 notes");
            Info("note line: " + videoLines[1]);
            Check(Paths.OldCombinedFiles.All(f => !File.Exists(f)), "no combined file of all notes is written");
            var report = store.WriteReport(currentVideo, out var missing);
            var md = File.ReadAllText(report);
            Check(report == Path.Combine(videoDir, "rapor.md") && missing == 0 && md.Contains("### Buglar") && md.Contains("](ss/")
                && md.IndexOf("### Buglar") < md.IndexOf("### Bip"), "report written into the video's folder, bugs first, screenshots linked");
            Check(File.Exists(Path.Combine(videoDir, "rapor.html")), "HTML report written");
            var allReport = File.ReadAllText(store.WriteReport(null, out _));
            Check(File.Exists(Path.Combine(Paths.Root, "rapor.html")) && allReport.Contains("](" + Uri.EscapeDataString(Path.GetFileName(videoDir)) + "/ss/"),
                "report of all videos in the root links into the video folders");
            Check(Whisper.SelfCheck(), "whisper JSON word parsing");

            // Reload from disk; new columns survive. An old combined file adds only the videos missing elsewhere and is retired.
            File.WriteAllText(Paths.OldCombinedFiles[0], "# saniye\tzaman\ttur\tnot\tvideo\tss\n12.00\t00:00:12.0\tBUG\teski not\teski_video.mp4\t\n" +
                $"3.00\t00:00:03.0\tBUG\tçift\t{currentVideo}\t\n");
            Check(!Migration.Run().Failed, "migration of the old combined file");
            var reloaded = new NoteStore();
            reloaded.Load();
            Check(reloaded.All.Count(n => n.Video == currentVideo) == 5 && reloaded.All.Count(n => n.Video == "eski_video.mp4") == 1,
                "reload gives 5 notes; the old combined file only adds the missing video");
            Check(!File.Exists(Paths.OldCombinedFiles[0]) && File.Exists(Path.Combine(Paths.Root, "video_notlari_eski.txt"))
                && File.Exists(Paths.NotesFile("eski_video.mp4")), "old combined file retired into the KareNot folder");
            var bugBack = reloaded.All.First(n => n.Video == currentVideo && n.Kind == "BUG");
            var bipBack = reloaded.All.First(n => n.Video == currentVideo && n.Kind == "BIP");
            Check(bugBack.Status == "DUZELTILDI" && bugBack.Severity == "KRITIK" && bipBack.End is double be && Math.Abs(be - bipBack.T - 0.8) < 0.02,
                "status, severity and BIP end saved");
            var lines = File.ReadAllLines(noteFiles[0]);
            Check(lines.Any(l => l.Split('\t').Length == 6) && lines.Any(l => l.Split('\t').Length == 8), "plain notes keep six columns, extended ones grow");

            // A video with the same name and another extension shares the folder; saving one keeps the other's notes.
            var twin = store.Add(1, "NOT", "ikiz", Path.ChangeExtension(currentVideo, ".mkv"), null);
            Check(File.ReadAllLines(noteFiles[0]).Length == 7 && Paths.VideoDir(twin.Video) == videoDir, "same-named video shares the folder's notlar.txt");
            store.Save(currentVideo);
            Check(File.ReadAllLines(noteFiles[0]).Count(l => l.Contains("\tikiz\t")) == 1, "saving one video keeps the other's notes");
            store.Remove(twin);
            Check(File.ReadAllLines(noteFiles[0]).Length == 6, "removing it leaves the open video's notes");

            MigrationTest(Check, Info);

            // Next video in the folder.
            StepVideo(1);
            await Wait(2500);
            Info("after next video: " + currentVideo + " state " + player.State);
            Check(currentVideo != Path.GetFileName(video) && player.HasVideo, "next video in folder plays");
            Check(gameList.Items.Count == 0 && editList.Items.Count == 0, "lists show only the open video's notes");
            player.Pause();
            await Wait(500);
            Directory.CreateDirectory(Paths.ShotsDir(currentVideo));
            Check(player.TakeSnapshot(Path.Combine(Paths.ShotsDir(currentVideo), "ikinci_video.png"), out var err2), "screenshot of second video " + err2);

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

    // Moving an old layout (Video Notlari\notlar, shared ss\, combined file) in a separate data root; synchronous, so nothing
    // else runs while VIDEONOTLARI_DATA points there.
    static void MigrationTest(Action<bool, string> Check, Action<string> Info)
    {
        var data = Environment.GetEnvironmentVariable("VIDEONOTLARI_DATA");
        Environment.SetEnvironmentVariable("VIDEONOTLARI_DATA", Path.Combine(Paths.Desktop, "tasima_testi"));
        try
        {
            var old = Paths.OldDir;
            Directory.CreateDirectory(Path.Combine(old, "notlar"));
            Directory.CreateDirectory(Path.Combine(old, "ss"));
            const string header = "# saniye\tzaman\ttur\tnot\tvideo\tss\n";
            // The BUG screenshot is an old VLC prefix; the NOT has a clip.
            File.WriteAllText(Path.Combine(old, "notlar", "oyun.mp4.txt"), header + "5.00\t00:00:05.0\tBUG\tduvar\toyun.mp4\tbug_eski\n" +
                "9.00\t00:00:09.0\tNOT\tklipli\toyun.mp4\t\t\t\t\tnot_klip.mp4\n");
            File.WriteAllBytes(Path.Combine(old, "ss", "bug_eski_20250101.png"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(old, "ss", "not_klip.mp4"), new byte[] { 4, 5 });
            File.WriteAllText(Path.Combine(old, "inceleme.json"), "{\"oyun.mp4\": {\"pos\": 7, \"build\": \"b12\"}}");
            File.WriteAllText(Path.Combine(old, "hazir_notlar.txt"), "Eski metin\n");
            File.WriteAllText(Paths.OldCombinedFiles[0], header + "5.00\t00:00:05.0\tBUG\tduvar\toyun.mp4\tbug_eski\n12.00\t00:00:12.0\tNOT\tbaska\tbaska.mkv\t\n");
            // Already in the new layout: review entries and another file with the screenshot's name.
            Directory.CreateDirectory(Paths.ShotsDir("oyun.mp4"));
            File.WriteAllBytes(Path.Combine(Paths.ShotsDir("oyun.mp4"), "bug_eski_20250101.png"), new byte[] { 9 });
            File.WriteAllText(Paths.ReviewFile, "{\"oyun.mp4\": {\"pos\": 30}, \"yeni.mp4\": {\"done\": true}}");

            var result = Migration.Run();
            Info("migration: " + result.Message);
            var moved = NoteStore.ReadAll();
            Check(!result.Failed && moved.Count(n => n.Video == "oyun.mp4") == 2 && moved.Count(n => n.Video == "baska.mkv") == 1
                && File.Exists(Path.Combine(Paths.Root, "oyun", "notlar.txt")) && File.Exists(Path.Combine(Paths.Root, "baska", "notlar.txt")),
                "migration: notes in KareNot\\<video>\\notlar.txt, combined file only for the missing video");
            var bug = moved.FirstOrDefault(n => n.Kind == "BUG");
            var ss = Paths.ShotsDir("oyun.mp4");
            Check(bug?.Shot == "bug_eski_20250101_2.png" && NoteStore.ResolveShot(bug) == bug.Shot && new FileInfo(Path.Combine(ss, bug.Shot)).Length == 3
                && new FileInfo(Path.Combine(ss, "bug_eski_20250101.png")).Length == 1, "migration: screenshot moved into the video's ss, a taken name keeps both: " + bug?.Shot);
            Check(NoteStore.ResolveClip(moved.First(n => n.Kind == "NOT" && n.Video == "oyun.mp4")) == "not_klip.mp4", "migration: clip moved");
            var review = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Paths.ReviewFile));
            Check(review["oyun.mp4"]["pos"].GetValue<double>() == 30 && review["oyun.mp4"]["build"].GetValue<string>() == "b12" && review["yeni.mp4"] != null,
                "migration: inceleme.json merged");
            Check(File.Exists(TextLists.TemplatesFile) && File.Exists(Path.Combine(Paths.Root, "video_notlari_eski.txt")) && !File.Exists(Paths.OldCombinedFiles[0]),
                "migration: lists moved, combined file retired");
            Check(!Directory.Exists(old) && File.Exists(Path.Combine(old + " (eski)", "notlar", "oyun.mp4.txt")) && File.Exists(Path.Combine(old + " (eski)", "inceleme.json"))
                && !File.Exists(Path.Combine(old + " (eski)", "ss", "bug_eski_20250101.png")), "migration: old folder renamed to 'Video Notlari (eski)'");

            // The same old notes once more: nothing doubles, the next old folder becomes "(eski 2)".
            Directory.CreateDirectory(Path.Combine(old, "notlar"));
            File.Copy(Path.Combine(old + " (eski)", "notlar", "oyun.mp4.txt"), Path.Combine(old, "notlar", "oyun.mp4.txt"));
            var again = Migration.Run();
            Check(!again.Failed && NoteStore.ReadAll().Count == 3 && Directory.Exists(old + " (eski 2)"), "migration again: no duplicate notes; " + again.Message);
            Check(Migration.Run().Message == null, "migration: nothing to do without old data");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VIDEONOTLARI_DATA", data);
            ReviewStore.Reload();
        }
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
