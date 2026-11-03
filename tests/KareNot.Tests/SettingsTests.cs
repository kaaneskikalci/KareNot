namespace KareNot.Tests;

// %APPDATA%\VideoNotlari\ayarlar.json
public class SettingsTests
{
    [Fact]
    public void Defaults()
    {
        var s = new AppSettings();
        Assert.Equal((5.0, 5.0, 3.0, 3.0, 0.6, 8.0), (s.ClipBefore, s.ClipAfter, s.LoopBefore, s.LoopAfter, s.BeepDefault, s.DeadAirMin));
        Assert.Equal("", s.DataFolder);
    }

    [Fact]
    public void Parse_reads_saved_values_and_ignores_unknown_keys()
    {
        var s = AppSettings.Parse("""{"ClipBefore":2.5,"ClipAfter":8,"FfmpegPath":"C:\\ffmpeg\\ffmpeg.exe","DataFolder":"D:\\Notlar","Gelecek":1}""");
        Assert.Equal(2.5, s.ClipBefore);
        Assert.Equal(8, s.ClipAfter);
        Assert.Equal(@"C:\ffmpeg\ffmpeg.exe", s.FfmpegPath);
        Assert.Equal(@"D:\Notlar", s.DataFolder);
        Assert.Equal(3, s.LoopBefore);   // missing keys keep their defaults
    }

    [Fact]
    public void Parse_replaces_nulls_and_nonsense_with_defaults()
    {
        // Regression: "DataFolder": null crashed the app on every start.
        var s = AppSettings.Parse("""{"DataFolder":null,"FfmpegPath":null,"WhisperPath":null,"WhisperModel":null,"ClipBefore":-4,"BeepDefault":0,"DeadAirMin":-1}""");
        Assert.Equal("", s.DataFolder);
        Assert.Equal("", s.FfmpegPath);
        Assert.Equal("", s.WhisperPath);
        Assert.Equal("", s.WhisperModel);
        Assert.Equal(5, s.ClipBefore);
        Assert.Equal(0.6, s.BeepDefault);
        Assert.Equal(8, s.DeadAirMin);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ bozuk")]
    [InlineData("[1,2]")]
    public void Parse_falls_back_to_defaults_for_unreadable_files(string json)
    {
        var s = AppSettings.Parse(json);
        Assert.Equal(5, s.ClipBefore);
        Assert.Equal("", s.DataFolder);
    }

    [Fact]
    public void Text_lists_skip_comments_and_blank_lines_and_create_the_default_file()
    {
        using var data = new TempData();
        var path = Path.Combine(data.Root, "liste.txt");
        Assert.Equal(new[] { "bir", "iki" }, TextLists.Read(path, "# yorum\nbir\n\n  iki  \n"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Profanity_list_is_lowercased_with_turkish_rules()
    {
        using var data = new TempData();
        data.Write("kufur_listesi.txt", "AMK\nIŞIK\n");
        Assert.Equal(new[] { "amk", "ışık" }, TextLists.Profanity());
    }
}
