using System.Globalization;

// Several classes under test read the process-wide data folder (Paths.Root via VIDEONOTLARI_DATA) and the shared
// ReviewStore cache, so the tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace KareNot.Tests;

// A throw-away data folder: Paths.Root becomes <temp>\KareNot for the lifetime of the object.
sealed class TempData : IDisposable
{
    readonly string previous = Environment.GetEnvironmentVariable("VIDEONOTLARI_DATA");
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "karenot_test_" + Guid.NewGuid().ToString("N"));
    public string Root => Path.Combine(Dir, "KareNot");

    public TempData()
    {
        Directory.CreateDirectory(Dir);
        Environment.SetEnvironmentVariable("VIDEONOTLARI_DATA", Dir);
        ReviewStore.Reload();
    }

    public string Write(string relative, string text)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("VIDEONOTLARI_DATA", previous);
        ReviewStore.Reload();
        try { Directory.Delete(Dir, true); } catch { }
    }
}

static class N
{
    public static Note Make(double t, string kind, string text = "", string video = "oyun.mp4", double? end = null) =>
        new() { T = t, Kind = kind, Text = text, Video = video, End = end };
}

// Runs a test body under a culture whose decimal separator is a comma, to catch culture-dependent formatting.
static class Culture
{
    public static void Turkish(Action body)
    {
        var (c, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
        try { body(); }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (c, ui); }
    }
}
