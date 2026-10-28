using System.Globalization;
using System.Text;

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
    public static string CombinedFile => Path.Combine(Desktop, "video_notlari.txt");
    public static string WorkDir => Path.Combine(Desktop, "Video Notlari");
    public static string NotesDir => Path.Combine(WorkDir, "notlar");
    public static string ShotsDir => Path.Combine(WorkDir, "ss");
    public static string ReportFile => Path.Combine(WorkDir, "rapor.md");
    public static string HtmlReportFile => Path.Combine(WorkDir, "rapor.html");
    public static string ReviewFile => Path.Combine(WorkDir, "inceleme.json");
    public static string ExportDir => Path.Combine(WorkDir, "cikti");
}

// Notes live in one file per video (Video Notlari\notlar\<video>.txt); Desktop\video_notlari.txt is kept as the
// combined file in the old VLC extension format: seconds <TAB> time <TAB> type <TAB> note <TAB> video <TAB> screenshot,
// extended with status <TAB> severity <TAB> end seconds <TAB> clip. Trailing empty columns are not written, so plain notes
// keep the old six-column lines. The IINA plugin (iina\) reads and writes the same files.
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

    // Copies of one video's notes, for undo / redo.
    public List<Note> Snapshot(string video) => All.Where(n => n.Video == video).Select(n => n.Clone()).ToList();

    public void Restore(string video, List<Note> notes)
    {
        All.RemoveAll(n => n.Video == video);
        All.AddRange(notes.Select(n => n.Clone()));
        Save(video);
    }

    // Finds the screenshot file of a note; old VLC notes only stored a prefix of the file name.
    public static string ResolveShot(string shot)
    {
        if (string.IsNullOrEmpty(shot) || !Directory.Exists(Paths.ShotsDir)) return null;
        if (File.Exists(Path.Combine(Paths.ShotsDir, shot))) return shot;
        return Directory.GetFiles(Paths.ShotsDir, shot + "*").Select(Path.GetFileName).OrderBy(x => x).FirstOrDefault();
    }

    // Writes rapor.md and rapor.html for all notes; returns the Markdown path.
    public string WriteReport(out int missing)
    {
        Directory.CreateDirectory(Paths.WorkDir);
        File.WriteAllText(Paths.ReportFile, Reports.Markdown(All, out missing), Utf8);
        File.WriteAllText(Paths.HtmlReportFile, Reports.Html(All), Utf8);
        return Paths.ReportFile;
    }
}
