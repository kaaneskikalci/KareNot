using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KareNot;

sealed class Note
{
    public double T;          // seconds
    public string Kind;       // BIP, KES_BASLA, KES_BITIR, NOT, BUG, DEGISIKLIK
    public string Text = "";
    public string Video;      // video file name
    public string Shot;       // screenshot file name in ss\ (old VLC notes: file name prefix), or null
    public string Status;     // game notes: null (open), DUZELTILDI, TEKRAR_TEST
    public string Severity;   // game notes: null, DUSUK, ORTA, KRITIK
    public double? End;       // seconds; BIP ranges
    public string Clip;       // short mp4 clip file name in ss\, or null
    public long Order;        // creation order in this session

    public Note Clone() => (Note)MemberwiseClone();
}

static class Statuses
{
    public static readonly string[] All = { null, "DUZELTILDI", "TEKRAR_TEST" };
    public static string Label(string status) => status switch
    {
        "DUZELTILDI" => "Düzeltildi",
        "TEKRAR_TEST" => "Tekrar test",
        _ => "Açık",
    };
}

static class Severities
{
    public static readonly string[] All = { null, "DUSUK", "ORTA", "KRITIK" };
    public static string Label(string severity) => severity switch
    {
        "DUSUK" => "Düşük",
        "ORTA" => "Orta",
        "KRITIK" => "Kritik",
        _ => "",
    };
}

static class Kinds
{
    public static readonly string[] All = { "BUG", "DEGISIKLIK", "NOT", "BIP", "KES_BASLA", "KES_BITIR" };

    // Video editing marks ("Kurgu" tab); everything else belongs to the game test ("Oyun testi" tab).
    public static readonly string[] Edit = { "BIP", "KES_BASLA", "KES_BITIR" };
    public static bool IsEdit(string kind) => Edit.Contains(kind);

    // Pairs cut marks in time order: a start closes at the next end; a start or end without a partner stays alone.
    public static List<(Note Start, Note End)> Cuts(IEnumerable<Note> notes)
    {
        var result = new List<(Note, Note)>();
        Note open = null;
        foreach (var n in notes.Where(n => n.Kind is "KES_BASLA" or "KES_BITIR").OrderBy(n => n.T))
        {
            if (n.Kind == "KES_BASLA")
            {
                if (open != null) result.Add((open, null));
                open = n;
            }
            else
            {
                result.Add((open, n));
                open = null;
            }
        }
        if (open != null) result.Add((open, null));
        return result;
    }

    public static List<(double Start, double End)> CutRanges(IEnumerable<Note> notes) =>
        Cuts(notes).Where(c => c.Start != null && c.End != null && c.End.T > c.Start.T).Select(c => (c.Start.T, c.End.T)).ToList();

    public static List<(double Start, double End)> BeepRanges(IEnumerable<Note> notes, double defaultLength) =>
        notes.Where(n => n.Kind == "BIP").Select(n => (n.T, n.End is double e && e > n.T ? e : n.T + defaultLength)).ToList();

    public static string Label(string kind) => kind switch
    {
        "BIP" => "BİP",
        "KES_BASLA" => "KES ▶",
        "KES_BITIR" => "KES ■",
        "NOT" => "NOT",
        "BUG" => "BUG",
        "DEGISIKLIK" => "DEĞİŞİKLİK",
        _ => kind,
    };

    public static string ReportTitle(string kind) => kind switch
    {
        "BUG" => "Buglar",
        "DEGISIKLIK" => "Değişiklikler",
        "NOT" => "Notlar",
        "BIP" => "Bipler (küfür)",
        "KES_BASLA" => "Kesim başı",
        "KES_BITIR" => "Kesim sonu",
        _ => kind,
    };

    public static Color Color(string kind) => kind switch
    {
        "BUG" => System.Drawing.Color.FromArgb(220, 40, 40),
        "DEGISIKLIK" => System.Drawing.Color.FromArgb(230, 130, 0),
        "NOT" => System.Drawing.Color.FromArgb(40, 120, 230),
        "BIP" => System.Drawing.Color.FromArgb(160, 60, 200),
        "KES_BASLA" => System.Drawing.Color.FromArgb(30, 160, 70),
        "KES_BITIR" => System.Drawing.Color.FromArgb(20, 110, 50),
        _ => System.Drawing.Color.Gray,
    };
}

static class Paths
{
    // VIDEONOTLARI_DATA overrides the Desktop folder (used by the self test).
    public static string Desktop =>
        Environment.GetEnvironmentVariable("VIDEONOTLARI_DATA") is { Length: > 0 } d ? d : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    // Desktop\KareNot\ holds the shared files and one folder per video, named after the video file without its extension.
    public static string Root => Path.Combine(Desktop, "KareNot");
    public static string ReviewFile => Path.Combine(Root, "inceleme.json");

