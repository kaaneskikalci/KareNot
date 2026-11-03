using System.Text.Json.Nodes;

namespace KareNot.Tests;

// Moving the notes folder (Ayarlar > not klasörü) and merging inceleme.json must never lose notes.
public class DataMoveTests
{
    [Fact]
    public void Moves_files_and_merges_notes_and_review_files_that_exist_on_both_sides()
    {
        using var data = new TempData();
        var from = Path.Combine(data.Dir, "eski");
        var to = Path.Combine(data.Dir, "yeni");
        NoteStore.WriteAtomic(Path.Combine(Directory.CreateDirectory(Path.Combine(from, "oyun")).FullName, "notlar.txt"), new[] { N.Make(1, "BUG", "eski"), N.Make(2, "NOT", "ikisinde") });
        NoteStore.WriteAtomic(Path.Combine(Directory.CreateDirectory(Path.Combine(to, "oyun")).FullName, "notlar.txt"), new[] { N.Make(2, "NOT", "ikisinde"), N.Make(3, "NOT", "yeni") });
        Directory.CreateDirectory(Path.Combine(from, "oyun", "ss"));
        File.WriteAllText(Path.Combine(from, "oyun", "ss", "a.png"), "a");
        File.WriteAllText(Path.Combine(from, "inceleme.json"), """{"oyun.mp4":{"pos":10,"build":"eski"},"diger.mp4":{"pos":5}}""");
        File.WriteAllText(Path.Combine(to, "inceleme.json"), """{"oyun.mp4":{"pos":20}}""");
        File.WriteAllText(Path.Combine(from, "hazir_notlar.txt"), "eski liste");
        File.WriteAllText(Path.Combine(to, "hazir_notlar.txt"), "yeni liste");

        var (moved, kept) = DataMove.Run(from, to);

        Assert.Equal(3, moved);   // notlar.txt (merged), a.png, inceleme.json (merged)
        Assert.Equal(1, kept);    // hazir_notlar.txt exists in both: the old one stays
        Assert.Equal(new[] { "eski", "ikisinde", "yeni" }, NoteStore.ReadFile(Path.Combine(to, "oyun", "notlar.txt")).Select(n => n.Text));
        Assert.True(File.Exists(Path.Combine(to, "oyun", "ss", "a.png")));
        var review = JsonNode.Parse(File.ReadAllText(Path.Combine(to, "inceleme.json")))!;
        Assert.Equal(20, (double)review["oyun.mp4"]!["pos"]);      // the target's values win
        Assert.Equal("eski", (string)review["oyun.mp4"]!["build"]); // missing keys come from the old file
        Assert.Equal(5, (double)review["diger.mp4"]!["pos"]);
        Assert.Equal("yeni liste", File.ReadAllText(Path.Combine(to, "hazir_notlar.txt")));
        Assert.True(File.Exists(Path.Combine(from, "hazir_notlar.txt")));
        Assert.False(Directory.Exists(Path.Combine(from, "oyun")));  // emptied folders are removed
    }

    [Fact]
    public void Missing_source_moves_nothing()
    {
        Assert.Equal((0, 0), DataMove.Run(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), Path.GetTempPath()));
    }
}
