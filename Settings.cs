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

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoNotlari");
    static string FilePath => Path.Combine(Dir, "ayarlar.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
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

    static JsonObject Root
    {
        get
        {
            if (root != null) return root;
            try { root = JsonNode.Parse(File.ReadAllText(Paths.ReviewFile)) as JsonObject; } catch { }
            return root ??= new JsonObject();
        }
    }

    public static void Reload() => root = null;

    static JsonObject Entry(string video, bool create)
    {
        if (Root[video] is JsonObject e) return e;
        if (!create) return null;
        e = new JsonObject();
        Root[video] = e;
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
        if (string.IsNullOrEmpty(video)) return;
        Entry(video, true)[key] = value;
        Save();
    }

    public static void SetPosition(string video, double seconds)
    {
        var e = Entry(video, true);
        e["pos"] = Math.Round(seconds, 2);
        Save();
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var tmp = Paths.ReviewFile + ".tmp";
            File.WriteAllText(tmp, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
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
