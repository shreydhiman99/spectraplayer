using System.Text;
using SpectraPlayer.Core.Library;
using SpectraPlayer.Core.Transcription;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class SubtitleParserTests
{
    private static TimeSpan T(int h, int m, int s, int ms = 0) => new(0, h, m, s, ms);

    [Fact]
    public void Parses_srt_with_numbers_multi_line_cues_and_crlf()
    {
        const string srt = "1\r\n00:00:01,000 --> 00:00:03,500\r\nHello there.\r\n\r\n" +
                           "2\r\n00:00:04,000 --> 00:00:07,250\r\nSecond line\r\nwraps onto two lines.\r\n\r\n";

        var cues = SubtitleParser.Parse(srt);

        Assert.Equal(2, cues.Count);
        Assert.Equal((T(0, 0, 1), T(0, 0, 3, 500), "Hello there."), (cues[0].Start, cues[0].End, cues[0].Text));
        Assert.Equal("Second line wraps onto two lines.", cues[1].Text);
        Assert.Equal(T(0, 0, 7, 250), cues[1].End);
    }

    [Fact]
    public void Parses_webvtt_header_notes_ids_short_times_and_cue_settings()
    {
        const string vtt = "WEBVTT - lecture\n\nNOTE this is a comment\nacross two lines\n\n" +
                           "intro\n00:01.500 --> 00:04.000 align:start position:0%\nWelcome.\n\n" +
                           "STYLE\n::cue { color: red }\n\n" +
                           "01:00:02.000 --> 01:00:05.000\nOver an hour in.\n";

        var cues = SubtitleParser.Parse(vtt);

        Assert.Equal(["Welcome.", "Over an hour in."], cues.Select(c => c.Text));
        Assert.Equal(T(0, 0, 1, 500), cues[0].Start);
        Assert.Equal(T(1, 0, 2), cues[1].Start);
    }

    [Fact]
    public void Strips_markup_decodes_entities_and_drops_non_speech_cues()
    {
        const string srt = "1\n00:00:01,000 --> 00:00:02,000\n<i>Force</i> &amp; <font color=\"#fff\">mass</font> {\\an8}\n\n" +
                           "2\n00:00:03,000 --> 00:00:04,000\n[Music]\n\n" +
                           "3\n00:00:05,000 --> 00:00:06,000\n♪ ♪\n\n" +
                           "4\n00:00:07,000 --> 00:00:08,000\n<b></b>\n\n" +
                           "5\n00:00:09,000 --> 00:00:10,000\nKept &lt;3\n";

        Assert.Equal(["Force & mass", "Kept <3"], SubtitleParser.Parse(srt).Select(c => c.Text));
    }

    [Fact]
    public void Short_fractions_mean_the_decimal_part_and_sorting_is_by_start()
    {
        const string srt = "1\n00:00:10,5 --> 00:00:12,0\nlater\n\n2\n00:00:02,25 --> 00:00:03,0\nearlier\n";

        var cues = SubtitleParser.Parse(srt);

        Assert.Equal(["earlier", "later"], cues.Select(c => c.Text));
        Assert.Equal(T(0, 0, 10, 500), cues[1].Start);
        Assert.Equal(T(0, 0, 2, 250), cues[0].Start);
    }

    [Fact]
    public void Junk_and_backwards_cues_are_skipped_without_failing()
    {
        const string junk = "not a subtitle file\n\n00:00:05,000 --> 00:00:04,000\nbackwards\n\n00:00:01,000 --> 00:00:02,000\nfine\n";
        Assert.Equal(["fine"], SubtitleParser.Parse(junk).Select(c => c.Text));
        Assert.Empty(SubtitleParser.Parse(""));
    }

    [Fact]
    public void A_cue_directly_after_another_without_a_blank_line_is_still_found()
    {
        const string srt = "1\n00:00:01,000 --> 00:00:02,000\none\n00:00:03,000 --> 00:00:04,000\ntwo\n";
        Assert.Equal(["one", "two"], SubtitleParser.Parse(srt).Select(c => c.Text));
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf8")]
    [InlineData("utf16le-bom")]
    [InlineData("windows-1252")]
    public void Decodes_common_encodings(string kind)
    {
        const string srt = "1\n00:00:01,000 --> 00:00:02,000\nCafé naïve\n";
        var bytes = kind switch
        {
            "utf8-bom" => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(srt)).ToArray(),
            "utf8" => Encoding.UTF8.GetBytes(srt),
            "utf16le-bom" => new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes(srt)).ToArray(),
            _ => Latin1(srt),
        };

        Assert.Equal("Café naïve", SubtitleParser.Parse(SubtitleParser.Decode(bytes)).Single().Text);
    }

    private static byte[] Latin1(string s) => s.Select(c => (byte)c).ToArray(); // é and ï are single bytes in Windows-1252
}

public class SubtitleFinderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("spectra-subs");

    public void Dispose() => _dir.Delete(true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir.FullName, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void Prefers_the_exact_name_then_english_then_anything_else()
    {
        var video = Touch("Lecture 1.mkv");
        var german = Touch("Lecture 1.de.srt");
        Assert.Equal(german, SubtitleFinder.Find(video));

        var english = Touch("Lecture 1.en.srt");
        Assert.Equal(english, SubtitleFinder.Find(video));

        var exact = Touch("Lecture 1.srt");
        Assert.Equal(exact, SubtitleFinder.Find(video));
    }

    [Fact]
    public void Prefers_srt_over_vtt_at_the_same_rank_and_finds_vtt_alone()
    {
        var video = Touch("a.mp4");
        var vtt = Touch("a.en.vtt");
        Assert.Equal(vtt, SubtitleFinder.Find(video));

        var srt = Touch("a.en.srt");
        Assert.Equal(srt, SubtitleFinder.Find(video));
    }

    [Fact]
    public void Ignores_other_videos_files_and_our_own_exports()
    {
        var video = Touch("Lecture 1.mkv");
        Touch("Lecture 10.srt");                 // a different video: "Lecture 1." is not a prefix of "Lecture 10.srt"
        Touch("Lecture 1.transcript.srt");       // exported by Spectraplayer: never a source
        Touch("Lecture 1.txt");

        Assert.Null(SubtitleFinder.Find(video));
    }

    [Fact]
    public void Returns_null_when_the_folder_is_missing()
    {
        Assert.Null(SubtitleFinder.Find(Path.Combine(_dir.FullName, "nope", "x.mp4")));
    }
}

