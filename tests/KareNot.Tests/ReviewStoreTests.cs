using System.Text.Json.Nodes;

namespace KareNot.Tests;

// inceleme.json: resume position, review progress, build and tester of every video.
public class ReviewStoreTests
{
    [Fact]
    public void Values_are_written_to_inceleme_json_and_read_back()
    {
        using var data = new TempData();
        ReviewStore.SetPosition("oyun.mp4", 12.346);
        ReviewStore.Set("oyun.mp4", "build", "0.4.2");
        ReviewStore.Reload();

        Assert.Equal(12.35, ReviewStore.Position("oyun.mp4"));
        Assert.Equal("0.4.2", ReviewStore.Build("oyun.mp4"));
        Assert.Equal("", ReviewStore.Tester("oyun.mp4"));
        Assert.Equal("Devam ediyor", ReviewStore.State("oyun.mp4", false));
        Assert.Equal("Başlanmadı", ReviewStore.State("diger.mp4", false));
    }

    [Fact]
    public void Unknown_keys_written_by_the_iina_plugin_are_kept()
    {
        using var data = new TempData();
        data.Write("inceleme.json", """{"oyun.mp4":{"pos":3,"mac_only":"x"}}""");
        ReviewStore.Reload();
        ReviewStore.SetPosition("oyun.mp4", 9);

        var json = JsonNode.Parse(File.ReadAllText(Paths.ReviewFile))!;
        Assert.Equal("x", (string)json["oyun.mp4"]!["mac_only"]);
        Assert.Equal(9, (double)json["oyun.mp4"]!["pos"]);
    }

    [Fact]
    public void A_locked_file_is_never_replaced_by_an_empty_one()
    {
        // Regression: while the file could not be read, an empty root was cached and the next save (every 5 s while
        // playing) wrote it over the progress, build and tester of every other video.
        using var data = new TempData();
        data.Write("inceleme.json", """{"eski.mp4":{"pos":100,"build":"0.3","tester":"Ayşe"}}""");
        ReviewStore.Reload();
        using (new FileStream(Paths.ReviewFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ReviewStore.SetPosition("yeni.mp4", 5);
            Assert.Equal(0, ReviewStore.Position("eski.mp4"));
        }
        ReviewStore.SetPosition("yeni.mp4", 6);

        var json = JsonNode.Parse(File.ReadAllText(Paths.ReviewFile))!;
        Assert.Equal("Ayşe", (string)json["eski.mp4"]!["tester"]);
        Assert.Equal(6, (double)json["yeni.mp4"]!["pos"]);
    }

    [Fact]
    public void A_corrupt_file_is_kept_aside_before_starting_over()
    {
        using var data = new TempData();
        data.Write("inceleme.json", "{ \"eski.mp4\": { \"pos\": 1");
        ReviewStore.Reload();
        ReviewStore.SetPosition("yeni.mp4", 7);

        var backup = Assert.Single(Directory.GetFiles(data.Root, "inceleme.json.bozuk-*"));
        Assert.Equal("{ \"eski.mp4\": { \"pos\": 1", File.ReadAllText(backup));
        Assert.Equal(7, ReviewStore.Position("yeni.mp4"));
    }
}
