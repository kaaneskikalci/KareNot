using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KareNot;

// Machine settings in %APPDATA%\VideoNotlari\ayarlar.json (the folder name predates the KareNot rename).
sealed class AppSettings
{
    public double ClipBefore { get; set; } = 5;
    public double ClipAfter { get; set; } = 5;
    public double LoopBefore { get; set; } = 3;
    public double LoopAfter { get; set; } = 3;
    public double BeepDefault { get; set; } = 0.6;   // seconds bleeped for a BIP without an end
    public double DeadAirMin { get; set; } = 8;      // seconds of silence / frozen picture before it becomes a cut candidate
    public string FfmpegPath { get; set; } = "";
    public string WhisperPath { get; set; } = "";
    public string WhisperModel { get; set; } = "";
    public string DataFolder { get; set; } = "";     // where the notes go; empty: Paths.DefaultRoot (next to KareNot.exe)

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoNotlari");
    static string FilePath => Path.Combine(Dir, "ayarlar.json");

    public static AppSettings Load()
    {
        try { return Parse(File.ReadAllText(FilePath)); }
        catch { return new AppSettings(); }
    }

    // A hand-edited file may hold null strings or nonsense numbers; those fall back to the defaults (a null DataFolder used to
    // crash every start in Paths.Init).
    public static AppSettings Parse(string json)
    {
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings(); }
        catch (Exception) { return new AppSettings(); }
        var d = new AppSettings();
        static double Num(double v, double fallback) => double.IsFinite(v) && v >= 0 ? v : fallback;
        s.ClipBefore = Num(s.ClipBefore, d.ClipBefore);
        s.ClipAfter = Num(s.ClipAfter, d.ClipAfter);
        s.LoopBefore = Num(s.LoopBefore, d.LoopBefore);
        s.LoopAfter = Num(s.LoopAfter, d.LoopAfter);
        s.BeepDefault = s.BeepDefault > 0 && double.IsFinite(s.BeepDefault) ? s.BeepDefault : d.BeepDefault;
        s.DeadAirMin = s.DeadAirMin > 0 && double.IsFinite(s.DeadAirMin) ? s.DeadAirMin : d.DeadAirMin;
        s.FfmpegPath ??= "";
        s.WhisperPath ??= "";
        s.WhisperModel ??= "";
        s.DataFolder ??= "";
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

// Per-video review progress and session info in KareNot\inceleme.json (one file for all videos), shared with the IINA plugin:
// { "<video>": { "pos": s, "reviewed": s, "done": bool, "build": "", "tester": "" } }. Unknown keys are kept.
static class ReviewStore
{
    static JsonObject root;

    // The file as read, or null while it exists but can't be read (locked by another program, a cloud placeholder offline):
    // then reads give defaults, writes are skipped and the next access tries again, so the other videos' progress is never
    // replaced by an empty file. A file that is not a JSON object is kept as inceleme.json.bozuk-<time> and started over.
    static JsonObject Root
    {
        get
        {
            if (root != null) return root;
            var path = Paths.ReviewFile;
            if (!File.Exists(path)) return root = new JsonObject();
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception) { return null; }
            try { root = JsonNode.Parse(text) as JsonObject; } catch (Exception) { }
            if (root != null) return root;
            try { File.Copy(path, $"{path}.bozuk-{DateTime.Now:yyyyMMdd_HHmmss}", true); }
            catch (Exception) { return null; }
            return root = new JsonObject();
        }
    }

    public static void Reload() => root = null;

    static JsonObject Entry(string video, bool create)
    {
        var r = Root;
        if (r == null) return null;
        if (r[video] is JsonObject e) return e;
        if (!create) return null;
        e = new JsonObject();
        r[video] = e;
        return e;
    }

    static double Num(JsonObject e, string key) => e?[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
    static string Str(JsonObject e, string key) => e?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public static double Position(string video) => Num(Entry(video, false), "pos");
    public static double Reviewed(string video) => Num(Entry(video, false), "reviewed");
    public static double Length(string video) => Num(Entry(video, false), "length");
    public static bool Done(string video) => Entry(video, false)?["done"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    public static string Build(string video) => Str(Entry(video, false), "build");
    public static string Tester(string video) => Str(Entry(video, false), "tester");

    public static void Set(string video, string key, JsonNode value)
    {
        if (string.IsNullOrEmpty(video) || Entry(video, true) is not JsonObject e) return;
        e[key] = value;
        Save();
    }

    public static void SetPosition(string video, double seconds) => Set(video, "pos", Math.Round(seconds, 2));

    static void Save()
    {
        if (root == null) return;
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var tmp = Paths.ReviewFile + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(tmp, Paths.ReviewFile, true);
        }
        catch { }
    }

    // "Başlanmadı" / "Devam ediyor" / "Tamamlandı".
    public static string State(string video, bool hasNotes)
    {
        if (Done(video)) return "Tamamlandı";
        return hasNotes || Position(video) > 1 || Reviewed(video) > 0 ? "Devam ediyor" : "Başlanmadı";
    }
}

// User-editable text lists in the KareNot root: note templates and the words that make a BIP candidate.
static class TextLists
{
    public static string TemplatesFile => Path.Combine(Paths.Root, "hazir_notlar.txt");
    public static string ProfanityFile => Path.Combine(Paths.Root, "kufur_listesi.txt");

    const string TemplatesDefault =
        "# Hazır not metinleri: her satır bir metin. # ile başlayan satırlar yok sayılır.\n" +
        "Ses çalmadı\nKamera takıldı\nAnimasyon kesildi\nKarakter duvardan geçti\nUI yazısı taşıyor\nFPS düştü\n";

    const string ProfanityDefault =
        "# Küfür adayı sayılacak kelimeler (konuşma tanıma için): her satır bir kelime ya da kelime başı.\n" +
        "# Kelime başı yazmak yeter, örneğin \"sik\" sikerim, siktir gibi çekimleri de yakalar. Büyük/küçük harf fark etmez.\n" +
        "amk\naq\nsik\norospu\npiç\nyarrak\nananı\nsiktir\ngöt\nmal\ngerizekalı\n";

    public static List<string> Read(string path, string defaults)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, defaults, new UTF8Encoding(false));
            }
            return File.ReadAllLines(path, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        }
        catch { return new List<string>(); }
    }

    public static List<string> Templates() => Read(TemplatesFile, TemplatesDefault);
    public static List<string> Profanity() => Read(ProfanityFile, ProfanityDefault).Select(w => w.ToLower(new CultureInfo("tr-TR"))).ToList();
}