public class SubtitleImporterTests : IDisposable
{
    private const string Video = @"D:\C\a.mp4";
    private readonly LibraryStore _store = new(":memory:");

    public void Dispose() => _store.Dispose();

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static SubtitleCue Cue(double a, double b, string t) => new(S(a), S(b), t);
    private static TimeRange R(double a, double b) => new(S(a), S(b));

    private static readonly SubtitleCue[] Cues =
    [
        Cue(0, 10, "zero"), Cue(10, 20, "ten"), Cue(20, 30, "twenty"), Cue(30, 45, "thirty"), Cue(45, 60, "forty-five"),
    ];

    private string[] Stored(double a, double b) =>
        _store.GetTranscript(Video, TranscriptSources.SubtitlesId, S(a), S(b)).Select(s => s.Text).ToArray();

    [Fact]
    public void Imports_the_cues_whose_centre_is_in_the_section()
    {
        var added = SubtitleImporter.Import(_store, Video, R(10, 40), Cues);

        // "thirty" runs 30-45, centre 37.5: inside, and it extends coverage to 45
        Assert.Equal(3, added);
        Assert.Equal(["ten", "twenty", "thirty"], Stored(0, 100));
        Assert.Equal([R(10, 45)], _store.GetTranscribedRanges(Video, TranscriptSources.SubtitlesId));
    }

    [Fact]
    public void Importing_the_same_section_again_adds_nothing()
    {
        SubtitleImporter.Import(_store, Video, R(10, 40), Cues);
        Assert.Equal(0, SubtitleImporter.Import(_store, Video, R(10, 40), Cues));
        Assert.Equal(3, Stored(0, 100).Length);
    }

    [Fact]
    public void Neighbouring_sections_never_store_a_line_twice()
    {
        SubtitleImporter.Import(_store, Video, R(0, 25), Cues);   // takes zero, ten, twenty (centre 25 is not < 25: next one)
        SubtitleImporter.Import(_store, Video, R(25, 60), Cues);

        Assert.Equal(["zero", "ten", "twenty", "thirty", "forty-five"], Stored(0, 100));
    }

    [Fact]
    public void A_section_without_subtitle_text_is_still_remembered()
    {
        Assert.Equal(0, SubtitleImporter.Import(_store, Video, R(100, 160), Cues));
        Assert.Equal([R(100, 160)], _store.GetTranscribedRanges(Video, TranscriptSources.SubtitlesId));
    }

    [Fact]
    public void Subtitle_text_is_kept_apart_from_whisper_text()
    {
        SubtitleImporter.Import(_store, Video, R(0, 60), Cues);
        Assert.Empty(_store.GetTranscript(Video, "base.en-q5_1", S(0), S(60)));
    }
}

public class TranscriptExportTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static TranscriptSegment Seg(double a, double b, string t) => new(1, @"D:\a.mp4", "m", S(a), S(b), t);

    [Fact]
    public void Markdown_has_a_heading_a_count_the_source_and_timestamped_paragraphs()
    {
        var md = TranscriptExport.ToMarkdown("Lecture 1", "Standard (base, English)",
            [Seg(62, 70, "Second."), Seg(5, 9, "First.")]);

        Assert.Equal(
            "# Lecture 1 — transcript\n\n" +
            "_2 lines · Standard (base, English)_\n\n" +
            "**[00:05]** First.\n\n" +
            "**[01:02]** Second.\n", md);
    }

    [Fact]
    public void Markdown_without_text_says_so()
    {
        Assert.Equal("# L — transcript\n\n_No text._\n", TranscriptExport.ToMarkdown("L", "x", []));
    }

    [Fact]
    public void Srt_uses_numbered_cues_with_comma_milliseconds_and_hours()
    {
        var srt = TranscriptExport.ToSrt([Seg(3725.25, 3730, "Late."), Seg(1.5, 4, "Early.")]);

        Assert.Equal(
            "1\r\n00:00:01,500 --> 00:00:04,000\r\nEarly.\r\n\r\n" +
            "2\r\n01:02:05,250 --> 01:02:10,000\r\nLate.\r\n\r\n", srt);
    }

    [Fact]
    public void Srt_trims_overlaps_so_lines_follow_each_other()
    {
        // the first line runs to 12 s but the next begins at 10 s (it was found with context padding)
        var srt = TranscriptExport.ToSrt([Seg(5, 12, "One."), Seg(10, 15, "Two.")]);

        Assert.Contains("00:00:05,000 --> 00:00:10,000", srt);
        Assert.Contains("00:00:10,000 --> 00:00:15,000", srt);
    }

    [Fact]
    public void Srt_survives_a_round_trip_through_the_parser()
    {
        var segments = new[] { Seg(1, 3, "Hello."), Seg(3.5, 6, "World, again.") };

        var back = SubtitleParser.Parse(TranscriptExport.ToSrt(segments));

        Assert.Equal(["Hello.", "World, again."], back.Select(c => c.Text));
        Assert.Equal([S(1), S(3.5)], back.Select(c => c.Start));
    }
}
