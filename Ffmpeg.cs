using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KareNot;

// Thin wrapper around an installed ffmpeg.exe (settings path, else PATH). Used for clips, exports, previews and analysis.
static partial class Ffmpeg
{
    public static string PathOverride;

    public static string Find()
    {
        if (!string.IsNullOrWhiteSpace(PathOverride) && File.Exists(PathOverride)) return PathOverride;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (dir.Trim().Length > 0 && File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    public const string Missing = "ffmpeg bulunamadı. Kur (winget install Gyan.FFmpeg) ya da Ayarlar'dan ffmpeg.exe yolunu seç.";

    [GeneratedRegex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex TimeRegex();

    // Runs ffmpeg; progress gets the processed media time in seconds (parsed from stderr).
    public static async Task<(int Exit, string Log)> Run(IEnumerable<string> args, Action<double> progress = null, CancellationToken cancel = default)
    {
        var exe = Find() ?? throw new InvalidOperationException(Missing);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-nostdin");
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        var log = new StringBuilder();
        var stdout = p.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancel);
        using var reg = cancel.Register(() => { try { p.Kill(true); } catch { } });
        string line;
        while ((line = await ReadLine(p.StandardError)) != null)
        {
            log.AppendLine(line);
            var m = TimeRegex().Match(line);
            if (m.Success && progress != null)
                progress(int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
        }
        await p.WaitForExitAsync(CancellationToken.None);
        try { await stdout; } catch { }
        cancel.ThrowIfCancellationRequested();
        return (p.ExitCode, log.ToString());
    }

    // ffmpeg ends progress lines with \r; treat \r and \n both as line ends.
    static async Task<string> ReadLine(StreamReader r)
    {
        var sb = new StringBuilder();
        var buf = new char[1];
        while (await r.ReadAsync(buf, 0, 1) == 1)
        {
            if (buf[0] == '\r' || buf[0] == '\n')
            {
                if (sb.Length > 0) return sb.ToString();
                continue;
            }
            sb.Append(buf[0]);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    static string S(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // Short H.264 clip around a moment.
    public static Task<(int Exit, string Log)> Clip(string video, double start, double duration, string output, CancellationToken cancel = default) =>
        Run(new[] { "-y", "-ss", S(Math.Max(0, start)), "-i", video, "-t", S(duration), "-map", "0:v:0", "-map", "0:a:0?",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k",
            "-movflags", "+faststart", output }, null, cancel);

    // One small frame as PNG (seek bar preview); null on failure.
    public static Bitmap Frame(string video, double t, int width)
    {
        var exe = Find();
        if (exe == null) return null;
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-ss", S(Math.Max(0, t)), "-i", video, "-frames:v", "1",
            "-vf", "scale=" + width + ":-2", "-f", "image2pipe", "-vcodec", "png", "-" })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            var err = p.StandardError.ReadToEndAsync();
            using var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch { } return null; }
            if (ms.Length == 0) return null;
            ms.Position = 0;
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?) fps")]
    private static partial Regex FpsRegex();

    public static async Task<double> FrameRate(string video)
    {
        var (_, log) = await Run(new[] { "-i", video });
        var m = FpsRegex().Match(log);
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 30;
    }

    [GeneratedRegex(@"silence_(start|end): (-?\d+(?:\.\d+)?)")]
    private static partial Regex SilenceRegex();

    [GeneratedRegex(@"freeze_(start|end): (-?\d+(?:\.\d+)?)")]
    private static partial Regex FreezeRegex();

    // Long silent or frozen stretches, merged, each at least minSeconds long.
    public static async Task<List<(double Start, double End)>> DeadAir(string video, double minSeconds, double length, Action<double> progress, CancellationToken cancel)
    {
        var ranges = new List<(double, double)>();
        var (_, silence) = await Run(new[] { "-i", video, "-vn", "-af", "silencedetect=n=-45dB:d=" + S(minSeconds), "-f", "null", "-" }, t => progress?.Invoke(t / 2), cancel);
        ranges.AddRange(Pairs(SilenceRegex().Matches(silence), length));
        var (_, freeze) = await Run(new[] { "-i", video, "-an", "-vf", "freezedetect=n=-60dB:d=" + S(minSeconds), "-f", "null", "-" }, t => progress?.Invoke(length / 2 + t / 2), cancel);
        ranges.AddRange(Pairs(FreezeRegex().Matches(freeze), length));
        return Merge(ranges).Where(r => r.Item2 - r.Item1 >= minSeconds).ToList();
    }

    static IEnumerable<(double, double)> Pairs(MatchCollection matches, double length)
    {
        double? start = null;
        foreach (Match m in matches)
        {
            var v = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (m.Groups[1].Value == "start") start = Math.Max(0, v);
            else if (start != null) { yield return (start.Value, v); start = null; }
        }
        if (start != null && length > start) yield return (start.Value, length);
    }

    public static List<(double Start, double End)> Merge(IEnumerable<(double Start, double End)> ranges)
    {
        var result = new List<(double Start, double End)>();
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (result.Count > 0 && r.Start <= result[^1].End) result[^1] = (result[^1].Start, Math.Max(result[^1].End, r.End));
            else result.Add(r);
        }
        return result;
    }

    // ffmpeg expression that is true inside any of the ranges.
    static string Between(IEnumerable<(double Start, double End)> ranges) =>
        string.Join("+", ranges.Select(r => $"between(t,{S(r.Start)},{S(r.End)})"));

    // Copy of the video with bleeps over the BIP ranges and the cut ranges removed.
    public static Task<(int Exit, string Log)> ApplyEdits(string video, bool hasAudio, List<(double Start, double End)> beeps,
        List<(double Start, double End)> cuts, double length, string output, Action<double> progress, CancellationToken cancel)
    {
        var filters = new List<string>();
        string v = "0:v:0", a = hasAudio ? "0:a:0" : null;
        if (a != null && beeps.Count > 0)
        {
            filters.Add($"[{a}]volume=0:enable='{Between(beeps)}'[muted]");
            filters.Add($"sine=frequency=1000:sample_rate=48000:duration={S(length)},volume=0.25,volume=0:enable='not({Between(beeps)})'[beep]");
            filters.Add("[muted][beep]amix=inputs=2:duration=first:normalize=0[ab]");
            a = "ab";
        }
        if (cuts.Count > 0)
        {
            filters.Add($"[{v}]select='not({Between(cuts)})',setpts=N/FRAME_RATE/TB[vc]");
            v = "vc";
            if (a != null)
            {
                filters.Add($"[{a}]aselect='not({Between(cuts)})',asetpts=N/SR/TB[ac]");
                a = "ac";
            }
        }
        var args = new List<string> { "-y", "-i", video };
        if (filters.Count > 0) args.AddRange(new[] { "-filter_complex", string.Join(";", filters) });
        args.AddRange(new[] { "-map", v.Contains(':') ? v : $"[{v}]" });
        if (a != null) args.AddRange(new[] { "-map", a.Contains(':') ? a : $"[{a}]" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p" });
        if (a != null) args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
        args.AddRange(new[] { "-movflags", "+faststart", output });
        return Run(args, progress, cancel);
    }

    public static async Task<bool> HasAudio(string video)
    {
        var (_, log) = await Run(new[] { "-i", video });
        return log.Contains("Audio:");
    }

    // 16 kHz mono WAV for speech recognition.
    public static Task<(int Exit, string Log)> ExtractSpeechAudio(string video, string wav, Action<double> progress, CancellationToken cancel) =>
        Run(new[] { "-y", "-i", video, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav }, progress, cancel);

    // ---------- Export additions ----------

    // ffmpeg.exe on PATH only (ignores PathOverride); for the settings dialog hint.
    public static string FindOnPath()
    {
        var keep = PathOverride;
        try { PathOverride = null; return Find(); }
        finally { PathOverride = keep; }
    }

    [GeneratedRegex(@"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    // Container duration in seconds, or null.
    public static async Task<double?> Duration(string video)
    {
        var (_, log) = await Run(new[] { "-i", video });
        var m = DurationRegex().Match(log);
        return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null;
    }

    // Kept parts of [0, length) outside the cut ranges; slivers shorter than 50 ms are dropped.
    public static List<(double Start, double? End)> KeptParts(List<(double Start, double End)> cuts, double length)
    {
        var keep = new List<(double Start, double? End)>();
        double pos = 0;
        foreach (var c in Merge(cuts.Select(c => (Math.Max(0, c.Start), length > 0 ? Math.Min(length, c.End) : c.End)).Where(c => c.Item2 > c.Item1)))
        {
            if (c.Start - pos >= 0.05) keep.Add((pos, c.Start));
            pos = c.End;
        }
        if (length <= 0 || length - pos >= 0.05) keep.Add((pos, null));
        return keep;
    }

    // Like ApplyEdits, but verified with ffmpeg 8.1 and exact: the kept parts are trimmed (trim/atrim) and concatenated, so
    // video and audio stay in sync and frame timing is kept. ApplyEdits' select + setpts=N/FRAME_RATE/TB drops one extra
    // frame per cut (between() is inclusive) and assumes a constant frame rate, and aselect only cuts at whole audio frames,
    // so audio drifts against video with every cut. The bleep is also louder: sine is generated at -18 dBFS, so the old
    // volume=0.25 gave about -30 dBFS, barely audible next to game audio; volume=2 gives about -12 dBFS.
    public static async Task<(int Exit, string Log)> ApplyEdits2(string video, bool hasAudio, List<(double Start, double End)> beeps,
        List<(double Start, double End)> cuts, double length, string output, Action<double> progress, CancellationToken cancel)
    {
        var filters = new List<string>();
        string v = "0:v:0", a = hasAudio ? "0:a:0" : null;
        if (a != null && beeps.Count > 0)
        {
            filters.Add($"[{a}]volume=0:enable='{Between(beeps)}'[muted]");
            filters.Add($"sine=frequency=1000:sample_rate=48000:duration={S(length + 1)},volume=2,volume=0:enable='not({Between(beeps)})'[beep]");
            filters.Add("[muted][beep]amix=inputs=2:duration=first:normalize=0[ab]");
            a = "ab";
        }
        if (cuts.Count > 0)
        {
            var keep = KeptParts(cuts, length);
            if (keep.Count == 0) throw new InvalidOperationException("Kesimler videonun tamamını kapsıyor.");
            int k = keep.Count;
            filters.Add($"[{v}]split={k}" + string.Concat(Enumerable.Range(0, k).Select(i => $"[v{i}]")));
            if (a != null) filters.Add($"[{a}]asplit={k}" + string.Concat(Enumerable.Range(0, k).Select(i => $"[a{i}]")));
            for (int i = 0; i < k; i++)
            {
                var range = "start=" + S(keep[i].Start) + (keep[i].End is double end ? ":end=" + S(end) : "");
                filters.Add($"[v{i}]trim={range},setpts=PTS-STARTPTS[vs{i}]");
                if (a != null) filters.Add($"[a{i}]atrim={range},asetpts=PTS-STARTPTS[as{i}]");
            }
            filters.Add(string.Concat(Enumerable.Range(0, k).Select(i => a != null ? $"[vs{i}][as{i}]" : $"[vs{i}]")) +
                $"concat=n={k}:v=1:a={(a != null ? 1 : 0)}[vc]" + (a != null ? "[ac]" : ""));
            v = "vc";
            if (a != null) a = "ac";
        }
        var args = new List<string> { "-y", "-i", video };
        string script = null;
        if (filters.Count > 0)
        {
            var graph = string.Join(";", filters);
            // Long graphs (many cuts) would overflow the command line.
            if (graph.Length > 8000)
            {
                script = Path.Combine(Path.GetTempPath(), "karenot_kurgu_" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(script, graph);
                args.AddRange(new[] { "-filter_complex_script", script });
            }
            else args.AddRange(new[] { "-filter_complex", graph });
        }
        args.AddRange(new[] { "-map", v.Contains(':') ? v : $"[{v}]" });
        if (a != null) args.AddRange(new[] { "-map", a.Contains(':') ? a : $"[{a}]" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p" });
        if (a != null) args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
        args.AddRange(new[] { "-movflags", "+faststart", output });
        try { return await Run(args, progress, cancel); }
        finally { if (script != null) try { File.Delete(script); } catch { } }
    }
}
