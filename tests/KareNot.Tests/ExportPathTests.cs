namespace KareNot.Tests;

public class ExportPathTests
{
    [Fact]
    public void Unique_returns_the_path_while_it_is_free()
    {
        using var data = new TempData();
        var path = Path.Combine(data.Dir, "KareNot_tum_20261103_1200.zip");
        Assert.Equal(path, Paths.Unique(path));
    }

    [Fact]
    public void Unique_numbers_the_name_instead_of_reusing_an_existing_export()
    {
        // Regression: a second findings package in the same minute failed ("file exists") and the clean-up then deleted
        // the first package.
        using var data = new TempData();
        var path = Path.Combine(data.Dir, "KareNot_oyun 1_20261103_1200.zip");
        File.WriteAllText(path, "ilk");
        Assert.Equal(Path.Combine(data.Dir, "KareNot_oyun 1_20261103_1200 (2).zip"), Paths.Unique(path));
        File.WriteAllText(Path.Combine(data.Dir, "KareNot_oyun 1_20261103_1200 (2).zip"), "ikinci");
        Assert.Equal(Path.Combine(data.Dir, "KareNot_oyun 1_20261103_1200 (3).zip"), Paths.Unique(path));
        Assert.Equal("ilk", File.ReadAllText(path));
    }
}
