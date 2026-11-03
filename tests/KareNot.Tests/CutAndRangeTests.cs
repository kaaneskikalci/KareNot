namespace KareNot.Tests;

// Pairing of cut marks and the range math behind the edited video export.
public class CutAndRangeTests
{
    [Fact]
    public void Cuts_pair_each_start_with_the_next_end_in_time_order()
    {
        var notes = new[]
        {
            N.Make(30, "KES_BITIR"), N.Make(10, "KES_BASLA"), N.Make(5, "BUG"),
            N.Make(50, "KES_BASLA"), N.Make(60, "KES_BASLA"), N.Make(70, "KES_BITIR"), N.Make(80, "KES_BITIR"),
        };
        var cuts = Kinds.Cuts(notes);

        Assert.Equal(4, cuts.Count);
        Assert.Equal((10.0, 30.0), (cuts[0].Start.T, cuts[0].End.T));
        Assert.Equal(50, cuts[1].Start.T);   // start without an end: the next start closes it
        Assert.Null(cuts[1].End);
        Assert.Equal((60.0, 70.0), (cuts[2].Start.T, cuts[2].End.T));
        Assert.Null(cuts[3].Start);           // end without a start
        Assert.Equal(80, cuts[3].End.T);
    }

    [Fact]
    public void CutRanges_keep_only_complete_forward_pairs()
    {
        var notes = new[] { N.Make(10, "KES_BASLA"), N.Make(20, "KES_BITIR"), N.Make(30, "KES_BASLA"), N.Make(30, "KES_BITIR"), N.Make(40, "KES_BASLA") };
        Assert.Equal(new[] { (10.0, 20.0) }, Kinds.CutRanges(notes));
    }

    [Fact]
    public void BeepRanges_use_the_end_or_the_default_length()
    {
        var notes = new[] { N.Make(5, "BIP", end: 7), N.Make(10, "BIP"), N.Make(20, "BIP", end: 19), N.Make(1, "BUG") };
        Assert.Equal(new[] { (5.0, 7.0), (10.0, 10.6), (20.0, 20.6) }, Kinds.BeepRanges(notes, 0.6));
    }

    [Fact]
    public void Merge_joins_overlapping_and_touching_ranges()
    {
        var merged = Ffmpeg.Merge(new[] { (20.0, 30.0), (0.0, 5.0), (4.0, 8.0), (8.0, 9.0), (25.0, 26.0) });
        Assert.Equal(new[] { (0.0, 9.0), (20.0, 30.0) }, merged);
        Assert.Empty(Ffmpeg.Merge(Array.Empty<(double, double)>()));
    }

    [Fact]
    public void KeptParts_are_the_gaps_between_cuts()
    {
        var keep = Ffmpeg.KeptParts(new List<(double, double)> { (10, 20), (15, 25), (40, 50) }, 60);
        Assert.Equal(new (double, double?)[] { (0, 10), (25, 40), (50, null) }, keep);
    }

    [Fact]
    public void KeptParts_clamp_cuts_to_the_video_and_drop_slivers()
    {
        // A cut from the start, a sliver of 30 ms between two cuts, a cut past the end.
        var keep = Ffmpeg.KeptParts(new List<(double, double)> { (-1, 10), (10.03, 20), (55, 70) }, 60);
        Assert.Equal(new (double, double?)[] { (20, 55) }, keep);
    }

    [Fact]
    public void KeptParts_without_a_known_length_keep_the_rest_open()
    {
        Assert.Equal(new (double, double?)[] { (0, 10), (20, null) }, Ffmpeg.KeptParts(new List<(double, double)> { (10, 20) }, 0));
        Assert.Empty(Ffmpeg.KeptParts(new List<(double, double)> { (0, 60) }, 60));
    }
}
