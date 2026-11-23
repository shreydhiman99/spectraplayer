using Microsoft.Data.Sqlite;
using SpectraPlayer.Core.Library;
using SpectraPlayer.Core.Transcription;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class TimeRangeTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static TimeRange R(double a, double b) => new(S(a), S(b));

    [Fact]
    public void Merge_joins_overlapping_and_touching_ranges_and_sorts_them()
    {
        var merged = TimeRanges.Merge([R(50, 60), R(0, 10), R(5, 20), R(20, 30), R(40, 40)]);
        Assert.Equal([R(0, 30), R(50, 60)], merged); // the empty 40–40 range is dropped
    }

    [Fact]
    public void Merge_keeps_the_longer_end_when_one_range_contains_another()
    {
        Assert.Equal([R(0, 100)], TimeRanges.Merge([R(0, 100), R(10, 20)]));
    }

    [Fact]
    public void Subtract_with_nothing_covered_returns_the_whole_section()
    {
        Assert.Equal([R(10, 70)], TimeRanges.Subtract(R(10, 70), []));
    }

    [Fact]
    public void Subtract_fully_covered_returns_nothing()
    {
        Assert.Empty(TimeRanges.Subtract(R(10, 70), [R(0, 100)]));
        Assert.Empty(TimeRanges.Subtract(R(10, 70), [R(0, 40), R(40, 80)]));
    }

    [Fact]
    public void Subtract_returns_the_uncovered_gaps_in_order()
    {
        var gaps = TimeRanges.Subtract(R(0, 100), [R(20, 30), R(60, 70)]);
        Assert.Equal([R(0, 20), R(30, 60), R(70, 100)], gaps);
    }

    [Fact]
    public void Subtract_ignores_coverage_outside_the_section()
    {
        Assert.Equal([R(50, 100)], TimeRanges.Subtract(R(50, 100), [R(0, 10), R(200, 300)]));
        Assert.Equal([R(50, 100)], TimeRanges.Subtract(R(50, 100), [R(0, 50)]));
    }

    [Fact]
    public void Subtract_drops_slivers_shorter_than_a_second()
    {
        Assert.Empty(TimeRanges.Subtract(R(0, 100), [R(0, 99.5)]));       // 0.5 s left at the end
        Assert.Equal([R(0, 10)], TimeRanges.Subtract(R(0, 100), [R(10, 99.6)]));
    }
}

public class SegmentFilterTests
{
    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData(" [ Silence ] ")]
    [InlineData("(music)")]
    [InlineData("*music*")]
    [InlineData("♪")]
    [InlineData("♪ ♪ ♪")]
    [InlineData("...")]
    [InlineData("   ")]
    [InlineData("")]
    public void Non_speech_is_dropped(string text) => Assert.Null(SegmentFilter.Clean(text));

    [Fact]
    public void Null_is_dropped() => Assert.Null(SegmentFilter.Clean(null));

    [Theory]
    [InlineData("  Hello   world ", "Hello world")]
    [InlineData("(laughs) That was funny", "(laughs) That was funny")]
    [InlineData("He said (quietly)", "He said (quietly)")]
    [InlineData("Line one\nline two", "Line one line two")]
    public void Speech_is_kept_and_whitespace_normalised(string input, string expected) =>
        Assert.Equal(expected, SegmentFilter.Clean(input));
}

public class TranscriptStoreTests : IDisposable
{
    private const string Video = @"D:\C\a.mp4";
    private readonly LibraryStore _store = new(":memory:");

    public void Dispose() => _store.Dispose();

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static TimeRange R(double a, double b) => new(S(a), S(b));
    private static (TimeSpan, TimeSpan, string) Seg(double a, double b, string t) => (S(a), S(b), t);

    [Fact]
    public void Segments_are_returned_in_time_order_when_they_overlap_the_window()
    {
        _store.AddTranscribed(Video, "m", R(0, 60), [Seg(30, 40, "later"), Seg(0, 10, "first"), Seg(10, 20, "second")]);

        Assert.Equal(["first", "second", "later"], _store.GetTranscript(Video, "m", S(0), S(60)).Select(s => s.Text));
        // a segment counts if any part of it falls inside the window
        Assert.Equal(["second", "later"], _store.GetTranscript(Video, "m", S(15), S(35)).Select(s => s.Text));
        // touching the window's edge is not overlapping it
        Assert.Equal(["second"], _store.GetTranscript(Video, "m", S(10), S(20)).Select(s => s.Text));
    }

