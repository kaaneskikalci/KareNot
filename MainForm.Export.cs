using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace KareNot;

// Exports (CSV, Resolve markers, task list, editing notes, findings ZIP, edited video), bug clips, settings and text lists.
sealed partial class MainForm
{
    static readonly UTF8Encoding Utf8NoBom = new(false);
    CancellationTokenSource exportCancel;
    ToolStripMenuItem exportCancelItem, openClipItem;

    partial void RegisterExport()
    {
        exportMenu.ShowItemToolTips = true;
        exportMenu.Items.Add("Tüm videoların raporu", null, (s, e) => SaveReportOf(null)).ToolTipText =
            "Bütün videoların notları tek raporda: Masaüstü\\KareNot\\rapor.md ve rapor.html.";
        exportMenu.Items.Add("Notları CSV olarak kaydet", null, (s, e) => ExportCsv()).ToolTipText =
            "Tüm videoların notları; Excel için ; ayraçlı, UTF-8. Masaüstü\\KareNot\\cikti klasörüne yazılır.";
        exportMenu.Items.Add("Görev listesi (agent / ekip için)", null, (s, e) => ExportTaskList()).ToolTipText =
            "Açık oyun testi notları, her biri ayrı görev (Markdown): video, zaman, önem, SS / klip, Beklenen / Gözlenen / Kabul ölçütü.";
        exportMenu.Items.Add("Kurgu notları (JSON, editör/AI için)", null, (s, e) => _ = ExportEditNotes()).ToolTipText =
            "Açık videonun bip, kesim ve notları: makine için JSON, editör için Markdown.";
        exportMenu.Items.Add("DaVinci Resolve işaretleri (EDL)", null, (s, e) => _ = ExportResolveEdl()).ToolTipText =
            "Açık videonun notları renkli işaret olarak. Resolve'da videoyu 01:00:00:00'dan başlayan zaman çizelgesine koy, sonra\n" +
            "Timeline > Import > Timeline Markers from EDL… ile bu dosyayı seç.";
        exportMenu.Items.Add(new ToolStripSeparator());
        exportMenu.Items.Add(new ToolStripMenuItem("Bulgu paketi (ZIP)…", null, (s, e) => ShowPackageDialog()) { ShortcutKeyDisplayString = "Ctrl+P" });
        exportMenu.Items[^1].ToolTipText =
            "Rapor (md + html), CSV, görev listesi, kurgu notları ve SS / klipler tek ZIP'te.";
        exportMenu.Items.Add("Bipli ve kesilmiş video oluştur", null, (s, e) => _ = ExportEditedVideo()).ToolTipText =
            "Açık videonun BİP aralıkları bip sesiyle örtülür, eşli kesimler çıkarılır (ffmpeg).";
        exportCancelItem = new ToolStripMenuItem("İptal (süren dışa aktarma)", null, (s, e) => exportCancel?.Cancel()) { Visible = false };
        exportMenu.Items.Add(exportCancelItem);

        gameMenu.Items.Add(new ToolStripSeparator());
        gameMenu.Items.Add(Item("Klip çıkar", "C", ClipSelected));
        openClipItem = new ToolStripMenuItem("Klibi aç", null, (s, e) => OpenSelectedClip());
        gameMenu.Items.Add(openClipItem);
        gameMenu.Items.Add("Görev metni olarak kopyala", null, (s, e) => CopyTask());
        gameMenu.Opening += (s, e) => openClipItem.Visible = SelectedNote?.Clip != null;

        toolsMenu.Items.Add("Ayarlar…", null, (s, e) => ShowSettings());
        toolsMenu.Items.Add("Hazır not metinlerini düzenle", null, (s, e) => OpenTextList(TextLists.TemplatesFile, () => TextLists.Templates()));
        toolsMenu.Items.Add("Küfür listesini düzenle", null, (s, e) => OpenTextList(TextLists.ProfanityFile, () => TextLists.Profanity()));

        featureChars['c'] = ClipSelected;
        featureKeys[Keys.Control | Keys.P] = ShowPackageDialog;
    }

    // ---------- Helpers ----------

