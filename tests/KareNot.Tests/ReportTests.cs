using System.Text.Json.Nodes;

namespace KareNot.Tests;

// Export formats handed to the team, AI agents and the video editor (Reports.cs).
public class ReportTests
{
    static List<Note> Sample() => new()
    {
        new Note { T = 12.5, Kind = "BUG", Text = "Kamera *takıldı* | duvar", Video = "oyun.mp4", Severity = "KRITIK" },
        new Note { T = 30, Kind = "DEGISIKLIK", Text = "=SUM(A1)", Video = "oyun.mp4", Status = "DUZELTILDI" },
        new Note { T = 40, Kind = "NOT", Text = "", Video = "oyun.mp4", Status = "TEKRAR_TEST" },
        new Note { T = 50, Kind = "BIP", Text = "aday: amk", Video = "oyun.mp4", End = 50.8 },
        new Note { T = 60, Kind = "KES_BASLA", Text = "sessiz", Video = "oyun.mp4" },
        new Note { T = 84, Kind = "KES_BITIR", Text = "", Video = "oyun.mp4" },
        new Note { T = 90, Kind = "KES_BASLA", Text = "", Video = "oyun.mp4" },
    };

    [Theory]
    [InlineData(0, "0 sn")]
    [InlineData(24, "24 sn")]
    [InlineData(1.5, "1,5 sn")]
    [InlineData(84, "1 dk 24 sn")]
    [InlineData(3725, "1 sa 2 dk 5 sn")]
    [InlineData(-5, "0 sn")]
    public void Duration_is_human_readable_turkish(double seconds, string expected)
    {
        Assert.Equal(expected, Reports.Duration(seconds));
    }

    [Fact]
    public void IsOpen_is_true_for_open_and_retest()
    {
        Assert.True(Reports.IsOpen(new Note()));
        Assert.True(Reports.IsOpen(new Note { Status = "TEKRAR_TEST" }));
        Assert.False(Reports.IsOpen(new Note { Status = "DUZELTILDI" }));
    }