    public static string FolderName(string video)
    {
        var name = Path.GetFileNameWithoutExtension(video ?? "");
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.TrimEnd('.', ' ');
        return name.Length > 0 ? name : "video";
    }

    // Videos with the same name but another extension share a folder (and its notlar.txt).
    public static bool SameFolder(string a, string b) => string.Equals(FolderName(a), FolderName(b), StringComparison.OrdinalIgnoreCase);
    public static string VideoDir(string video) => Path.Combine(Root, FolderName(video));
    public static string NotesFile(string video) => Path.Combine(VideoDir(video), "notlar.txt");
    public static string ShotsDir(string video) => Path.Combine(VideoDir(video), "ss");
    // Reports and exports of one video go to its folder; with video null (all videos) to the root.
    public static string Dir(string video) => video == null ? Root : VideoDir(video);
    public static string ReportFile(string video) => Path.Combine(Dir(video), "rapor.md");
    public static string HtmlReportFile(string video) => Path.Combine(Dir(video), "rapor.html");
    public static string ExportDir(string video) => Path.Combine(Dir(video), "cikti");

    // Layout before v1.3, moved by Migration: Desktop\Video Notlari\notlar\<video>.txt and one shared ss\; combined files of all
    // notes on the Desktop (old VLC extension) and in the folder (a v1.3 preview).
    public static string OldDir => Path.Combine(Desktop, "Video Notlari");
    public static string[] OldCombinedFiles => new[] { Path.Combine(Desktop, "video_notlari.txt"), Path.Combine(OldDir, "video_notlari.txt") };
}

// Notes live in the video's folder (KareNot\<video>\notlar.txt), in the old VLC extension format:
// seconds <TAB> time <TAB> type <TAB> note <TAB> video <TAB> screenshot, extended with status <TAB> severity <TAB> end seconds
// <TAB> clip. Trailing empty columns are not written, so plain notes keep the old six-column lines. The IINA plugin (iina\)
// reads and writes the same files. Screenshots and clips are file names in the video's ss\.
sealed class NoteStore
{
    const string Header = "# saniye\tzaman\ttur\tnot\tvideo\tss\tdurum\tonem\tbitis\tklip";
    static readonly UTF8Encoding Utf8 = new(false);

    public readonly List<Note> All = new();
    long orderCounter;

