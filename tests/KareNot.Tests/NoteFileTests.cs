namespace KareNot.Tests;

// notlar.txt: the tab-separated note file shared with the IINA plugin (Notes.cs, iina/KareNot.iinaplugin/lib/format.js).
public class NoteFileTests
{
    [Fact]
    public void Clean_removes_tabs_and_line_breaks()
    {
        Assert.Equal("a b  c", NoteStore.Clean(" a\tb\r\nc "));   // each control character becomes one space
        Assert.Equal("", NoteStore.Clean(null));
    }

    [Fact]
    public void Write_then_read_round_trips_every_column()
    {
        using var data = new TempData();
        var path = Path.Combine(data.Dir, "notlar.txt");
        var notes = new[]
        {
            new Note { T = 65.4, Kind = "BUG", Text = "Kamera takıldı #kamera", Video = "oyun 1 ğüşiöç.mp4", Shot = "bug_1.png", Status = "TEKRAR_TEST", Severity = "KRITIK", Clip = "bug_1_klip.mp4" },
            new Note { T = 10, Kind = "BIP", Text = "", Video = "oyun 1 ğüşiöç.mp4", End = 11.5 },
            new Note { T = 3, Kind = "NOT", Text = "düz not", Video = "oyun 1 ğüşiöç.mp4" },
        };
        NoteStore.WriteAtomic(path, notes);
        var read = NoteStore.ReadFile(path);

        Assert.Equal(3, read.Count);
        Assert.Equal(65.4, read[0].T);
        Assert.Equal("BUG", read[0].Kind);
        Assert.Equal("Kamera takıldı #kamera", read[0].Text);
        Assert.Equal("oyun 1 ğüşiöç.mp4", read[0].Video);
        Assert.Equal("bug_1.png", read[0].Shot);
        Assert.Equal("TEKRAR_TEST", read[0].Status);
        Assert.Equal("KRITIK", read[0].Severity);
        Assert.Null(read[0].End);
        Assert.Equal("bug_1_klip.mp4", read[0].Clip);
        Assert.Equal(11.5, read[1].End);
        Assert.Null(read[2].Shot);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Written_lines_keep_the_old_six_column_layout_when_the_extra_columns_are_empty()
    {
        using var data = new TempData();
        var path = Path.Combine(data.Dir, "notlar.txt");
        NoteStore.WriteAtomic(path, new[] { N.Make(65.4, "BUG", "a"), N.Make(10, "BIP", "", end: 11.5) });
        var lines = File.ReadAllLines(path);

        Assert.Equal("# saniye\tzaman\ttur\tnot\tvideo\tss\tdurum\tonem\tbitis\tklip", lines[0]);
        Assert.Equal("65.40\t00:01:05.4\tBUG\ta\toyun.mp4\t", lines[1]);
        Assert.Equal("10.00\t00:00:10.0\tBIP\t\toyun.mp4\t\t\t\t11.50", lines[2]);
    }

    [Fact]
    public void Writing_is_culture_independent()
    {
        using var data = new TempData();
        var path = Path.Combine(data.Dir, "notlar.txt");
        Culture.Turkish(() => NoteStore.WriteAtomic(path, new[] { N.Make(1.5, "NOT", "x") }));
        Assert.StartsWith("1.50\t00:00:01.5\t", File.ReadAllLines(path)[1]);
    }

    [Fact]
    public void Reading_accepts_old_five_column_files_a_bom_and_crlf()
    {
        using var data = new TempData();
        var path = Path.Combine(data.Dir, "notlar.txt");
        File.WriteAllText(path, "﻿# yorum\r\n12.50\t00:00:12.5\tNOT\teski not\tv.mp4\r\n\r\n", new System.Text.UTF8Encoding(true));
        var n = Assert.Single(NoteStore.ReadFile(path));
        Assert.Equal(12.5, n.T);
        Assert.Equal("eski not", n.Text);
        Assert.Null(n.Shot);
    }

    [Fact]
    public void Missing_file_reads_as_empty()
    {
        Assert.Empty(NoteStore.ReadFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt")));
    }

    [Fact]
    public void Key_identifies_a_note_by_video_time_kind_and_text()
    {
        var a = N.Make(1.004, "BUG", " x ");
        var b = N.Make(1.0, "BUG", "x");
        b.Status = "DUZELTILDI";
        Assert.Equal(NoteStore.Key(a), NoteStore.Key(b));
        Assert.NotEqual(NoteStore.Key(a), NoteStore.Key(N.Make(1.0, "NOT", "x")));
    }

    [Fact]
    public void FindMedia_takes_the_exact_name_or_the_first_file_with_the_old_prefix()
    {
        using var data = new TempData();
        var dir = Path.Combine(data.Dir, "ss");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bug_b.png"), "");
        File.WriteAllText(Path.Combine(dir, "bug_a.png"), "");

        Assert.Equal("bug_b.png", NoteStore.FindMedia(dir, "bug_b.png", true));
        Assert.Equal("bug_a.png", NoteStore.FindMedia(dir, "bug_", true));
        Assert.Null(NoteStore.FindMedia(dir, "bug_", false));
        Assert.Null(NoteStore.FindMedia(dir, null, true));
        Assert.Null(NoteStore.FindMedia(Path.Combine(dir, "yok"), "bug_a.png", true));
    }
}
