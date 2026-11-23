using Microsoft.Data.Sqlite;
using SpectraPlayer.Core.Library;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class NotesStoreTests : IDisposable
{
    private readonly LibraryStore _store = new(":memory:");

    public void Dispose() => _store.Dispose();

    [Fact]
    public void Notes_come_back_in_time_order_per_video()
    {
        _store.AddNote(@"D:\C\a.mp4", TimeSpan.FromSeconds(90), "later");
        _store.AddNote(@"D:\C\a.mp4", TimeSpan.FromSeconds(10), "earlier");
        _store.AddNote(@"D:\C\b.mp4", TimeSpan.FromSeconds(5), "other video");

        var notes = _store.GetNotes(@"d:\c\A.MP4"); // path lookup ignores case

        Assert.Equal(["earlier", "later"], notes.Select(n => n.Text));
        Assert.All(notes, n => Assert.Equal(Note.ManualSource, n.Source));
    }

    [Fact]
    public void Section_notes_keep_their_end_and_text_is_trimmed()
    {
        var added = _store.AddNote(@"D:\C\a.mp4", TimeSpan.FromSeconds(60), "  Derivation  \n", TimeSpan.FromSeconds(125));
        var stored = _store.GetNotes(@"D:\C\a.mp4").Single();

        Assert.Equal(added.Id, stored.Id);
        Assert.Equal("Derivation", stored.Text);
        Assert.Equal(TimeSpan.FromSeconds(125), stored.End);
        Assert.Equal("01:00–02:05", stored.TimeLabel);
    }

    [Fact]
    public void Edit_changes_text_and_timestamp_of_update_only_and_unknown_id_returns_null()
    {
        var note = _store.AddNote(@"D:\C\a.mp4", TimeSpan.FromSeconds(10), "draft");

        var edited = _store.UpdateNoteText(note.Id, " final ")!;

        Assert.Equal("final", edited.Text);
        Assert.Equal(note.Start, edited.Start);
        Assert.True(edited.UpdatedUtc >= note.CreatedUtc);
        Assert.Equal(note.CreatedUtc, edited.CreatedUtc);
        Assert.Null(_store.UpdateNoteText(9999, "x"));
    }

    [Fact]
    public void Delete_removes_only_that_note()
    {
        var a = _store.AddNote(@"D:\C\a.mp4", TimeSpan.FromSeconds(1), "a");
        _store.AddNote(@"D:\C\a.mp4", TimeSpan.FromSeconds(2), "b");

        _store.DeleteNote(a.Id);

        Assert.Equal(["b"], _store.GetNotes(@"D:\C\a.mp4").Select(n => n.Text));
    }

    [Fact]
    public void GetNotesUnder_groups_by_video_and_stays_inside_the_folder()
    {
        _store.AddNote(@"D:\Course\a.mp4", TimeSpan.FromSeconds(20), "a2");
        _store.AddNote(@"D:\Course\a.mp4", TimeSpan.FromSeconds(5), "a1");
        _store.AddNote(@"D:\Course\sub\b.mp4", TimeSpan.FromSeconds(1), "b1");
        _store.AddNote(@"D:\Course2\c.mp4", TimeSpan.FromSeconds(1), "other course");

        var map = _store.GetNotesUnder(@"d:\course");

        Assert.Equal(2, map.Count);
        Assert.Equal(["a1", "a2"], map[@"D:\Course\a.mp4"].Select(n => n.Text));
        Assert.Single(map[@"D:\Course\sub\b.mp4"]);
    }

    [Fact]
    public void Multi_line_and_unicode_text_round_trips()
    {
        const string text = "Line one\nLine two — ∑ x² ✓";
        _store.AddNote(@"D:\C\a.mp4", TimeSpan.Zero, text);
        Assert.Equal(text, _store.GetNotes(@"D:\C\a.mp4").Single().Text);
    }

    [Fact]
    public void Database_from_the_previous_release_is_upgraded_and_keeps_its_data()
    {
        var dir = Directory.CreateTempSubdirectory("spectra-migrate");
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
                    INSERT INTO tabs (title, folder, sort_order) VALUES ('Physics', 'D:\Physics', 0);
                    PRAGMA user_version = 1;
                    """;
                cmd.ExecuteNonQuery();
            }

            using (var upgraded = new LibraryStore(file))
            {
                Assert.Equal("Physics", upgraded.GetTabs().Single().Title);
                upgraded.AddNote(@"D:\Physics\1.mp4", TimeSpan.FromSeconds(3), "works after upgrade");
            }

            using var reopened = new LibraryStore(file); // already at the current version: must not fail
            Assert.Single(reopened.GetNotes(@"D:\Physics\1.mp4"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            dir.Delete(true);
        }
    }
}

public class NotesMarkdownTests
{
    private static Note N(long id, double startSec, string text, double? endSec = null) =>
        new(id, @"D:\C\a.mp4", TimeSpan.FromSeconds(startSec), endSec is { } e ? TimeSpan.FromSeconds(e) : null,
            text, Note.ManualSource, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public void Video_notes_are_bullets_in_time_order_with_timestamps()
    {
        var md = NotesMarkdown.ForVideo("Lecture 1", [N(1, 3725, "late"), N(2, 42, "early"), N(3, 60, "range", 125)]);

        Assert.Equal(
            "# Lecture 1\n\n" +
            "- **[00:42]** early\n" +
            "- **[01:00–02:05]** range\n" +
            "- **[1:02:05]** late\n", md);
    }

    [Fact]
    public void Multi_line_notes_stay_inside_one_bullet()
    {
        var md = NotesMarkdown.ForVideo("L", [N(1, 5, "First line\r\n\r\nSecond para\n  - sub point")]);

        Assert.Equal("# L\n\n- **[00:05]** First line\n\n  Second para\n    - sub point\n", md);
    }

    [Fact]
    public void Video_without_notes_says_so()
    {
        Assert.Equal("# L\n\n_No notes._\n", NotesMarkdown.ForVideo("L", []));
    }

    [Fact]
    public void Tab_export_has_a_section_per_video_with_counts_and_skips_empty_ones()
    {
        var md = NotesMarkdown.ForTab("Physics",
        [
            new NotesSection("Lecture 1", null, [N(1, 5, "a"), N(2, 9, "b")]),
            new NotesSection("Lecture 2", null, []),
            new NotesSection("Intro", "Week 1", [N(3, 1, "c")]),
        ]);

        Assert.Equal(
            "# Physics — notes\n\n" +
            "_3 notes in 2 videos_\n\n" +
            "## Lecture 1\n\n" +
            "- **[00:05]** a\n" +
            "- **[00:09]** b\n\n" +
            "## Intro\n" +
            "_Week 1_\n\n" +
            "- **[00:01]** c\n", md);
    }

    [Fact]
    public void Tab_export_with_no_notes_says_so()
    {
        Assert.Equal("# Maths — notes\n\n_No notes._\n", NotesMarkdown.ForTab("Maths", []));
    }

    [Fact]
    public void Preview_is_the_first_line_shortened()
    {
        Assert.Equal("short", N(1, 0, "short\nmore").Preview);
        Assert.Equal(60, N(1, 0, new string('x', 100)).Preview.Length);
    }
}
