namespace KareNot.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "00:00:00.0")]
    [InlineData(65.49, "00:01:05.4")]   // tenths are floored, like the player's clock
    [InlineData(65.4, "00:01:05.4")]
    [InlineData(59.99, "00:00:59.9")]
    [InlineData(3723.25, "01:02:03.2")]
    [InlineData(-3, "00:00:00.0")]
    [InlineData(360000, "100:00:00.0")]
    public void Fmt_formats_hours_minutes_and_floored_tenths(double seconds, string expected)
    {
        Assert.Equal(expected, NoteStore.Fmt(seconds));
    }

    [Fact]
    public void Fmt_does_not_depend_on_the_current_culture()
    {
        Culture.Turkish(() => Assert.Equal("00:00:01.5", NoteStore.Fmt(1.5)));
    }
}

public class PathTests
{
    [Theory]
    [InlineData("oyun.mp4", "oyun")]
    [InlineData("Oyun Testi 2026-10-28 21-03-11.mkv", "Oyun Testi 2026-10-28 21-03-11")]
    [InlineData("çğıöşü İĞÜ.mp4", "çğıöşü İĞÜ")]
    [InlineData("a.b.mp4", "a.b")]
    [InlineData("kötü:ad?.mp4", "kötü_ad_")]
    [InlineData("nokta. .mp4", "nokta")]
    [InlineData(".mp4", "video")]
    [InlineData("", "video")]
    [InlineData(null, "video")]
    public void FolderName_is_the_file_name_without_extension_made_safe(string video, string expected)
    {
        Assert.Equal(expected, Paths.FolderName(video));
    }

    [Fact]
    public void Videos_with_the_same_base_name_share_a_folder()
    {
        Assert.True(Paths.SameFolder("Oyun.mp4", "oyun.MKV"));
        Assert.False(Paths.SameFolder("oyun1.mp4", "oyun2.mp4"));
    }

    [Fact]
    public void Per_video_files_live_in_the_video_folder_under_the_root()
    {
        using var data = new TempData();
        Assert.Equal(Path.Combine(data.Root, "oyun", "notlar.txt"), Paths.NotesFile("oyun.mp4"));
        Assert.Equal(Path.Combine(data.Root, "oyun", "ss"), Paths.ShotsDir("oyun.mkv"));
        Assert.Equal(Path.Combine(data.Root, "oyun", "cikti"), Paths.ExportDir("oyun.mp4"));
        Assert.Equal(Path.Combine(data.Root, "cikti"), Paths.ExportDir(null));
        Assert.Equal(Path.Combine(data.Root, "inceleme.json"), Paths.ReviewFile);
    }

    [Fact]
    public void SamePath_ignores_case_and_trailing_separators()
    {
        Assert.True(Paths.SamePath(@"C:\Notlar\", @"c:\notlar"));
        Assert.False(Paths.SamePath(@"C:\Notlar", @"C:\Notlar2"));
    }

    [Fact]
    public void Nested_detects_a_folder_inside_the_other()
    {
        Assert.True(DataMove.Nested(@"C:\Notlar", @"C:\Notlar\Alt"));
        Assert.True(DataMove.Nested(@"C:\Notlar\Alt", @"C:\notlar"));
        Assert.False(DataMove.Nested(@"C:\Notlar", @"C:\Notlar2"));
    }
}
