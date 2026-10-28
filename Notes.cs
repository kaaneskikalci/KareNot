using System.Globalization;
using System.Text;

namespace VideoNotlari;

sealed class Note
{
    public double T;          // seconds
    public string Kind;       // BIP, KES_BASLA, KES_BITIR, NOT, BUG, DEGISIKLIK
    public string Text = "";
    public string Video;      // video file name
    public string Shot;       // screenshot file name in ss\ (old VLC notes: file name prefix), or null
    public long Order;        // creation order in this session, for "delete last"
}

static class Kinds
{
    public static readonly string[] All = { "BUG", "DEGISIKLIK", "NOT", "BIP", "KES_BASLA", "KES_BITIR" };

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
        "BIP" => "Bip (küfür)",
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
    public static string CombinedFile => Path.Combine(Desktop, "video_notlari.txt");
    public static string WorkDir => Path.Combine(Desktop, "Video Notlari");
    public static string NotesDir => Path.Combine(WorkDir, "notlar");
    public static string ShotsDir => Path.Combine(WorkDir, "ss");
    public static string ReportFile => Path.Combine(WorkDir, "rapor.md");
}

// Notes live in one file per video (Video Notlari\notlar\<video>.txt); Desktop\video_notlari.txt is kept as the
// combined file in the old VLC extension format: seconds <TAB> time <TAB> type <TAB> note <TAB> video <TAB> screenshot.
sealed class NoteStore
{
    const string Header = "# saniye\tzaman\ttur\tnot\tvideo\tss";
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

    static string VideoFile(string video)
    {
        var name = video;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return Path.Combine(Paths.NotesDir, name + ".txt");
    }

    static List<Note> ReadFile(string path)
    {
        var result = new List<Note>();
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadAllLines(path, Utf8))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var f = line.Split('\t');
            // Old files have five columns (no screenshot); new ones six.
            if (f.Length < 5 || !double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
            result.Add(new Note
            {
                T = t,
                Kind = f[2],
                Text = f[3],
                Video = f[4],
                Shot = f.Length > 5 && f[5].Length > 0 ? f[5] : null,
            });
        }
        return result;
    }

    public void Load()
    {
        All.Clear();
        var videos = new HashSet<string>();
        if (Directory.Exists(Paths.NotesDir))
        {
            foreach (var file in Directory.GetFiles(Paths.NotesDir, "*.txt"))
            {
                foreach (var n in ReadFile(file))
                {
                    All.Add(n);
                    videos.Add(n.Video);
                }
            }
        }

        // Notes from the combined file whose video has no own file yet (e.g. written by the VLC extension).
        var migrated = new HashSet<string>();
        foreach (var n in ReadFile(Paths.CombinedFile))
        {
            if (videos.Contains(n.Video)) continue;
            All.Add(n);
            migrated.Add(n.Video);
        }
        Sort();
        foreach (var v in migrated) SaveVideo(v);
    }

    void Sort() => All.Sort((a, b) =>
    {
        var c = string.CompareOrdinal(a.Video, b.Video);
        return c != 0 ? c : a.T.CompareTo(b.T);
    });

    static string Line(Note n) => string.Format(CultureInfo.InvariantCulture, "{0:0.00}\t{1}\t{2}\t{3}\t{4}\t{5}",
        n.T, Fmt(n.T), n.Kind, Clean(n.Text), n.Video, n.Shot ?? "");

    void SaveVideo(string video)
    {
        var notes = All.Where(n => n.Video == video).ToList();
        var path = VideoFile(video);
        if (notes.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Paths.NotesDir);
        WriteAtomic(path, notes);
    }

    static void WriteAtomic(string path, IEnumerable<Note> notes)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var n in notes) sb.Append(Line(n)).Append('\n');
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), Utf8);
        File.Move(tmp, path, true);
    }

    // Saves the video's own file and the combined file.
    public void Save(string video)
    {
        Sort();
        SaveVideo(video);
        WriteAtomic(Paths.CombinedFile, All);
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

    // Finds the screenshot file of a note; old VLC notes only stored a prefix of the file name.
    public static string ResolveShot(string shot)
    {
        if (string.IsNullOrEmpty(shot) || !Directory.Exists(Paths.ShotsDir)) return null;
        if (File.Exists(Path.Combine(Paths.ShotsDir, shot))) return shot;
        return Directory.GetFiles(Paths.ShotsDir, shot + "*").Select(Path.GetFileName).OrderBy(x => x).FirstOrDefault();
    }

    // Markdown report grouped by video and type (bugs and changes first), screenshots linked relative to the report.
    public string WriteReport(out int missing)
    {
        missing = 0;
        Directory.CreateDirectory(Paths.WorkDir);
        var sb = new StringBuilder();
        sb.Append("# Video notları — bug fixing raporu\n\n");
        sb.Append("Oluşturuldu: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("\n\n");
        foreach (var video in All.Select(n => n.Video).Distinct().OrderBy(v => v, StringComparer.Ordinal))
        {
            var notes = All.Where(n => n.Video == video).ToList();
            sb.Append("## ").Append(video).Append("\n\n");
            sb.Append(string.Format("{0} bug, {1} değişiklik, {2} not\n\n",
                notes.Count(n => n.Kind == "BUG"), notes.Count(n => n.Kind == "DEGISIKLIK"), notes.Count(n => n.Kind == "NOT")));
            foreach (var kind in Kinds.All.Concat(notes.Select(n => n.Kind).Where(k => !Kinds.All.Contains(k)).Distinct()))
            {
                var ofKind = notes.Where(n => n.Kind == kind).OrderBy(n => n.T).ToList();
                if (ofKind.Count == 0) continue;
                sb.Append("### ").Append(Kinds.ReportTitle(kind)).Append("\n\n");
                foreach (var n in ofKind)
                {
                    sb.Append("- **").Append(Fmt(n.T)).Append("** — ").Append(n.Text.Length > 0 ? n.Text : "(açıklama yok)").Append('\n');
                    if (n.Shot == null) continue;
                    var file = ResolveShot(n.Shot);
                    if (file != null)
                        sb.Append("\n  ![").Append(Fmt(n.T)).Append("](ss/").Append(Uri.EscapeDataString(file)).Append(")\n\n");
                    else
                    {
                        missing++;
                        sb.Append("  - (SS bulunamadı: ").Append(n.Shot).Append(")\n");
                    }
                }
                sb.Append('\n');
            }
        }
        File.WriteAllText(Paths.ReportFile, sb.ToString(), Utf8);
        return Paths.ReportFile;
    }
}