    public static string Fmt(double t)
    {
        if (t < 0) t = 0;
        int h = (int)(t / 3600);
        int m = (int)(t % 3600 / 60);
        double s = t % 60;
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00.0}", h, m, Math.Floor(s * 10) / 10);
    }

    public static string Clean(string text) => (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    // Identity of a note when merging files (status, severity and media may differ between copies).
    public static string Key(Note n) => string.Join('\t', n.Video, n.T.ToString("0.00", CultureInfo.InvariantCulture), n.Kind, Clean(n.Text));

    public static List<Note> ReadFile(string path)
    {
        var result = new List<Note>();
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadAllLines(path, Utf8))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var f = line.Split('\t');
            // Old files have five columns (no screenshot); new ones six.
            if (f.Length < 5 || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
            string Col(int i) => f.Length > i && f[i].Length > 0 ? f[i] : null;
            result.Add(new Note
            {
                T = t,
                Kind = f[2],
                Text = f[3],
                Video = f[4],
                Shot = Col(5),
                Status = Col(6),
                Severity = Col(7),
                End = double.TryParse(Col(8), NumberStyles.Float, CultureInfo.InvariantCulture, out var end) ? end : null,
                Clip = Col(9),
            });
        }
        return result;
    }

    // Every KareNot\*\notlar.txt (run Migration first).
    public static List<Note> ReadAll() => !Directory.Exists(Paths.Root) ? new List<Note>()
        : Directory.GetDirectories(Paths.Root).SelectMany(d => ReadFile(Path.Combine(d, "notlar.txt"))).ToList();

    public void Load()
    {
        All.Clear();
        All.AddRange(ReadAll());
        Sort();
    }

    void Sort() => All.Sort((a, b) =>
    {
        var c = string.CompareOrdinal(a.Video, b.Video);
        return c != 0 ? c : a.T.CompareTo(b.T);
    });

    static string Line(Note n)
    {
        var cols = new List<string>
        {
            n.T.ToString("0.00", CultureInfo.InvariantCulture), Fmt(n.T), n.Kind, Clean(n.Text), n.Video, n.Shot ?? "",
            n.Status ?? "", n.Severity ?? "", n.End?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", n.Clip ?? "",
        };
        while (cols.Count > 6 && cols[^1].Length == 0) cols.RemoveAt(cols.Count - 1);
        return string.Join('\t', cols);
    }

    // Writes the video's notlar.txt with the notes of every video sharing its folder.
    void SaveVideo(string video)
    {
        var notes = All.Where(n => Paths.SameFolder(n.Video, video)).ToList();
        var path = Paths.NotesFile(video);
        if (notes.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        WriteAtomic(path, notes);
    }

    public static void WriteAtomic(string path, IEnumerable<Note> notes)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var n in notes) sb.Append(Line(n)).Append('\n');
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), Utf8);
        File.Move(tmp, path, true);
    }

    public void Save(string video)
    {
        Sort();
        SaveVideo(video);
    }

    public Note Add(double t, string kind, string text, string video, string shot)
    {
        var n = new Note { T = t, Kind = kind, Text = Clean(text), Video = video, Shot = shot, Order = ++orderCounter };
        All.Add(n);
        Save(video);
        return n;
    }

    public void Remove(Note n)
    {
        All.Remove(n);
        Save(n.Video);
    }

    // Copies of one video's notes, for undo / redo.
    public List<Note> Snapshot(string video) => All.Where(n => n.Video == video).Select(n => n.Clone()).ToList();

    public void Restore(string video, List<Note> notes)
    {
        All.RemoveAll(n => n.Video == video);
        All.AddRange(notes.Select(n => n.Clone()));
        Save(video);
    }

    // File names of a note's screenshot / clip in its video's ss\, or null; old VLC notes only stored a prefix of the shot name.
    public static string ResolveShot(Note n) => FindMedia(Paths.ShotsDir(n.Video), n.Shot, true);
    public static string ResolveClip(Note n) => FindMedia(Paths.ShotsDir(n.Video), n.Clip, false);

    public static string FindMedia(string dir, string name, bool prefix)
    {
        if (string.IsNullOrEmpty(name) || !Directory.Exists(dir)) return null;
        if (File.Exists(Path.Combine(dir, name))) return name;
        return prefix ? Directory.GetFiles(dir, name + "*").Select(Path.GetFileName).OrderBy(x => x).FirstOrDefault() : null;
    }

    // Writes rapor.md and rapor.html into the video's folder (the notes of that folder), or for all notes into the root when
    // video is null; returns the Markdown path.
    public string WriteReport(string video, out int missing)
    {
        var notes = video == null ? All : All.Where(n => Paths.SameFolder(n.Video, video)).ToList();
        Func<string, string> media = video == null ? Reports.FolderMedia : null;
        Directory.CreateDirectory(Paths.Dir(video));
        File.WriteAllText(Paths.ReportFile(video), Reports.Markdown(notes, out missing, media), Utf8);
        File.WriteAllText(Paths.HtmlReportFile(video), Reports.Html(notes, media), Utf8);
        return Paths.ReportFile(video);
    }
}

// One-time move from the layout before v1.3 (Desktop\Video Notlari\notlar\<video>.txt, one shared ss\, combined files) to
// Desktop\KareNot\<video>\. Runs at startup before loading and can run again after a failure: notes already in the new files are
// not added twice, media are copied (identical copies are reused) and their old files deleted only after all notes are written.
// The old folder is then renamed to "Video Notlari (eski)", with its reports and exports in it.
static class Migration
{
    // Returns a message for the status line (null when there was nothing to move) and whether it failed.
    public static (string Message, bool Failed) Run()
    {
        var oldDir = Paths.OldDir;
        var combined = Paths.OldCombinedFiles.Where(File.Exists).ToList();
        if (!Directory.Exists(oldDir) && combined.Count == 0) return (null, false);
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var oldNotesDir = Path.Combine(oldDir, "notlar");
            var notes = Directory.Exists(oldNotesDir) ? Directory.GetFiles(oldNotesDir, "*.txt").SelectMany(NoteStore.ReadFile).ToList() : new List<Note>();
            // Combined files only for videos found nowhere else.
            var known = new HashSet<string>(notes.Concat(NoteStore.ReadAll()).Select(n => n.Video));
            notes.AddRange(combined.SelectMany(NoteStore.ReadFile).Where(n => !known.Contains(n.Video)));

            var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var group in notes.GroupBy(n => Paths.FolderName(n.Video), StringComparer.OrdinalIgnoreCase))
            {
                var file = Paths.NotesFile(group.First().Video);
                var merged = NoteStore.ReadFile(file);
                var keys = new HashSet<string>(merged.Select(NoteStore.Key));
                int before = merged.Count;
                foreach (var n in group.Where(n => keys.Add(NoteStore.Key(n))))
                {
                    n.Shot = CopyMedia(n.Shot, true, Paths.ShotsDir(n.Video), copied);
                    n.Clip = CopyMedia(n.Clip, false, Paths.ShotsDir(n.Video), copied);
                    merged.Add(n);
                }
                if (merged.Count == before) continue;
                added += merged.Count - before;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                NoteStore.WriteAtomic(file, merged.OrderBy(n => n.Video, StringComparer.Ordinal).ThenBy(n => n.T));
            }
            foreach (var source in copied.Keys.Select(k => k.Split('|')[0]).Distinct())
                try { File.Delete(source); } catch { }