    // One export job at a time; errors go to the status line, real failures also to a message box.
    async Task RunExportJob(string name, Func<CancellationToken, Task> work)
    {
        if (exportCancel != null)
        {
            SetStatus("Önce süren işin bitmesini bekle ya da iptal et (Dışa aktar ▾ > İptal).");
            return;
        }
        var cts = exportCancel = new CancellationTokenSource();
        exportCancelItem.Visible = true;
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus(name + " iptal edildi.");
        }
        catch (Exception ex)
        {
            SetStatus("HATA: " + name + ": " + ex.Message);
            if (!IsDisposed) MessageBox.Show(this, name + " başarısız:\n\n" + ex.Message, "KareNot", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            exportCancel = null;
            cts.Dispose();
            exportCancelItem.Visible = false;
        }
    }

    void Ui(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a);
        else a();
    }

    bool NeedFfmpeg()
    {
        if (Ffmpeg.Find() != null) return true;
        SetStatus(Ffmpeg.Missing);
        return false;
    }

    static string FfmpegError(string log)
    {
        var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("frame=") && !l.StartsWith("size=")).ToList();
        return "ffmpeg: " + string.Join(" / ", lines.Skip(Math.Max(0, lines.Count - 2)));
    }

    static string SafeName(string video) => Paths.FolderName(video);

    static void Reveal(string path)
    {
        try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
    }

    static string Stamp => DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);

    // Into the video's cikti\, or KareNot\cikti for several videos (video null).
    string WriteExport(string video, string name, string text, Encoding encoding = null)
    {
        Directory.CreateDirectory(Paths.ExportDir(video));
        var path = Path.Combine(Paths.ExportDir(video), name);
        File.WriteAllText(path, text, encoding ?? Utf8NoBom);
        return path;
    }

    // ---------- CSV, task list ----------

    void ExportCsv()
    {
        if (store.All.Count == 0) { SetStatus("Kaydedilecek not yok."); return; }
        try
        {
            var path = WriteExport(null, $"KareNot_notlar_{Stamp}.csv", Reports.Csv(store.All), Reports.CsvEncoding);
            SetStatus($"CSV kaydedildi ({store.All.Count} not): {path}");
            Reveal(path);
        }
        catch (Exception ex) { SetStatus("HATA: CSV yazılamadı: " + ex.Message); }
    }

    void ExportTaskList()
    {
        var source = currentVideo != null ? CurrentNotes() : store.All.ToList();
        var notes = source.Where(n => !Kinds.IsEdit(n.Kind) && Reports.IsOpen(n)).ToList();
        if (notes.Count == 0) { SetStatus(currentVideo != null ? "Bu videoda açık oyun testi notu yok." : "Açık oyun testi notu yok."); return; }
        try
        {
            var path = WriteExport(currentVideo, $"agent_gorevleri_{(currentVideo != null ? SafeName(currentVideo) : "tum")}.md",
                Reports.TaskList(notes, currentVideo ?? "tüm videolar", currentVideo != null ? _ => "../ss/" : v => "../" + Reports.FolderMedia(v)));
            SetStatus($"Görev listesi kaydedildi ({notes.Count} görev): {path}");
            Reveal(path);
        }
        catch (Exception ex) { SetStatus("HATA: görev listesi yazılamadı: " + ex.Message); }
    }

    void CopyTask()
    {
        var n = SelectedNote;
        if (n == null || Kinds.IsEdit(n.Kind)) { SetStatus("Önce oyun testi listesinden bir not seç."); return; }
        try
        {
            Clipboard.SetText(Reports.TaskText(n, null));
            SetStatus("Görev metni panoya kopyalandı: " + NoteStore.Fmt(n.T) + " " + Kinds.Label(n.Kind));
        }
        catch (Exception ex) { SetStatus("HATA: panoya kopyalanamadı: " + ex.Message); }
    }

    // ---------- Editing notes, Resolve markers ----------

    // Video path, length and frame rate for the editing notes; ffmpeg is optional here.
    async Task<Reports.EditVideo> EditVideoInfo(string video, IReadOnlyList<Note> notes)
    {
        string path = null;
        double? length = null, fps = null;
        if (player?.FilePath != null)
        {
            var candidate = video == currentVideo ? player.FilePath : Path.Combine(Path.GetDirectoryName(player.FilePath), video);
            if (File.Exists(candidate)) path = candidate;
            if (video == currentVideo && player.LengthMs > 0) length = player.LengthMs / 1000.0;
        }
        if (path != null && Ffmpeg.Find() != null)
        {
            try
            {
                fps = await Ffmpeg.FrameRate(path);
                length ??= await Ffmpeg.Duration(path);
            }
            catch { }
        }
        return new Reports.EditVideo(video, path, length, fps, notes, settings.BeepDefault);
    }

    Task ExportEditNotes()
    {
        if (currentVideo == null) { SetStatus("Önce bir video aç."); return Task.CompletedTask; }
        var notes = CurrentNotes();
        if (!notes.Any(n => Kinds.IsEdit(n.Kind))) { SetStatus("Bu videoda kurgu işareti (BİP / kesim) yok."); return Task.CompletedTask; }
        var video = currentVideo;
        return RunExportJob("Kurgu notları", async ct =>
        {
            SetStatus("Kurgu notları hazırlanıyor…");
            var info = await EditVideoInfo(video, notes);
            ct.ThrowIfCancellationRequested();
            var name = "kurgu_notlari_" + SafeName(video);
            var json = WriteExport(video, name + ".json", Reports.EditJson(info));
            WriteExport(video, name + ".md", Reports.EditMarkdown(info));
            SetStatus("Kurgu notları kaydedildi: " + Path.ChangeExtension(json, null) + " (.json + .md)");
            Reveal(json);
        });
    }

    Task ExportResolveEdl()
    {
        if (player?.FilePath == null) { SetStatus("Önce bir video aç."); return Task.CompletedTask; }
        var notes = CurrentNotes();
        if (notes.Count == 0) { SetStatus("Bu videoda not yok."); return Task.CompletedTask; }
        if (!NeedFfmpeg()) return Task.CompletedTask;
        var path = player.FilePath;
        var video = currentVideo;
        return RunExportJob("Resolve işaretleri", async ct =>
        {
            SetStatus("Kare hızı okunuyor…");
            var fps = await Ffmpeg.FrameRate(path);
            ct.ThrowIfCancellationRequested();
            var file = WriteExport(video, SafeName(video) + "_isaretler.edl", Reports.ResolveEdl(notes, fps, Path.GetFileNameWithoutExtension(video)));
            SetStatus($"Resolve işaretleri kaydedildi ({notes.Count} not, {fps.ToString("0.##", CultureInfo.InvariantCulture)} fps): {file} — " +
                "Resolve: Timeline > Import > Timeline Markers from EDL");
            Reveal(file);
        });
    }

    // ---------- Bug clip ----------

    void ClipSelected()
    {
        var n = SelectedNote;
        if (n == null || Kinds.IsEdit(n.Kind)) { SetStatus("Klip için önce oyun testi listesinden bir not seç."); return; }
        if (player?.FilePath == null || n.Video != currentVideo) { SetStatus("Önce notun videosunu aç."); return; }
        if (!NeedFfmpeg()) return;
        var path = player.FilePath;
        var name = string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyyMMdd_HHmmss}_{2}s_klip.mp4", n.Kind.ToLowerInvariant(), DateTime.Now, (int)n.T);
        var output = Path.Combine(Paths.ShotsDir(n.Video), name);
        _ = RunExportJob("Klip", async ct =>
        {
            Directory.CreateDirectory(Paths.ShotsDir(n.Video));
            SetStatus($"Klip çıkarılıyor: {NoteStore.Fmt(n.T)} (−{settings.ClipBefore:0.#} / +{settings.ClipAfter:0.#} sn)…");
            try
            {
                var (exit, log) = await Ffmpeg.Clip(path, n.T - settings.ClipBefore, settings.ClipBefore + settings.ClipAfter, output, ct);
                if (exit != 0) throw new InvalidOperationException(FfmpegError(log));
            }
            catch
            {
                try { File.Delete(output); } catch { }
                throw;
            }
            if (!store.All.Contains(n))
            {
                SetStatus("Klip kaydedildi ama not artık yok: " + output);
                return;
            }
            if (n.Video == currentVideo) RecordUndo("Klip");
            n.Clip = name;
            Saved(() => store.Save(n.Video));
            if (n.Video == currentVideo) RefreshList(n);
            SetStatus("Klip kaydedildi: " + name);
        });
    }

    void OpenSelectedClip()
    {
        var n = SelectedNote;
        var file = n == null || NoteStore.ResolveClip(n) == null ? null : Path.Combine(Paths.ShotsDir(n.Video), n.Clip);
        if (file == null) { SetStatus("Seçili notun klibi yok."); return; }
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus("HATA: klip açılamadı: " + ex.Message); }
    }

    // ---------- Findings package ----------

    void ShowPackageDialog()
    {
        if (store.All.Count == 0) { SetStatus("Pakete girecek not yok."); return; }
        using var dlg = new PackageDialog(currentVideo);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var notes = (dlg.AllVideos || currentVideo == null ? store.All : store.All.Where(n => n.Video == currentVideo))
            .Where(n => !dlg.GameOnly || !Kinds.IsEdit(n.Kind))
            .Where(n => !dlg.OpenOnly || Reports.IsOpen(n))
            .ToList();
        if (notes.Count == 0) { SetStatus("Seçilen süzgeçle pakete girecek not yok."); return; }
        var scopeVideo = dlg.AllVideos ? null : currentVideo;
        var zip = Path.Combine(Paths.ExportDir(scopeVideo), $"KareNot_{(scopeVideo == null ? "tum" : SafeName(scopeVideo))}_{Stamp}.zip");
        _ = RunExportJob("Bulgu paketi", ct => MakePackage(notes, zip, dlg.IncludeMedia, ct));
    }

    async Task MakePackage(List<Note> notes, string zipPath, bool includeMedia, CancellationToken ct)
    {
        SetStatus("Bulgu paketi hazırlanıyor…");
        // Media go to ss/ in the ZIP, into one sub-folder per video folder when there are several.
        bool several = notes.Select(n => Paths.FolderName(n.Video)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        string Sub(string video) => several ? Paths.FolderName(video) + "/" : "";
        Func<string, string> media = !includeMedia ? null : several ? v => "ss/" + Uri.EscapeDataString(Paths.FolderName(v)) + "/" : _ => "ss/";
        // Without media the reports must not link files that are not in the ZIP.
        var reportNotes = includeMedia ? notes : notes.Select(n => { var c = n.Clone(); c.Shot = null; c.Clip = null; return c; }).ToList();
        var texts = new List<(string Name, string Text, Encoding Encoding)>
        {
            ("rapor.md", Reports.Markdown(reportNotes, out _, media), Utf8NoBom),
            ("rapor.html", Reports.Html(reportNotes, media), Utf8NoBom),
            ("notlar.csv", Reports.Csv(notes), Reports.CsvEncoding),
        };
        var game = notes.Where(n => !Kinds.IsEdit(n.Kind)).ToList();
        if (game.Count > 0)
        {
            var videos = game.Select(n => n.Video).Distinct().ToList();
            texts.Add(("agent_gorevleri.md", Reports.TaskList(game, videos.Count == 1 ? videos[0] : "tüm videolar", media), Utf8NoBom));
        }
        foreach (var group in notes.GroupBy(n => n.Video).Where(g => g.Any(n => Kinds.IsEdit(n.Kind))))
        {
            var info = await EditVideoInfo(group.Key, group.OrderBy(n => n.T).ToList());
            ct.ThrowIfCancellationRequested();
            var name = "kurgu_notlari_" + SafeName(group.Key);
            texts.Add((name + ".json", Reports.EditJson(info), Utf8NoBom));
            texts.Add((name + ".md", Reports.EditMarkdown(info), Utf8NoBom));
        }
        var files = !includeMedia ? new List<(string Path, string Entry)>() : notes
            .SelectMany(n => new[] { NoteStore.ResolveShot(n), NoteStore.ResolveClip(n) }.Where(f => f != null)
                .Select(f => (Path: Path.Combine(Paths.ShotsDir(n.Video), f), Entry: "ss/" + Sub(n.Video) + f)))
            .DistinctBy(f => f.Entry, StringComparer.OrdinalIgnoreCase).ToList();

        var progress = new Progress<int>(i => SetStatus($"Bulgu paketi: {i} / {files.Count} dosya…"));
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                foreach (var (name, text, encoding) in texts)
                {
                    using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                    var preamble = encoding.GetPreamble();
                    stream.Write(preamble);
                    stream.Write(encoding.GetBytes(text));
                }
                int done = 0;
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    archive.CreateEntryFromFile(f.Path, f.Entry, CompressionLevel.NoCompression);
                    ((IProgress<int>)progress).Report(++done);
                }
            }, ct);
        }
        catch
        {
            try { File.Delete(zipPath); } catch { }
            throw;
        }
        SetStatus($"Bulgu paketi hazır ({notes.Count} not, {files.Count} dosya): {zipPath}");
        Reveal(zipPath);
    }

    // ---------- Edited video ----------

    Task ExportEditedVideo()
    {
        if (player?.FilePath == null) { SetStatus("Önce bir video aç."); return Task.CompletedTask; }
        var notes = CurrentNotes();
        var beeps = Kinds.BeepRanges(notes, settings.BeepDefault);
        var cuts = Kinds.CutRanges(notes);
        int unpaired = Kinds.Cuts(notes).Count(c => c.Start == null || c.End == null || c.End.T <= c.Start.T);
        if (beeps.Count == 0 && cuts.Count == 0)
        {
            SetStatus(unpaired > 0 ? "Eşli kesim yok: her Kesim BAŞLA'nın ardından bir Kesim BİTİR olmalı." : "Bu videoda BİP ya da kesim işareti yok.");
            return Task.CompletedTask;
        }
        if (!NeedFfmpeg()) return Task.CompletedTask;
        var path = player.FilePath;
        double length = player.LengthMs / 1000.0;
        var outputDir = Paths.ExportDir(currentVideo);
        var output = Path.Combine(outputDir, SafeName(currentVideo) + "_kurgu.mp4");
        return RunExportJob("Kurgu videosu", async ct =>
        {
            SetStatus("Kurgu videosu hazırlanıyor…");
            if (length <= 0) length = await Ffmpeg.Duration(path) ?? 0;
            var hasAudio = await Ffmpeg.HasAudio(path);
            double outLength = Ffmpeg.KeptParts(cuts, length).Sum(k => (k.End ?? length) - k.Start);
            Directory.CreateDirectory(outputDir);
            var extra = (hasAudio ? "" : ", videoda ses yok") + (unpaired > 0 ? $", {unpaired} eşsiz kesim işareti atlandı" : "");
            try
            {
                var (exit, log) = await Ffmpeg.ApplyEdits2(path, hasAudio, beeps, cuts, length, output,
                    t => Ui(() => SetStatus($"Kurgu videosu: %{(outLength > 0 ? Math.Clamp(t / outLength * 100, 0, 100) : 0):0} — " +
                        $"{beeps.Count} bip, {cuts.Count} kesim{extra} (Dışa aktar ▾ > İptal)")), ct);
                if (exit != 0) throw new InvalidOperationException(FfmpegError(log));
            }
            catch
            {
                try { File.Delete(output); } catch { }
                throw;
            }
            SetStatus($"Kurgu videosu hazır ({beeps.Count} bip, {cuts.Count} kesim, {Reports.Duration(outLength)}{extra}): {output}");
            Reveal(output);
        });
    }

    // ---------- Settings, text lists ----------

    void ShowSettings()
    {
        using var dlg = new SettingsDialog(settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        settings.Save();
        Ffmpeg.PathOverride = settings.FfmpegPath;
        SetStatus("Ayarlar kaydedildi." + (Ffmpeg.Find() == null ? " " + Ffmpeg.Missing : ""));
    }

    void OpenTextList(string file, Action ensure)
    {
        ensure();
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            SetStatus("Dosya açıldı; kaydedince geçerli olur: " + file);
        }
        catch (Exception ex) { SetStatus("HATA: dosya açılamadı: " + ex.Message); }
    }
}
