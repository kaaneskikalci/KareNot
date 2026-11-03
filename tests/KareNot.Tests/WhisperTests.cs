namespace KareNot.Tests;

// Parsing of whisper.cpp output and the profanity matcher (Whisper.cs).
public class WhisperTests
{
    const string Sample = """
        {
          "transcription": [
            { "timestamps": { "from": "00:00:01,000", "to": "00:00:01,400" }, "offsets": { "from": 1000, "to": 1400 }, "text": " Abi" },
            { "timestamps": { "from": "00:00:01,400", "to": "00:00:01,900" }, "offsets": { "from": 1400, "to": 1900 }, "text": " AMK," },
            { "timestamps": { "from": "00:00:02,000", "to": "00:00:02,600" }, "offsets": { "from": 2000, "to": 2600 }, "text": " \"Siktir\"" },
            { "timestamps": { "from": "00:00:03,000", "to": "00:00:04,000" }, "offsets": { "from": 3000, "to": 4000 }, "text": " İyi oyun." },
            { "timestamps": { "from": "00:00:04,000", "to": "00:00:04,000" }, "offsets": { "from": 4000, "to": 4000 }, "text": "" }
          ]
        }
        """;

    [Fact]
    public void Parses_whisper_cpp_json_and_splits_segments_by_word_length()
    {
        var words = Whisper.ParseWords(Sample);
        Assert.Equal(5, words.Count);
        Assert.Equal((1.4, 1.9), (words[1].Start, words[1].End));
        Assert.Equal("İyi", words[3].Text);
        Assert.Equal(3.375, words[3].End, 9);
        Assert.Equal(3.375, words[4].Start, 9);
    }

    [Fact]
    public void Falls_back_to_a_regex_scan_for_malformed_json()
    {
        var broken = Whisper.ParseWords(Sample.Replace(" Abi", " A\tbi"));   // raw tab: invalid JSON string
        Assert.Equal(6, broken.Count);
        Assert.Equal(new[] { "A", "bi", "AMK," }, broken.Take(3).Select(w => w.Text));
        Assert.Equal(1.4, broken[2].Start, 9);
    }

    [Fact]
    public void Reads_openai_style_segments_and_words()
    {
        Assert.Equal(0.5, Whisper.ParseWords("""{"segments":[{"start":0.5,"end":1.0,"text":" amk"}]}""")[0].Start);
        var words = Whisper.ParseWords("""{"segments":[{"start":0,"end":2,"text":"x y","words":[{"word":"x","start":0.1,"end":0.2},{"word":"y","start":1.5,"end":1.9}]}]}""");
        Assert.Equal(new[] { (0.1, 0.2), (1.5, 1.9) }, words.Select(w => (w.Start, w.End)));
    }

    [Fact]
    public void Never_throws_on_garbage()
    {
        Assert.Empty(Whisper.ParseWords(""));
        Assert.Empty(Whisper.ParseWords(null));
        Assert.Empty(Whisper.ParseWords("{ nonsense"));
        Assert.Empty(Whisper.ParseWords("[1,2,3]"));
    }

    [Fact]
    public void Candidates_match_word_starts_with_turkish_case_rules_and_strip_punctuation()
    {
        var hits = Whisper.Candidates(Whisper.ParseWords(Sample), new[] { "amk", "sik", "piç" });
        Assert.Equal(new[] { "AMK", "Siktir" }, hits.Select(h => h.Text));
        Assert.Equal(2.0, hits[1].Start, 9);
    }

    [Theory]
    [InlineData("IŞIK!", "ışık")]
    [InlineData("İyi.", "iyi")]
    [InlineData("\"a-b\"", "ab")]
    [InlineData(null, "")]
    public void Normalize_lowercases_with_turkish_rules(string word, string expected)
    {
        Assert.Equal(expected, Whisper.Normalize(word));
    }

    [Fact]
    public void Built_in_self_check_passes()
    {
        Assert.True(Whisper.SelfCheck());
    }
}
