using System.Runtime.InteropServices;
using System.Text;

namespace VideoNotlari;

// "--selftest <log> <video>": drives the real window and player through the main features and logs the results.
sealed partial class MainForm
{
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

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
            AddNote("BIP", false);
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
            Check(list.Items.Count == 6 && seek.Markers.Count == 6, "list rows and timeline markers");

            // Screenshot to the selected note, go to, delete last.
            var bip = list.Items.Cast<ListViewItem>().First(i => ((Note)i.Tag).Kind == "BIP");
            bip.Selected = true;
            ShotToSelected();
            Check(((Note)bip.Tag).Shot != null, "screenshot added to selected BIP");
            GotoSelected();
            await Wait(800);
            Check(Math.Abs(player.TimeMs - 3000) < 500, "go to selected (-2 s) -> " + player.TimeMs);
            DeleteLast();
            Check(CurrentNotes().Count == 5 && CurrentNotes().All(n => n.Text != "serbest not"), "delete last removed the last added note");

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

            // Reload from disk (per-video file wins over the combined file).
            var reloaded = new NoteStore();
            reloaded.Load();
            Check(reloaded.All.Count == 5, "reload gives 5 notes");

            // Next video in the folder.
            StepVideo(1);
            await Wait(2500);
            Info("after next video: " + currentVideo + " state " + player.State);
            Check(currentVideo != Path.GetFileName(video) && player.HasVideo, "next video in folder plays");
            Check(list.Items.Count == 0, "list shows only the open video's notes");
            player.Pause();
            await Wait(500);
            Check(player.TakeSnapshot(Path.Combine(Paths.ShotsDir, "ikinci_video.png"), out var err2), "screenshot of second video " + err2);

            TogglePanel();
            Check(split.Panel2Collapsed, "panel hides");
            TogglePanel();
            ToggleFullscreen();
            await Wait(800);
            Check(FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Maximized && !split.Panel2Collapsed, "fullscreen keeps panel");
            CaptureWindow(Path.ChangeExtension(logPath, ".tamekran.png"));
            ToggleFullscreen();
            await Wait(500);
            StepVideo(-1);
            await Wait(1500);
            player.Pause();
            await Wait(500);
            CaptureWindow(Path.ChangeExtension(logPath, ".pencere.png"));
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

    void CaptureWindow(string path)
    {
        var r = RectangleToScreen(ClientRectangle);
        using var bmp = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        bmp.Save(path);
    }
}