    [Fact]
    public void Covered_ranges_are_merged_across_calls()
    {
        _store.AddTranscribed(Video, "m", R(0, 30), [Seg(0, 5, "a")]);
        _store.AddTranscribed(Video, "m", R(60, 90), [Seg(60, 65, "c")]);
        _store.AddTranscribed(Video, "m", R(30, 60), [Seg(30, 35, "b")]); // fills the gap between them

        Assert.Equal([R(0, 90)], _store.GetTranscribedRanges(Video, "m"));
    }

    [Fact]
    public void A_range_without_speech_is_still_remembered()
    {
        _store.AddTranscribed(Video, "m", R(100, 160), []);
        Assert.Equal([R(100, 160)], _store.GetTranscribedRanges(Video, "m"));
        Assert.Empty(_store.GetTranscript(Video, "m", S(100), S(160)));
    }

    [Fact]
    public void Models_and_videos_do_not_share_results()
    {
        _store.AddTranscribed(Video, "base", R(0, 10), [Seg(0, 5, "from base")]);

        Assert.Empty(_store.GetTranscribedRanges(Video, "small"));
        Assert.Empty(_store.GetTranscript(Video, "small", S(0), S(10)));
        Assert.Empty(_store.GetTranscribedRanges(@"D:\C\other.mp4", "base"));
        Assert.Equal(Video, _store.GetTranscript(@"d:\c\A.MP4", "base", S(0), S(10)).Single().VideoPath); // path lookup ignores case
    }

    [Fact]
    public void Clear_removes_one_model_or_everything_for_the_video()
    {
        _store.AddTranscribed(Video, "base", R(0, 10), [Seg(0, 5, "b")]);
        _store.AddTranscribed(Video, "small", R(0, 10), [Seg(0, 5, "s")]);

        _store.ClearTranscript(Video, "base");
        Assert.Empty(_store.GetTranscribedRanges(Video, "base"));
        Assert.Single(_store.GetTranscript(Video, "small", S(0), S(10)));

        _store.ClearTranscript(Video);
        Assert.Empty(_store.GetTranscribedRanges(Video, "small"));
        Assert.Empty(_store.GetTranscript(Video, "small", S(0), S(10)));
    }

    [Fact]
    public void Database_from_the_previous_release_is_upgraded_and_keeps_its_notes()
    {
        var dir = Directory.CreateTempSubdirectory("spectra-migrate3");
        try
        {
            var file = Path.Combine(dir.FullName, "library.db");
            using (var old = new SqliteConnection($"Data Source={file};Pooling=False"))
            {
                old.Open();
                using var cmd = old.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE tabs (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL,
                        folder TEXT NOT NULL UNIQUE COLLATE NOCASE, sort_order INTEGER NOT NULL, last_video TEXT);
                    CREATE TABLE progress (path TEXT PRIMARY KEY COLLATE NOCASE, position_ms INTEGER NOT NULL,
                        duration_ms INTEGER NOT NULL, updated_utc TEXT NOT NULL);
                    CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    CREATE TABLE notes (id INTEGER PRIMARY KEY AUTOINCREMENT, video_path TEXT NOT NULL COLLATE NOCASE,
                        start_ms INTEGER NOT NULL, end_ms INTEGER, text TEXT NOT NULL, source TEXT NOT NULL DEFAULT 'manual',
                        created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
                    INSERT INTO notes (video_path, start_ms, text, created_utc, updated_utc)
                        VALUES ('D:\C\a.mp4', 5000, 'kept', '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z');
                    PRAGMA user_version = 2;
                    """;
                cmd.ExecuteNonQuery();
            }

            using (var upgraded = new LibraryStore(file))
            {
                Assert.Equal("kept", upgraded.GetNotes(@"D:\C\a.mp4").Single().Text);
                upgraded.AddTranscribed(Video, "m", R(0, 10), [Seg(0, 5, "works after upgrade")]);
            }

            using var reopened = new LibraryStore(file); // already current: must not fail
            Assert.Single(reopened.GetTranscript(Video, "m", S(0), S(10)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            dir.Delete(true);
        }
    }
}
