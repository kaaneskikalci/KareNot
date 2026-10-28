using System.Globalization;

namespace KareNot;

// Analysis tools (Araçlar menu): profanity candidates with whisper.cpp and dead air (silent / frozen) cut candidates
// with ffmpeg. One job at a time for the open video; switching videos or closing cancels it.
sealed partial class MainForm
{
    CancellationTokenSource analysisCancel;
    readonly List<ToolStripMenuItem> analysisItems = new();
    ToolStripMenuItem cancelAnalysisItem;

    partial void RegisterAnalysis()
    {
        if (toolsMenu.Items.Count > 0) toolsMenu.Items.Add(new ToolStripSeparator());
        analysisItems.Add(new ToolStripMenuItem("Küfür adaylarını bul (Whisper)", null, (s, e) => FindProfanity()));
        analysisItems.Add(new ToolStripMenuItem("Ölü anları bul (sessiz / donuk)", null, (s, e) => FindDeadAir()));
        cancelAnalysisItem = new ToolStripMenuItem("Analizi iptal et", null, (s, e) => analysisCancel?.Cancel()) { Enabled = false };
        foreach (var item in analysisItems) toolsMenu.Items.Add(item);
        toolsMenu.Items.Add(cancelAnalysisItem);
        VideoClosing += () => analysisCancel?.Cancel();
    }

    // Runs job(video, path, length seconds, status, cancel); its result is the final status line.
    async void RunAnalysis(string name, Func<string, string, double, IProgress<string>, CancellationToken, Task<string>> job)
    {
        if (analysisCancel != null)
        {
            SetStatus("Bir analiz zaten çalışıyor (Araçlar > Analizi iptal et).");
            return;
        }
        if (player == null || !player.HasMedia || currentVideo == null || player.FilePath == null)
        {
            SetStatus("Önce bir video aç.");
            return;
        }
        if (Ffmpeg.Find() == null)
        {
            SetStatus(Ffmpeg.Missing);
            return;
        }
        var cts = analysisCancel = new CancellationTokenSource();
        var status = new Progress<string>(s => { if (analysisCancel == cts && !IsDisposed) SetStatus(s); });
        SetAnalysisRunning(true);
        SetStatus(name + " başladı…");
        string result;
        try { result = await job(currentVideo, player.FilePath, player.LengthMs / 1000.0, status, cts.Token); }
        catch (OperationCanceledException) { result = name + " iptal edildi."; }
        catch (Exception ex) { result = name + " yapılamadı: " + ex.Message; }
        analysisCancel = null;
        cts.Dispose();
        if (IsDisposed) return;
        SetAnalysisRunning(false);
        SetStatus(result);
    }

    void SetAnalysisRunning(bool running)
    {
        foreach (var item in analysisItems) item.Enabled = !running;
        cancelAnalysisItem.Enabled = running;
    }

    static string Percent(string label, double seconds, double length) =>
        length > 0 ? $"{label} %{Math.Clamp((int)(100 * seconds / length), 0, 100)} (iptal: Araçlar > Analizi iptal et)" : $"{label}… {NoteStore.Fmt(seconds)}";

