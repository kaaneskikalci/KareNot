using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KareNot;

readonly record struct WhisperWord(double Start, double End, string Text);

// Offline speech recognition with an installed whisper.cpp whisper-cli.exe, and word-level parsing of its JSON output.
static partial class Whisper
{
    // Transcribes a 16 kHz WAV; returns the JSON text written by whisper-cli. progress gets 0..100.
    public static async Task<string> Transcribe(string exe, string model, string wav, string language, Action<int> progress, CancellationToken cancel)
    {
        var outBase = Path.Combine(Path.GetTempPath(), "karenot_whisper_" + Guid.NewGuid().ToString("N"));
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe),
        };
        // -ml 1 -sow: one segment per word (word-level timestamps); -oj -of: JSON next to outBase; -pp: progress on stderr.
        foreach (var a in new[] { "-m", model, "-f", wav, "-l", language, "-ml", "1", "-sow", "-oj", "-of", outBase, "-pp",
            "-t", Math.Clamp(Environment.ProcessorCount, 1, 8).ToString(CultureInfo.InvariantCulture) })
            psi.ArgumentList.Add(a);
        var json = outBase + ".json";
        try
        {
            using var p = Process.Start(psi);
            var tail = new Queue<string>();
            var stdout = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            using var reg = cancel.Register(() => { try { p.Kill(true); } catch { } });
            string line;
            while ((line = await p.StandardError.ReadLineAsync()) != null)
            {
                var m = ProgressRegex().Match(line);
                if (m.Success) progress?.Invoke(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                else if (line.Trim().Length > 0)
                {
                    tail.Enqueue(line.Trim());
                    if (tail.Count > 3) tail.Dequeue();
                }
            }
            await p.WaitForExitAsync(CancellationToken.None);
            try { await stdout; } catch { }
            cancel.ThrowIfCancellationRequested();
            if (p.ExitCode != 0 || !File.Exists(json))
                throw new InvalidOperationException($"whisper-cli hata verdi (çıkış kodu {p.ExitCode}): " + string.Join(" / ", tail));
            return File.ReadAllText(json, Encoding.UTF8);
        }
        finally
        {
            try { File.Delete(json); } catch { }
        }
    }

    [GeneratedRegex(@"progress\s*=\s*(\d+)\s*%")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"""offsets""\s*:\s*\{\s*""from""\s*:\s*(\d+)\s*,\s*""to""\s*:\s*(\d+)\s*\}\s*,\s*""text""\s*:\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex SegmentRegex();

    // Words with times in seconds. Reads whisper-cli JSON ("transcription": offsets in ms, optional "tokens" from -ojf)
    // and OpenAI-style JSON ("segments" with start/end seconds, optional "words"). Segments with several words get
    // their time split by token times when present, else in proportion to the word lengths. Never throws.
    public static List<WhisperWord> ParseWords(string json)
    {
        var words = new List<WhisperWord>();
        if (string.IsNullOrWhiteSpace(json)) return words;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                (root.TryGetProperty("transcription", out var segs) || root.TryGetProperty("segments", out segs)) && segs.ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in segs.EnumerateArray()) AddSegment(seg, words);
                return words;
            }
        }
        catch (Exception) { words.Clear(); }

        // Malformed JSON (whisper.cpp does not escape every control character): scan the segments directly.
        foreach (Match m in SegmentRegex().Matches(json))
        {
            string text;
            try { text = JsonSerializer.Deserialize<string>("\"" + m.Groups[3].Value + "\""); } catch { text = m.Groups[3].Value; }
            Split(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 1000, double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 1000, text, words);
        }
        return words;
    }

    static void AddSegment(JsonElement seg, List<WhisperWord> words)
    {
        if (seg.ValueKind != JsonValueKind.Object) return;
        if (seg.TryGetProperty("words", out var ws) && ws.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in ws.EnumerateArray())
                if (Times(w, out var a, out var b) && (Str(w, "word") ?? Str(w, "text")) is { } t) Split(a, b, t, words);
            return;
        }
        if (!Times(seg, out var start, out var end)) return;
        var text = Str(seg, "text") ?? "";
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1 && seg.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Array)
        {
            // Tokens starting with a space begin a new word; special tokens look like [_BEG_] or [_TT_123].
            var before = words.Count;
            string current = null;
            double ws0 = 0, we0 = 0;
            foreach (var tok in tokens.EnumerateArray())
            {
                var tt = Str(tok, "text");
                if (tt == null || tt.StartsWith("[_") || !Times(tok, out var a, out var b)) continue;
                if (current == null || tt.StartsWith(' '))
                {
                    if (current != null) Split(ws0, we0, current, words);
                    current = tt;
                    ws0 = a;
                }
                else current += tt;
                we0 = b;
            }
            if (current != null) Split(ws0, we0, current, words);
            if (words.Count > before) return;
        }
        Split(start, end, text, words);
    }

    // Adds the words of a text, spreading start..end over them by length.
    static void Split(double start, double end, string text, List<WhisperWord> words)
    {
        var parts = (text ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        if (end < start) end = start;
        double total = parts.Sum(p => p.Length), pos = start;
        foreach (var p in parts)
        {
            double len = (end - start) * p.Length / total;
            words.Add(new WhisperWord(pos, pos + len, p));
            pos += len;
        }
    }

    // Start and end in seconds from offsets (ms), start/end (s) or timestamps ("00:00:01,230").
    static bool Times(JsonElement e, out double start, out double end)
    {
        start = end = 0;
        if (e.ValueKind != JsonValueKind.Object) return false;
        if (e.TryGetProperty("offsets", out var o) && Num(o, "from") is double f && Num(o, "to") is double t)
        {
            (start, end) = (f / 1000, t / 1000);
            return true;
        }
        if (Num(e, "start") is double s && Num(e, "end") is double en)
        {
            (start, end) = (s, en);
            return true;
        }
        if (e.TryGetProperty("timestamps", out var ts) && Stamp(Str(ts, "from")) is double sf && Stamp(Str(ts, "to")) is double st)
        {
            (start, end) = (sf, st);
            return true;
        }
        return false;
    }

    static double? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Stamp(string s) =>
        s != null && TimeSpan.TryParse(s.Replace(',', '.'), CultureInfo.InvariantCulture, out var t) ? t.TotalSeconds : null;

    static readonly CultureInfo Turkish = new("tr-TR");

    // Lowercase (Turkish rules) letters and digits only.
    public static string Normalize(string word) =>
        new string((word ?? "").ToLower(Turkish).Where(char.IsLetterOrDigit).ToArray());

    // Words starting with one of the list entries (already lowercase); Text is the word without surrounding punctuation.
    public static List<WhisperWord> Candidates(IEnumerable<WhisperWord> words, IEnumerable<string> profanity)
    {
        var list = profanity.Select(Normalize).Where(p => p.Length > 0).Distinct().ToList();
        var result = new List<WhisperWord>();
        foreach (var w in words)
        {
            var n = Normalize(w.Text);
            if (n.Length == 0 || !list.Any(p => n.StartsWith(p, StringComparison.Ordinal))) continue;
            int a = 0, b = w.Text.Length - 1;
            while (!char.IsLetterOrDigit(w.Text[a])) a++;
            while (!char.IsLetterOrDigit(w.Text[b])) b--;
            result.Add(w with { Text = w.Text[a..(b + 1)] });
        }
        return result;
    }

    const string SampleJson = """
        {
          "systeminfo": "AVX = 1",
          "model": { "type": "small", "multilingual": true },
          "params": { "model": "ggml-small.bin", "language": "tr", "translate": false },
          "result": { "language": "tr" },
          "transcription": [
            { "timestamps": { "from": "00:00:01,000", "to": "00:00:01,400" }, "offsets": { "from": 1000, "to": 1400 }, "text": " Abi" },
            { "timestamps": { "from": "00:00:01,400", "to": "00:00:01,900" }, "offsets": { "from": 1400, "to": 1900 }, "text": " AMK," },
            { "timestamps": { "from": "00:00:02,000", "to": "00:00:02,600" }, "offsets": { "from": 2000, "to": 2600 }, "text": " \"Siktir\"" },
            { "timestamps": { "from": "00:00:03,000", "to": "00:00:04,000" }, "offsets": { "from": 3000, "to": 4000 }, "text": " İyi oyun." },
            { "timestamps": { "from": "00:00:04,000", "to": "00:00:04,000" }, "offsets": { "from": 4000, "to": 4000 }, "text": "" }
          ]
        }
        """;

    // Pure check of the parser and the matcher for the self test.
    public static bool SelfCheck()
    {
        try
        {
            var words = ParseWords(SampleJson);
            var hits = Candidates(words, new[] { "amk", "sik", "piç" });
            var broken = ParseWords(SampleJson.Replace(" Abi", " A\tbi"));
            return words.Count == 5
                && Math.Abs(words[1].Start - 1.4) < 1e-9 && Math.Abs(words[1].End - 1.9) < 1e-9
                && words[3].Text == "İyi" && Math.Abs(words[3].End - 3.375) < 1e-9 && Math.Abs(words[4].Start - 3.375) < 1e-9
                && hits.Count == 2 && hits[0].Text == "AMK" && hits[1].Text == "Siktir" && Math.Abs(hits[1].Start - 2.0) < 1e-9
                && Normalize("IŞIK!") == "ışık"
                && broken.Count == 6 && broken[0].Text == "A" && broken[1].Text == "bi" && broken[2].Text == "AMK," && Math.Abs(broken[2].Start - 1.4) < 1e-9
                && ParseWords("").Count == 0 && ParseWords("{ nonsense").Count == 0
                && ParseWords("""{"segments":[{"start":0.5,"end":1.0,"text":" amk"}]}""")[0].Start == 0.5;
        }
        catch (Exception) { return false; }
    }
}