            // Shared files: review progress (merged into an existing one), editable lists.
            var oldReview = Path.Combine(oldDir, "inceleme.json");
            if (File.Exists(oldReview))
            {
                if (!File.Exists(Paths.ReviewFile)) File.Move(oldReview, Paths.ReviewFile);
                else MergeReview(oldReview, Paths.ReviewFile);
            }
            foreach (var name in new[] { "hazir_notlar.txt", "kufur_listesi.txt" })
                if (File.Exists(Path.Combine(oldDir, name)) && !File.Exists(Path.Combine(Paths.Root, name)))
                    File.Move(Path.Combine(oldDir, name), Path.Combine(Paths.Root, name));
            foreach (var file in combined)
            {
                var target = Path.Combine(Paths.Root, "video_notlari_eski.txt");
                for (int i = 2; File.Exists(target); i++) target = Path.Combine(Paths.Root, $"video_notlari_eski_{i}.txt");
                File.Move(file, target);
            }

            var renamed = "";
            if (Directory.Exists(oldDir))
            {
                var target = oldDir + " (eski)";
                for (int i = 2; Directory.Exists(target) || File.Exists(target); i++) target = $"{oldDir} (eski {i})";
                Directory.Move(oldDir, target);
                renamed = $" Eski klasör: {Path.GetFileName(target)}.";
            }
            var message = $"Eski veriler Masaüstü\\KareNot klasörüne taşındı ({added} not, {copied.Count} SS / klip).{renamed}";
            Log(message);
            return (message, false);
        }
        catch (Exception ex)
        {
            var message = $"HATA: eski veriler taşınamadı, yerinde duruyor ({oldDir}): {ex.Message}";
            Log(message + "\n" + ex);
            return (message, true);
        }
    }

    // Copies a screenshot / clip of the old shared ss\ into the video's ss\; returns its name there (suffixed when the name is
    // taken by another file). A missing file keeps its reference.
    static string CopyMedia(string name, bool prefix, string dir, Dictionary<string, string> copied)
    {
        var oldShots = Path.Combine(Paths.OldDir, "ss");
        var found = NoteStore.FindMedia(oldShots, name, prefix);
        if (found == null) return name;
        var source = Path.Combine(oldShots, found);
        if (copied.TryGetValue(source + "|" + dir, out var done)) return done;
        Directory.CreateDirectory(dir);
        var target = found;
        for (int i = 2; File.Exists(Path.Combine(dir, target)) && !SameContent(source, Path.Combine(dir, target)); i++)
            target = Path.GetFileNameWithoutExtension(found) + "_" + i + Path.GetExtension(found);
        if (!File.Exists(Path.Combine(dir, target))) File.Copy(source, Path.Combine(dir, target));
        return copied[source + "|" + dir] = target;
    }

    static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    // Adds the old file's videos and keys that the new inceleme.json does not have; an unreadable old file is left alone.
    static void MergeReview(string oldFile, string newFile)
    {
        JsonObject old;
        try { old = JsonNode.Parse(File.ReadAllText(oldFile)) as JsonObject; } catch { return; }
        if (old == null) return;
        var root = JsonNode.Parse(File.ReadAllText(newFile)) as JsonObject ?? new JsonObject();
        foreach (var (video, entry) in old)
        {
            if (root[video] is JsonObject current && entry is JsonObject oldEntry)
            {
                foreach (var (key, value) in oldEntry)
                    if (!current.ContainsKey(key)) current[key] = value?.DeepClone();
            }
            else if (!root.ContainsKey(video)) root[video] = entry?.DeepClone();
        }
        File.WriteAllText(newFile + ".tmp", root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(newFile + ".tmp", newFile, true);
    }

    static void Log(string text)
    {
        try { File.AppendAllText(Path.Combine(Paths.Root, "tasima_gunlugu.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}\n", new UTF8Encoding(false)); }
        catch { }
    }
}