    static string LastLine(string log) =>
        (log ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";

    // ---------- Profanity candidates (whisper.cpp) ----------

    string WhisperHelp =>
        "Küfür adaylarını bulmak için whisper.cpp gerekiyor. Ücretsizdir ve tamamen çevrimdışı çalışır: ses bilgisayarından çıkmaz.\n\n" +
        "1. whisper.cpp'nin Windows sürümünü indir: github.com/ggml-org/whisper.cpp > Releases (örneğin whisper-bin-x64.zip). " +
        "Bir klasöre aç; içinde whisper-cli.exe var.\n\n" +
        "2. Çok dilli (Türkçe bilen) bir model indir: huggingface.co/ggerganov/whisper.cpp — örneğin ggml-small.bin (yaklaşık 470 MB), " +
        "daha iyi sonuç için ggml-medium.bin. Adında \".en\" geçen modeller yalnızca İngilizce, onları alma.\n\n" +
        "3. Araçlar > Ayarlar'da Whisper için whisper-cli.exe'yi, model için indirdiğin .bin dosyasını seç.\n\n" +
        "Bulunan kelimeler Kurgu sekmesine \"aday\" yazılı BİP olarak eklenir. Kelime listesi:\n" + TextLists.ProfanityFile;

    void FindProfanity()
    {
        if (!File.Exists(settings.WhisperPath) || !File.Exists(settings.WhisperModel))
        {
            MessageBox.Show(this, WhisperHelp, "KareNot — Whisper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string exe = settings.WhisperPath, model = settings.WhisperModel;
        RunAnalysis("Küfür arama", async (video, path, length, status, cancel) =>
        {
            var profanity = TextLists.Profanity();
            if (profanity.Count == 0) return "Küfür listesi boş: " + TextLists.ProfanityFile;
            var wav = Path.Combine(Path.GetTempPath(), "karenot_ses_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var (exit, log) = await Ffmpeg.ExtractSpeechAudio(path, wav, t => status.Report(Percent("Küfür adayları: ses çıkarılıyor", t, length)), cancel);
                if (exit != 0 || !File.Exists(wav)) return "Ses çıkarılamadı (videoda ses yok mu?): " + LastLine(log);
                status.Report("Küfür adayları: konuşma tanınıyor… (iptal: Araçlar > Analizi iptal et)");
                var json = await Whisper.Transcribe(exe, model, wav, "tr",
                    p => status.Report($"Küfür adayları: konuşma tanınıyor %{p} (iptal: Araçlar > Analizi iptal et)"), cancel);
                var hits = Whisper.Candidates(Whisper.ParseWords(json), profanity);
                cancel.ThrowIfCancellationRequested();

                var bips = CurrentNotes().Where(n => n.Kind == "BIP").Select(n => n.T).ToList();
                var add = new List<WhisperWord>();
                foreach (var h in hits)
                {
                    if (bips.Any(b => Math.Abs(b - h.Start) < 0.5)) continue;
                    add.Add(h);
                    bips.Add(h.Start);
                }
                if (add.Count > 0)
                {
                    RecordUndo("Küfür adayları");
                    bool saved = Saved(() =>
                    {
                        foreach (var h in add)
                        {
                            var n = store.Add(h.Start, "BIP", "aday: " + h.Text, video, null);
                            n.End = Math.Max(h.End + 0.1, h.Start + 0.4);   // whisper can report zero-length words
                        }
                        store.Save(video);
                    });
                    if (!saved)
                    {
                        RefreshList();
                        return "HATA: küfür adayları kaydedilemedi (kayıt durumuna bak).";
                    }
                }
                ShowTab(PanelTab.Edit);
                RefreshList();
                return $"{add.Count} küfür adayı eklendi (Kurgu sekmesi, Ctrl+Z geri alır)" +
                    (hits.Count > add.Count ? $" — {hits.Count - add.Count} tanesi var olan BİP'e yakın olduğu için atlandı" : "");
            }
            finally
            {
                try { File.Delete(wav); } catch { }
            }
        });
    }

    // ---------- Dead air (silent / frozen) ----------

    void FindDeadAir()
    {
        double min = settings.DeadAirMin > 0 ? settings.DeadAirMin : 8;
        RunAnalysis("Ölü an arama", async (video, path, length, status, cancel) =>
        {
            var ranges = await Ffmpeg.DeadAir(path, min, length, t => status.Report(Percent("Ölü anlar: sessiz / donuk yerler aranıyor", t, length)), cancel);
            cancel.ThrowIfCancellationRequested();

            // Skip ranges that overlap an existing cut or contain a lone cut mark (it would pair with the new marks).
            var notes = CurrentNotes();
            var cuts = Kinds.CutRanges(notes);
            var marks = notes.Where(n => n.Kind is "KES_BASLA" or "KES_BITIR").Select(n => n.T).ToList();
            var found = ranges.Select(r => (Start: r.Start, End: length > 0 ? Math.Min(r.End, length) : r.End)).Where(r => r.End - r.Start > 0.5).ToList();
            var add = found.Where(r => !cuts.Any(c => c.Start < r.End && r.Start < c.End) && !marks.Any(m => m >= r.Start && m <= r.End)).ToList();
            if (add.Count > 0)
            {
                RecordUndo("Ölü anlar");
                bool saved = Saved(() =>
                {
                    foreach (var r in add)
                    {
                        var text = "aday: sessiz/donuk " + ((int)Math.Round(r.End - r.Start)).ToString(CultureInfo.InvariantCulture) + " sn";
                        store.Add(r.Start + 0.2, "KES_BASLA", text, video, null);
                        store.Add(r.End - 0.2, "KES_BITIR", text, video, null);
                    }
                });
                if (!saved)
                {
                    RefreshList();
                    return "HATA: ölü an adayları kaydedilemedi (kayıt durumuna bak).";
                }
            }
            ShowTab(PanelTab.Edit);
            RefreshList();
            if (found.Count == 0) return $"Ölü an bulunamadı (en az {min.ToString("0.#", CultureInfo.InvariantCulture)} sn sessiz ya da donuk yer yok).";
            return $"{add.Count} ölü an kesim adayı olarak eklendi (Kurgu sekmesi, Ctrl+Z geri alır)" +
                (found.Count > add.Count ? $" — {found.Count - add.Count} tanesi var olan kesimle çakıştığı için atlandı" : "");
        });
    }
}