    [Fact]
    public void Csv_has_a_header_semicolons_decimal_commas_and_quoted_fields()
    {
        using var data = new TempData();
        var lines = Reports.Csv(Sample()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("\"video\";\"saniye\";\"zaman\";\"tür\";\"durum\";\"önem\";\"bitiş\";\"not\";\"ss\";\"klip\"", lines[0]);
        Assert.Equal(8, lines.Length);
        Assert.Equal("\"oyun.mp4\";\"12,50\";\"00:00:12.5\";\"BUG\";\"Açık\";\"Kritik\";\"\";\"Kamera *takıldı* | duvar\";\"\";\"\"", lines[1]);
        Assert.Contains("\"BİP\";\"\";\"\";\"00:00:50.8\";\"aday: amk\"", lines[4]);
    }

    [Fact]
    public void Csv_neutralises_spreadsheet_formulas_and_doubles_quotes()
    {
        using var data = new TempData();
        var csv = Reports.Csv(new[] { N.Make(1, "NOT", "=SUM(A1)"), N.Make(2, "NOT", "-5"), N.Make(3, "NOT", "\"alıntı\"") });
        Assert.Contains("\" =SUM(A1)\"", csv);
        Assert.Contains("\"-5\"", csv);                  // plain numbers stay numbers
        Assert.Contains("\"\"\"alıntı\"\"\"", csv);
    }

    [Fact]
    public void Resolve_edl_puts_markers_on_a_one_hour_timeline()
    {
        var edl = Reports.ResolveEdl(new[] { N.Make(1.5, "BUG", "çarptı"), N.Make(10, "BIP", "", end: 11), N.Make(20, "KES_BASLA"), N.Make(22, "KES_BITIR") }, 30, "oyun");
        var lines = edl.Split("\r\n");

        Assert.Equal("TITLE: oyun", lines[0]);
        Assert.Equal("FCM: NON-DROP FRAME", lines[1]);
        Assert.Equal("001  001      V     C        01:00:01:15 01:00:01:16 01:00:01:15 01:00:01:16  ", lines[3]);
        Assert.Equal(" |C:ResolveColorRed |M:BUG: çarptı |D:1", lines[4]);
        Assert.Contains(" |C:ResolveColorPurple |M:BİP |D:30", lines);   // 1 s bleep at 30 fps
        Assert.Contains(" |C:ResolveColorGreen |M:KES |D:60", lines);    // one marker per cut, 2 s long
    }

    [Fact]
    public void Resolve_edl_falls_back_to_30_fps_and_rounds_the_timecode_base()
    {
        var nan = Reports.ResolveEdl(new[] { N.Make(1, "NOT") }, double.NaN, "v");
        Assert.Contains("01:00:01:00", nan);
        // 100 s at 29.97 fps is frame 2997, shown in 30-frame non-drop timecode like Resolve does.
        var ntsc = Reports.ResolveEdl(new[] { N.Make(100, "NOT") }, 29.97, "v");
        Assert.Contains("01:01:39:27", ntsc);
    }

    [Fact]
    public void Markdown_report_groups_by_kind_and_escapes_markdown()
    {
        using var data = new TempData();
        var md = Reports.Markdown(Sample(), out var missing);

        Assert.Equal(0, missing);
        Assert.Contains("## oyun.mp4", md);
        Assert.Contains("### Buglar", md);
        Assert.Contains("- **00:00:12.5** — Kamera \\*takıldı\\* \\| duvar `Kritik`", md);
        Assert.Contains("- **00:00:40.0** — (açıklama yok) `Tekrar test`", md);
        Assert.Contains("- **00:01:00.0–00:01:24.0** (24 sn) — sessiz", md);
        Assert.Contains("- **00:01:30.0** — ⚠ bitişi yok", md);
        Assert.Contains("Toplam kesilen süre: 24 sn", md);
    }

    [Fact]
    public void Markdown_report_links_screenshots_relative_and_counts_missing_ones()
    {
        using var data = new TempData();
        data.Write(Path.Combine("oyun 1", "ss", "bug ğ.png"), "png");
        var notes = new[]
        {
            new Note { T = 1, Kind = "BUG", Text = "var", Video = "oyun 1.mp4", Shot = "bug ğ.png" },
            new Note { T = 2, Kind = "BUG", Text = "yok", Video = "oyun 1.mp4", Shot = "kayip.png" },
        };
        var md = Reports.Markdown(notes, out var missing);

        Assert.Equal(1, missing);
        Assert.Contains("![00:00:01.0](ss/bug%20%C4%9F.png)", md);
        Assert.Contains("(SS bulunamadı: kayip.png)", md);
        var all = Reports.Markdown(notes, out _, Reports.FolderMedia);
        Assert.Contains("](oyun%201/ss/bug%20%C4%9F.png)", all);
    }

    [Fact]
    public void Html_report_escapes_text()
    {
        using var data = new TempData();
        var html = Reports.Html(new[] { N.Make(1, "BUG", "<script>alert('x')</script> & \"q\"") });
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;q&quot;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Task_list_orders_by_kind_then_severity_and_skips_editing_marks()
    {
        using var data = new TempData();
        var notes = new[]
        {
            new Note { T = 5, Kind = "NOT", Text = "not", Video = "v.mp4" },
            new Note { T = 9, Kind = "BUG", Text = "küçük", Video = "v.mp4", Severity = "DUSUK" },
            new Note { T = 7, Kind = "BUG", Text = "büyük", Video = "v.mp4", Severity = "KRITIK" },
            new Note { T = 3, Kind = "DEGISIKLIK", Text = "değiş", Video = "v.mp4" },
            new Note { T = 1, Kind = "BIP", Text = "bip", Video = "v.mp4" },
        };
        var md = Reports.TaskList(notes, "v.mp4", _ => "../ss/");

        Assert.StartsWith("# Görev listesi — v.mp4\n", md);
        Assert.Contains("4 görev (2 bug, 1 değişiklik, 1 not)", md);
        var order = new[] { "## [BUG] büyük", "## [BUG] küçük", "## [DEĞİŞİKLİK] değiş", "## [NOT] not" }.Select(h => md.IndexOf(h, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.DoesNotContain("bip", md);
        Assert.Contains("**Beklenen:** _…_", md);
    }

    [Fact]
    public void Task_text_has_video_time_severity_and_session_info()
    {
        using var data = new TempData();
        ReviewStore.Set("v.mp4", "build", JsonValue.Create("0.4.2"));
        ReviewStore.Set("v.mp4", "tester", JsonValue.Create("Ayşe"));
        var text = Reports.TaskText(new Note { T = 65.4, Kind = "BUG", Text = "x", Video = "v.mp4", Severity = "ORTA" }, null);

        Assert.Contains("- **Video:** v.mp4 — **Zaman:** 00:01:05.4 (65.4 sn)", text);
        Assert.Contains("- **Önem:** Orta · **Durum:** Açık", text);
        Assert.Contains("**Build:** 0.4.2 · **Testçi:** Ayşe", text);
    }

    [Fact]
    public void Editing_notes_json_lists_bleeps_cuts_and_unpaired_marks()
    {
        using var data = new TempData();
        var info = new Reports.EditVideo("oyun.mp4", @"C:\Videolar\oyun.mp4", 120, 60, Sample(), 0.6);
        var json = JsonNode.Parse(Reports.EditJson(info))!.AsObject();

        Assert.Equal("oyun.mp4", (string)json["video"]);
        Assert.Equal(120, (double)json["length_s"]);
        Assert.Equal("seconds from the start of the video", (string)json["time_unit"]);
        var bleep = json["bleeps"]!.AsArray().Single()!;
        Assert.Equal((50.0, 50.8), ((double)bleep["start"], (double)bleep["end"]));
        var cut = json["cuts"]!.AsArray().Single()!;
        Assert.Equal((60.0, 84.0, 24.0), ((double)cut["start"], (double)cut["end"], (double)cut["duration"]));
        Assert.Equal(24, (double)json["cut_total_s"]);
        Assert.Equal(90, (double)json["unpaired_cut_marks"]!.AsArray().Single()!["t"]);
        Assert.Equal(3, json["notes"]!.AsArray().Count);
        Assert.Equal("ACIK", (string)json["notes"]![0]!["status"]);
    }

    [Fact]
    public void Editing_notes_markdown_marks_default_bleep_lengths()
    {
        using var data = new TempData();
        var notes = new List<Note> { N.Make(5, "BIP", "a"), N.Make(9, "BIP", "b", end: 10) };
        var md = Reports.EditMarkdown(new Reports.EditVideo("v.mp4", null, null, null, notes, 0.5));

        Assert.Contains("| 00:00:05.0 | 00:00:05.5 (varsayılan) | a |", md);
        Assert.Contains("| 00:00:09.0 | 00:00:10.0 | b |", md);
        Assert.Contains("## Kesimler — 0 (toplam 0 sn)", md);
    }
}
