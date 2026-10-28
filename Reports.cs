using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KareNot;

// Report and export formats: Markdown / HTML report, CSV, DaVinci Resolve marker EDL, task briefs and editing notes.
// Media links are relative to the Video Notlari folder (ss/...) unless a prefix is given.
static class Reports
{
    static readonly CultureInfo Tr = new("tr-TR");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] GameKinds = { "BUG", "DEGISIKLIK", "NOT" };

    public static bool IsOpen(Note n) => n.Status is null or "TEKRAR_TEST";

    public static string Duration(double s)
    {
        s = Math.Max(0, s);
        if (s < 60) return s.ToString("0.#", Tr) + " sn";
        var ts = TimeSpan.FromSeconds(Math.Round(s));
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours} sa {ts.Minutes} dk {ts.Seconds} sn" : $"{ts.Minutes} dk {ts.Seconds} sn";
    }

    static string Range(double a, double b) => NoteStore.Fmt(a) + "–" + NoteStore.Fmt(b);
    static string Esc(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    static string Url(string prefix, string file) => prefix + Uri.EscapeDataString(file);
    static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
    static string ClipFile(Note n) => n.Clip != null && File.Exists(Path.Combine(Paths.ShotsDir, n.Clip)) ? n.Clip : null;

    static string Md(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? "")
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '|') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    static List<string> Tags(Note n)
    {
        var tags = new List<string>();
        if (n.Severity != null) tags.Add(Severities.Label(n.Severity) is { Length: > 0 } l ? l : n.Severity);
        if (n.Status != null) tags.Add(Statuses.Label(n.Status));
        return tags;
    }

    // ---------- Report model shared by Markdown and HTML ----------

    sealed record Item(string Time, string Extra, string Text, List<string> Tags, string Warning, string Shot, string ShotMissing, string Clip, string ClipMissing, bool Done);
    sealed record Section(string Title, string Kind, List<Item> Items, string Footer);
    sealed record VideoPart(string Video, string Session, List<(string Text, string Kind)> Counts, List<Section> Sections);

    static List<VideoPart> Build(IEnumerable<Note> all, ref int missing)
    {
        var parts = new List<VideoPart>();
        int lost = 0;
        foreach (var group in all.GroupBy(n => n.Video ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var notes = group.OrderBy(n => n.T).ToList();
            var video = group.Key;
            var session = new List<string>();
            if (ReviewStore.Build(video) is { Length: > 0 } build) session.Add("Build: " + build);
            if (ReviewStore.Tester(video) is { Length: > 0 } tester) session.Add("Testçi: " + tester);
            session.Add("Durum: " + ReviewStore.State(video, notes.Count > 0));

            int bugs = notes.Count(n => n.Kind == "BUG"), openBugs = notes.Count(n => n.Kind == "BUG" && IsOpen(n));
            var cuts = Kinds.Cuts(notes);
            var pairs = cuts.Where(c => c.Start != null && c.End != null && c.End.T > c.Start.T).ToList();
            double cutTotal = Ffmpeg.Merge(pairs.Select(c => (c.Start.T, c.End.T))).Sum(r => r.End - r.Start);
            var counts = new List<(string, string)> { ($"{bugs} bug ({openBugs} açık)", "BUG") };
            counts.Add(($"{notes.Count(n => n.Kind == "DEGISIKLIK")} değişiklik", "DEGISIKLIK"));
            counts.Add(($"{notes.Count(n => n.Kind == "NOT")} not", "NOT"));
            if (notes.Any(n => n.Kind == "BIP")) counts.Add(($"{notes.Count(n => n.Kind == "BIP")} bip", "BIP"));
            if (cuts.Count > 0) counts.Add(($"{pairs.Count} kesim ({Duration(cutTotal)})", "KES_BASLA"));

            var sections = new List<Section>();
            Item GameItem(Note n)
            {
                string shot = null, shotMissing = null, clip = null, clipMissing = null;
                if (n.Shot != null)
                {
                    shot = NoteStore.ResolveShot(n.Shot);
                    if (shot == null) { lost++; shotMissing = n.Shot; }
                }
                if (n.Clip != null)
                {
                    clip = ClipFile(n);
                    if (clip == null) clipMissing = n.Clip;
                }
                return new Item(NoteStore.Fmt(n.T), null, n.Text.Length > 0 ? n.Text : "(açıklama yok)", Tags(n), null, shot, shotMissing, clip, clipMissing, n.Status == "DUZELTILDI");
            }
            foreach (var kind in GameKinds)
            {
                var items = notes.Where(n => n.Kind == kind).Select(GameItem).ToList();
                if (items.Count > 0) sections.Add(new Section(Kinds.ReportTitle(kind), kind, items, null));
            }
            var bips = notes.Where(n => n.Kind == "BIP").Select(n => new Item(n.End is double e && e > n.T ? Range(n.T, e) : NoteStore.Fmt(n.T),
                null, n.Text, new List<string>(), null, null, null, null, null, false)).ToList();
            if (bips.Count > 0) sections.Add(new Section(Kinds.ReportTitle("BIP"), "BIP", bips, null));
            if (cuts.Count > 0)
            {
                var items = new List<Item>();
                foreach (var (s, e) in cuts)
                {
                    var text = string.Join(" / ", new[] { s?.Text, e?.Text }.Where(t => !string.IsNullOrEmpty(t)));
                    if (s != null && e != null && e.T > s.T)
                        items.Add(new Item(Range(s.T, e.T), "(" + Duration(e.T - s.T) + ")", text, new List<string>(), null, null, null, null, null, false));
                    else
                        items.Add(new Item(NoteStore.Fmt((s ?? e).T), null, text, new List<string>(), s != null ? "bitişi yok" : "başı yok", null, null, null, null, false));
                }
                sections.Add(new Section("Kesimler", "KES_BASLA", items, "Toplam kesilen süre: " + Duration(cutTotal)));
            }
            foreach (var kind in notes.Select(n => n.Kind).Where(k => !Kinds.All.Contains(k)).Distinct())
                sections.Add(new Section(Kinds.ReportTitle(kind), kind, notes.Where(n => n.Kind == kind).Select(GameItem).ToList(), null));
            parts.Add(new VideoPart(video, string.Join(" · ", session), counts, sections));
        }
        missing += lost;
        return parts;
    }

    public static string Markdown(IReadOnlyCollection<Note> notes, out int missing)
    {
        missing = 0;
        var parts = Build(notes, ref missing);
        var sb = new StringBuilder();
        sb.Append("# KareNot raporu\n\n");
        sb.Append("Oluşturuldu: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)).Append($" · {parts.Count} video, {notes.Count} not\n\n");
        foreach (var p in parts)
        {
            sb.Append("## ").Append(Md(p.Video)).Append("\n\n");
            sb.Append(Md(p.Session)).Append("\n\n");
            sb.Append(string.Join(" · ", p.Counts.Select((c, i) => i == 0 ? "**" + c.Text + "**" : c.Text))).Append("\n\n");
            foreach (var s in p.Sections)
            {
                sb.Append("### ").Append(s.Title).Append("\n\n");
                foreach (var it in s.Items)
                {
                    sb.Append("- **").Append(it.Time).Append("**");
                    if (it.Extra != null) sb.Append(' ').Append(it.Extra);
                    if (!string.IsNullOrEmpty(it.Text)) sb.Append(" — ").Append(Md(it.Text));
                    foreach (var t in it.Tags) sb.Append(" `").Append(t).Append('`');
                    if (it.Warning != null) sb.Append(" — ⚠ ").Append(it.Warning);
                    sb.Append('\n');
                    if (it.Shot != null) sb.Append("\n  ![").Append(it.Time).Append("](").Append(Url("ss/", it.Shot)).Append(")\n\n");
                    if (it.ShotMissing != null) sb.Append("  - (SS bulunamadı: ").Append(Md(it.ShotMissing)).Append(")\n");
                    if (it.Clip != null) sb.Append("  [Klip](").Append(Url("ss/", it.Clip)).Append(")\n\n");
                    if (it.ClipMissing != null) sb.Append("  - (Klip bulunamadı: ").Append(Md(it.ClipMissing)).Append(")\n");
                }
                if (s.Footer != null) sb.Append('\n').Append(s.Footer).Append('\n');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    const string Css = @"
:root{color-scheme:dark}
*{box-sizing:border-box}
body{margin:0;background:#1b1b1d;color:#ddd;font:15px/1.55 'Segoe UI',system-ui,sans-serif}
main{max-width:1100px;margin:0 auto;padding:28px 20px 60px}
h1{margin:0 0 4px;font-size:26px;color:#fff}
.gen{color:#888;margin-bottom:18px}
.toc a{color:#9cdcfe;text-decoration:none;margin-right:14px}
section.video{background:#252527;border:1px solid #333;border-radius:10px;padding:18px 22px;margin:22px 0}
h2{margin:0 0 4px;font-size:20px;color:#fff;word-break:break-all}
.session{color:#aaa;margin-bottom:10px}
.counts{display:flex;flex-wrap:wrap;gap:6px;margin-bottom:6px}
h3{margin:22px 0 8px;font-size:16px;padding-left:10px;border-left:4px solid var(--c)}
ul{list-style:none;margin:0;padding:0}
li{padding:9px 0;border-top:1px solid #333}
li.done{opacity:.6}
li.done .txt{text-decoration:line-through}
.t{font-family:Consolas,monospace;color:#9cdcfe;margin-right:8px;white-space:nowrap}
.x{color:#aaa;margin-right:8px}
.b{display:inline-block;padding:0 8px;margin:0 2px;border-radius:10px;font-size:12px;line-height:20px;border:1px solid var(--c);color:var(--c);background:color-mix(in srgb,var(--c) 14%,transparent)}
.warn{color:#ffb454;margin-left:6px}
.miss{color:#e57373;font-size:13px;margin-top:4px}
.media{display:flex;flex-wrap:wrap;gap:10px;margin-top:8px}
.media img,.media video{max-width:360px;width:100%;border-radius:6px;border:1px solid #3a3a3a;display:block;background:#000}
.foot{color:#aaa;margin-top:8px}
";

    public static string Html(IReadOnlyCollection<Note> notes)
    {
        int missing = 0;
        var parts = Build(notes, ref missing);
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"tr\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>KareNot raporu</title><style>").Append(Css).Append("</style></head><body><main>\n");
        sb.Append("<h1>KareNot raporu</h1><div class=\"gen\">Oluşturuldu: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv))
            .Append($" · {parts.Count} video, {notes.Count} not</div>\n");
        if (parts.Count > 1)
        {
            sb.Append("<nav class=\"toc\">");
            for (int i = 0; i < parts.Count; i++) sb.Append($"<a href=\"#v{i}\">").Append(Esc(parts[i].Video)).Append("</a>");
            sb.Append("</nav>\n");
        }
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            sb.Append($"<section class=\"video\" id=\"v{i}\"><h2>").Append(Esc(p.Video)).Append("</h2>");
            sb.Append("<div class=\"session\">").Append(Esc(p.Session)).Append("</div><div class=\"counts\">");
            foreach (var (text, kind) in p.Counts) sb.Append(Badge(text, kind));
            sb.Append("</div>\n");
            foreach (var s in p.Sections)
            {
                sb.Append($"<h3 style=\"--c:{Hex(Kinds.Color(s.Kind))}\">").Append(Esc(s.Title)).Append("</h3><ul>\n");
                foreach (var it in s.Items)
                {
                    sb.Append(it.Done ? "<li class=\"done\">" : "<li>");
                    sb.Append("<span class=\"t\">").Append(Esc(it.Time)).Append("</span>");
                    if (it.Extra != null) sb.Append("<span class=\"x\">").Append(Esc(it.Extra)).Append("</span>");
                    if (!string.IsNullOrEmpty(it.Text)) sb.Append("<span class=\"txt\">").Append(Esc(it.Text)).Append("</span> ");
                    foreach (var t in it.Tags) sb.Append(Badge(t, s.Kind));
                    if (it.Warning != null) sb.Append("<span class=\"warn\">⚠ ").Append(Esc(it.Warning)).Append("</span>");
                    if (it.Shot != null || it.Clip != null)
                    {
                        sb.Append("<div class=\"media\">");
                        if (it.Shot != null)
                        {
                            var url = Esc(Url("ss/", it.Shot));
                            sb.Append($"<a href=\"{url}\" target=\"_blank\"><img src=\"{url}\" loading=\"lazy\" alt=\"{Esc(it.Time)}\"></a>");
                        }
                        if (it.Clip != null) sb.Append($"<video controls preload=\"none\" src=\"{Esc(Url("ss/", it.Clip))}\"></video>");
                        sb.Append("</div>");
                    }
                    if (it.ShotMissing != null) sb.Append("<div class=\"miss\">SS bulunamadı: ").Append(Esc(it.ShotMissing)).Append("</div>");
                    if (it.ClipMissing != null) sb.Append("<div class=\"miss\">Klip bulunamadı: ").Append(Esc(it.ClipMissing)).Append("</div>");
                    sb.Append("</li>\n");
                }
                sb.Append("</ul>");
                if (s.Footer != null) sb.Append("<div class=\"foot\">").Append(Esc(s.Footer)).Append("</div>");
                sb.Append('\n');
            }
            sb.Append("</section>\n");
        }
        sb.Append("</main></body></html>\n");
        return sb.ToString();
    }

    static string Badge(string text, string kind) =>
        $"<span class=\"b\" style=\"--c:{Hex(ControlPaint.Light(Kinds.Color(kind), 0.4f))}\">{Esc(text)}</span>";

    // ---------- CSV (Turkish Excel: ';' separator, UTF-8 with BOM, decimal comma) ----------

    public static readonly Encoding CsvEncoding = new UTF8Encoding(true);

    public static string Csv(IEnumerable<Note> notes)
    {
        static string Q(string s)
        {
            s ??= "";
            // Keep Excel from reading note text as a formula.
            if (s.Length > 0 && s[0] is '=' or '+' or '@' or '-' && !double.TryParse(s, NumberStyles.Float, Tr, out _)) s = " " + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder();
        sb.Append(string.Join(";", new[] { "video", "saniye", "zaman", "tür", "durum", "önem", "bitiş", "not", "ss", "klip" }.Select(Q))).Append("\r\n");
        foreach (var n in notes.OrderBy(n => n.Video, StringComparer.Ordinal).ThenBy(n => n.T))
        {
            bool game = !Kinds.IsEdit(n.Kind);
            sb.Append(string.Join(";", new[]
            {
                n.Video, n.T.ToString("0.00", Tr), NoteStore.Fmt(n.T), Kinds.Label(n.Kind), game ? Statuses.Label(n.Status) : "",
                Severities.Label(n.Severity), n.End is double e ? NoteStore.Fmt(e) : "", n.Text, NoteStore.ResolveShot(n.Shot) ?? n.Shot ?? "", n.Clip ?? "",
            }.Select(Q))).Append("\r\n");
        }
        return sb.ToString();
    }

    // ---------- DaVinci Resolve marker EDL (Timeline > Import > Timeline Markers from EDL) ----------

    static string ResolveColor(string kind) => kind switch
    {
        "BUG" => "ResolveColorRed",
        "DEGISIKLIK" => "ResolveColorYellow",
        "NOT" => "ResolveColorBlue",
        "BIP" => "ResolveColorPurple",
        "KES_BASLA" or "KES_BITIR" => "ResolveColorGreen",
        _ => "ResolveColorCream",
    };

    public static string ResolveEdl(IEnumerable<Note> notes, double fps, string title)
    {
        if (double.IsNaN(fps) || fps < 1) fps = 30;
        int tcBase = (int)Math.Round(fps);
        static string Name(string s) => NoteStore.Clean(s).Replace('|', '/');
        string Tc(long f) => string.Format(Inv, "{0:00}:{1:00}:{2:00}:{3:00}", f / tcBase / 3600, f / tcBase / 60 % 60, f / tcBase % 60, f % tcBase);
        string Label(string kind, string text) => Kinds.Label(kind) + (string.IsNullOrWhiteSpace(text) ? "" : ": " + text);

        var list = notes.ToList();
        var markers = new List<(double T, double Length, string Color, string Name)>();
        foreach (var n in list.Where(n => n.Kind is not ("KES_BASLA" or "KES_BITIR")))
            markers.Add((n.T, n.Kind == "BIP" && n.End is double e && e > n.T ? e - n.T : 0, ResolveColor(n.Kind), Label(n.Kind, n.Text)));
        foreach (var (s, e) in Kinds.Cuts(list))
        {
            var text = string.Join(" / ", new[] { s?.Text, e?.Text }.Where(t => !string.IsNullOrEmpty(t)));
            if (s != null && e != null && e.T > s.T) markers.Add((s.T, e.T - s.T, ResolveColor("KES_BASLA"), "KES" + (text.Length > 0 ? ": " + text : "")));
            else markers.Add(((s ?? e).T, 0, ResolveColor("KES_BASLA"), (s != null ? "KES başı (bitişi yok)" : "KES sonu (başı yok)") + (text.Length > 0 ? ": " + text : "")));
        }

        var sb = new StringBuilder();
        sb.Append("TITLE: ").Append(Name(title)).Append("\r\nFCM: NON-DROP FRAME\r\n\r\n");
        int i = 0;
        foreach (var m in markers.OrderBy(m => m.T))
        {
            long f = (long)Math.Round(Math.Max(0, m.T) * fps) + 3600L * tcBase;
            long d = Math.Max(1, (long)Math.Round(m.Length * fps));
            sb.AppendFormat(Inv, "{0:000}  001      V     C        {1} {2} {1} {2}  \r\n", ++i, Tc(f), Tc(f + 1));
            sb.Append(" |C:").Append(m.Color).Append(" |M:").Append(Name(m.Name)).Append(" |D:").Append(d.ToString(Inv)).Append("\r\n\r\n");
        }
        return sb.ToString();
    }

    // ---------- Task list for AI coding agents and teammates ----------

    static int SeverityRank(string s) => s switch { "KRITIK" => 0, "ORTA" => 1, "DUSUK" => 2, _ => 3 };

    // One task. mediaPrefix: relative link prefix for ss files (e.g. "ss/" in the ZIP, "../ss/" next to cikti\);
    // null for the clipboard, where only absolute paths make sense.
    public static string TaskText(Note n, string mediaPrefix, int level = 2)
    {
        var sb = new StringBuilder();
        var text = n.Text.Length > 0 ? n.Text : "(açıklama yok)";
        sb.Append(new string('#', level)).Append(" [").Append(Kinds.Label(n.Kind)).Append("] ").Append(Md(text)).Append("\n\n");
        sb.Append("- **Video:** ").Append(Md(n.Video)).Append(" — **Zaman:** ").Append(NoteStore.Fmt(n.T))
            .Append(" (").Append(n.T.ToString("0.0", Inv)).Append(" sn)\n");
        sb.Append("- **Önem:** ").Append(n.Severity != null ? Severities.Label(n.Severity) : "belirtilmedi")
            .Append(" · **Durum:** ").Append(Statuses.Label(n.Status)).Append('\n');
        var build = ReviewStore.Build(n.Video);
        var tester = ReviewStore.Tester(n.Video);
        if (build.Length > 0 || tester.Length > 0)
            sb.Append("- ").Append(string.Join(" · ", new[] { build.Length > 0 ? "**Build:** " + Md(build) : null, tester.Length > 0 ? "**Testçi:** " + Md(tester) : null }.Where(x => x != null))).Append('\n');
        var shot = NoteStore.ResolveShot(n.Shot);
        var clip = ClipFile(n);
        var absolute = new List<string>();
        if (shot != null)
        {
            sb.Append("- **Ekran görüntüsü:** ");
            sb.Append(mediaPrefix != null ? $"[{Md(shot)}]({Url(mediaPrefix, shot)})" : "`" + shot + "`");
            sb.Append(" (kırmızı çerçeve varsa sorunlu alanı gösterir)\n");
            absolute.Add(Path.Combine(Paths.ShotsDir, shot));
        }
        else if (n.Shot != null) sb.Append("- **Ekran görüntüsü:** bulunamadı (").Append(Md(n.Shot)).Append(")\n");
        if (clip != null)
        {
            sb.Append("- **Klip:** ").Append(mediaPrefix != null ? $"[{Md(clip)}]({Url(mediaPrefix, clip)})" : "`" + clip + "`").Append('\n');
            absolute.Add(Path.Combine(Paths.ShotsDir, clip));
        }
        if (absolute.Count > 0) sb.Append("- Tam yol: ").Append(string.Join(", ", absolute.Select(a => "`" + a + "`"))).Append('\n');
        sb.Append('\n');
        if (shot != null && mediaPrefix != null) sb.Append("![").Append(NoteStore.Fmt(n.T)).Append("](").Append(Url(mediaPrefix, shot)).Append(")\n\n");
        sb.Append("**Beklenen:** _…_  \n**Gözlenen:** _…_  \n**Kabul ölçütü:** _…_\n");
        return sb.ToString();
    }

    public static string TaskList(IEnumerable<Note> notes, string title, string mediaPrefix)
    {
        var list = notes.Where(n => !Kinds.IsEdit(n.Kind))
            .OrderBy(n => Array.IndexOf(GameKinds, n.Kind) is var i && i >= 0 ? i : 9)
            .ThenBy(n => SeverityRank(n.Severity)).ThenBy(n => n.Video, StringComparer.Ordinal).ThenBy(n => n.T).ToList();
        var sb = new StringBuilder();
        sb.Append("# Görev listesi — ").Append(Md(title)).Append("\n\n");
        sb.Append("Oluşturuldu: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)).Append(" · ").Append(list.Count).Append(" görev (")
            .Append(list.Count(n => n.Kind == "BUG")).Append(" bug, ").Append(list.Count(n => n.Kind == "DEGISIKLIK")).Append(" değişiklik, ")
            .Append(list.Count(n => n.Kind == "NOT")).Append(" not)\n\n");
        sb.Append("Oyun testi kayıtlarından KareNot ile çıkarıldı. Her görev videodaki bir ana bağlıdır ve tek başına ele alınabilir; ")
            .Append("sıra: buglar, değişiklikler, notlar, her grupta önce kritik olanlar. Ekran görüntülerindeki kırmızı çerçeve sorunlu alanı gösterir. ")
            .Append("**Beklenen / Gözlenen / Kabul ölçütü** satırlarını doldurup görevi verebilirsin.\n\n");
        foreach (var n in list) sb.Append("---\n\n").Append(TaskText(n, mediaPrefix)).Append('\n');
        if (list.Count == 0) sb.Append("_Açık görev yok._\n");
        return sb.ToString();
    }

    // ---------- Editing notes for a human or AI video editor ----------

    public sealed record EditVideo(string Video, string Path, double? Length, double? Fps, IReadOnlyList<Note> Notes, double BeepDefault);

    static double R(double v) => Math.Round(v, 3);
    static string Abs(string file) => file == null ? null : System.IO.Path.Combine(Paths.ShotsDir, file);

    public static string EditJson(EditVideo v)
    {
        var notes = v.Notes.OrderBy(n => n.T).ToList();
        var bleeps = new JsonArray();
        foreach (var n in notes.Where(n => n.Kind == "BIP"))
        {
            var end = n.End is double e && e > n.T ? e : n.T + v.BeepDefault;
            bleeps.Add(new JsonObject { ["start"] = R(n.T), ["end"] = R(end), ["text"] = n.Text });
        }
        var cuts = new JsonArray();
        var unpaired = new JsonArray();
        foreach (var (s, e) in Kinds.Cuts(notes))
        {
            if (s != null && e != null && e.T > s.T)
                cuts.Add(new JsonObject
                {
                    ["start"] = R(s.T), ["end"] = R(e.T), ["duration"] = R(e.T - s.T),
                    ["text"] = string.Join(" / ", new[] { s.Text, e.Text }.Where(t => !string.IsNullOrEmpty(t))),
                });
            else
            {
                var n = s ?? e;
                unpaired.Add(new JsonObject { ["t"] = R(n.T), ["kind"] = n.Kind, ["problem"] = s != null ? "bitişi yok" : "başı yok", ["text"] = n.Text });
            }
        }
        var game = new JsonArray();
        foreach (var n in notes.Where(n => !Kinds.IsEdit(n.Kind)))
            game.Add(new JsonObject
            {
                ["t"] = R(n.T), ["time"] = NoteStore.Fmt(n.T), ["kind"] = n.Kind, ["text"] = n.Text, ["status"] = n.Status ?? "ACIK", ["severity"] = n.Severity,
                ["screenshot"] = Abs(NoteStore.ResolveShot(n.Shot)), ["clip"] = Abs(ClipFile(n)),
            });
        var root = new JsonObject
        {
            ["video"] = v.Video,
            ["path"] = v.Path,
            ["length_s"] = v.Length is double l ? R(l) : null,
            ["fps"] = v.Fps is double f ? R(f) : null,
            ["build"] = ReviewStore.Build(v.Video),
            ["tester"] = ReviewStore.Tester(v.Video),
            ["created"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv),
            ["time_unit"] = "seconds from the start of the video",
            ["bleeps"] = bleeps,
            ["cuts"] = cuts,
            ["cut_total_s"] = R(Ffmpeg.Merge(Kinds.CutRanges(notes)).Sum(r => r.End - r.Start)),
            ["unpaired_cut_marks"] = unpaired,
            ["notes"] = game,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    public static string EditMarkdown(EditVideo v)
    {
        var notes = v.Notes.OrderBy(n => n.T).ToList();
        var sb = new StringBuilder();
        sb.Append("# Kurgu notları — ").Append(Md(v.Video)).Append("\n\n");
        sb.Append("- **Dosya:** `").Append(v.Path ?? v.Video).Append("`\n");
        if (v.Length is double l) sb.Append("- **Süre:** ").Append(NoteStore.Fmt(l)).Append('\n');
        if (v.Fps is double f) sb.Append("- **Kare hızı:** ").Append(f.ToString("0.##", Inv)).Append(" fps\n");
        var build = ReviewStore.Build(v.Video);
        var tester = ReviewStore.Tester(v.Video);
        if (build.Length > 0) sb.Append("- **Build:** ").Append(Md(build)).Append('\n');
        if (tester.Length > 0) sb.Append("- **Testçi:** ").Append(Md(tester)).Append('\n');
        sb.Append("\nZamanlar videonun başından itibaren (ss:dd:ss.s). Aynı bilgiler makine için `.json` dosyasında.\n\n");

        var bips = notes.Where(n => n.Kind == "BIP").ToList();
        sb.Append("## Bipler (küfür) — ").Append(bips.Count).Append("\n\n");
        if (bips.Count > 0)
        {
            sb.Append("| Başlangıç | Bitiş | Not |\n|---|---|---|\n");
            foreach (var n in bips)
            {
                var end = n.End is double e && e > n.T ? e : n.T + v.BeepDefault;
                sb.Append("| ").Append(NoteStore.Fmt(n.T)).Append(" | ").Append(NoteStore.Fmt(end)).Append(n.End == null ? " (varsayılan)" : "").Append(" | ").Append(Md(n.Text)).Append(" |\n");
            }
            sb.Append('\n');
        }
        var cuts = Kinds.Cuts(notes);
        var pairs = cuts.Where(c => c.Start != null && c.End != null && c.End.T > c.Start.T).ToList();
        var bad = cuts.Except(pairs).ToList();
        sb.Append("## Kesimler — ").Append(pairs.Count).Append(" (toplam ")
            .Append(Duration(Ffmpeg.Merge(pairs.Select(c => (c.Start.T, c.End.T))).Sum(r => r.End - r.Start))).Append(")\n\n");
        if (pairs.Count > 0)
        {
            sb.Append("| Başlangıç | Bitiş | Süre | Not |\n|---|---|---|---|\n");
            foreach (var (s, e) in pairs)
                sb.Append("| ").Append(NoteStore.Fmt(s.T)).Append(" | ").Append(NoteStore.Fmt(e.T)).Append(" | ").Append(Duration(e.T - s.T)).Append(" | ")
                    .Append(Md(string.Join(" / ", new[] { s.Text, e.Text }.Where(t => !string.IsNullOrEmpty(t))))).Append(" |\n");
            sb.Append('\n');
        }
        if (bad.Count > 0)
        {
            sb.Append("### Eşi olmayan kesim işaretleri\n\n");
            foreach (var (s, e) in bad)
                sb.Append("- **").Append(NoteStore.Fmt((s ?? e).T)).Append("** — ⚠ ").Append(s != null ? "kesim başı, bitişi yok" : "kesim sonu, başı yok")
                    .Append((s ?? e).Text.Length > 0 ? " — " + Md((s ?? e).Text) : "").Append('\n');
            sb.Append('\n');
        }
        var game = notes.Where(n => !Kinds.IsEdit(n.Kind)).ToList();
        if (game.Count > 0)
        {
            sb.Append("## Oyun testi notları (bağlam)\n\n");
            foreach (var n in game)
            {
                sb.Append("- **").Append(NoteStore.Fmt(n.T)).Append("** ").Append(Kinds.Label(n.Kind)).Append(" — ").Append(Md(n.Text.Length > 0 ? n.Text : "(açıklama yok)"));
                foreach (var t in Tags(n)) sb.Append(" `").Append(t).Append('`');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }
}
