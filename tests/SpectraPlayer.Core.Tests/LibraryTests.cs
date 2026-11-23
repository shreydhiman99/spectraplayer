using SpectraPlayer.Core.Library;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class NaturalComparerTests
{
    private static List<string> Sorted(params string[] items)
    {
        var list = items.ToList();
        list.Sort(NaturalComparer.Instance);
        return list;
    }

    [Fact]
    public void Numbers_compare_by_value_not_text()
    {
        Assert.Equal(["Lecture 1", "Lecture 2", "Lecture 10", "Lecture 11"],
            Sorted("Lecture 10", "Lecture 2", "Lecture 11", "Lecture 1"));
    }

    [Fact]
    public void Ignores_case_and_handles_leading_zeros()
    {
        Assert.Equal(["a01", "B2", "b10"], Sorted("b10", "a01", "B2"));
        Assert.True(NaturalComparer.Instance.Compare("part 007", "part 8") < 0);
    }

    [Fact]
    public void Same_value_with_different_zero_padding_is_ordered_deterministically()
    {
        Assert.True(NaturalComparer.Instance.Compare("v1", "v01") < 0);
        Assert.True(NaturalComparer.Instance.Compare("v01", "v1") > 0);
        Assert.Equal(0, NaturalComparer.Instance.Compare("v1", "v1"));
    }

    [Fact]
    public void Prefix_sorts_before_longer_string_and_nulls_first()
    {
        Assert.True(NaturalComparer.Instance.Compare("abc", "abcd") < 0);
        Assert.True(NaturalComparer.Instance.Compare(null, "a") < 0);
    }
}

public class PlaylistScannerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("spectra-scan");

    public void Dispose() => _root.Delete(true);

    private void Touch(string relative)
    {
        var path = Path.Combine(_root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void Finds_media_recursively_in_natural_order_and_skips_other_files()
    {
        Touch("Lecture 10.mp4");
        Touch("Lecture 2.MKV");
        Touch("notes.txt");
        Touch("slides.pdf");
        Touch(@"Week 1\a.mp4");
        Touch(@"Week 1\subs.srt");

        var entries = PlaylistScanner.Scan(_root.FullName);

        Assert.Equal(["Lecture 2.MKV", "Lecture 10.mp4", @"Week 1\a.mp4"], entries.Select(e => e.RelativePath));
        Assert.Equal("", entries[0].Subfolder);
        Assert.Equal("Week 1", entries[2].Subfolder);
        Assert.Equal("Lecture 2", entries[0].Name);
    }

    [Fact]
    public void Non_recursive_scan_ignores_subfolders()
    {
        Touch("a.mp4");
        Touch(@"sub\b.mp4");
        Assert.Single(PlaylistScanner.Scan(_root.FullName, recursive: false));
    }

    [Fact]
    public void Empty_folder_gives_empty_playlist()
    {
        Assert.Empty(PlaylistScanner.Scan(_root.FullName));
    }
}

public class LibraryStoreTests : IDisposable
{
    private readonly LibraryStore _store = new(":memory:");

    public void Dispose() => _store.Dispose();

    [Fact]
    public void AddTab_uses_folder_name_as_title_and_keeps_insertion_order()
    {
        var a = _store.AddTab(@"D:\Courses\Physics");
        var b = _store.AddTab(@"D:\Courses\Maths\", "Maths II");

        Assert.Equal("Physics", a.Title);
        Assert.Equal(@"D:\Courses\Maths", b.Folder); // trailing slash trimmed
        Assert.Equal(["Physics", "Maths II"], _store.GetTabs().Select(t => t.Title));
    }

    [Fact]
    public void AddTab_for_same_folder_returns_existing_tab_ignoring_case()
    {
        var first = _store.AddTab(@"D:\Courses\Physics");
        var again = _store.AddTab(@"d:\courses\PHYSICS\");

        Assert.Equal(first.Id, again.Id);
        Assert.Single(_store.GetTabs());
    }

    [Fact]
    public void Rename_remove_last_video_and_reorder()
    {
        var a = _store.AddTab(@"D:\A");
        var b = _store.AddTab(@"D:\B");
        var c = _store.AddTab(@"D:\C");

        _store.RenameTab(b.Id, "  Bee  ");
        _store.SetLastVideo(a.Id, @"D:\A\1.mp4");
        _store.ReorderTabs([c.Id, a.Id, b.Id]);
        _store.RemoveTab(a.Id);

        var tabs = _store.GetTabs();
        Assert.Equal(["C", "Bee"], tabs.Select(t => t.Title));
        Assert.All(tabs, t => Assert.Null(t.LastVideo));
    }

    [Fact]
    public void Progress_round_trips_and_computes_fraction()
    {
        _store.SaveProgress(@"D:\A\1.mp4", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20));
        var p = _store.GetProgress(@"d:\a\1.MP4")!; // path lookup ignores case

        Assert.Equal(TimeSpan.FromMinutes(5), p.Position);
        Assert.Equal(0.25, p.Fraction, 3);
        Assert.False(p.IsWatched);

        _store.SaveProgress(@"D:\A\1.mp4", TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(20));
        Assert.True(_store.GetProgress(@"D:\A\1.mp4")!.IsWatched);
    }

    [Fact]
    public void GetProgressUnder_only_returns_files_inside_the_folder()
    {
        _store.SaveProgress(@"D:\Course\a.mp4", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100));
        _store.SaveProgress(@"D:\Course\sub\b.mp4", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(100));
        _store.SaveProgress(@"D:\Course2\c.mp4", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(100));
        _store.SaveProgress(@"D:\Cour_e\d.mp4", TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(100)); // '_' must not act as a wildcard

        var map = _store.GetProgressUnder(@"d:\course");

        Assert.Equal(2, map.Count);
        Assert.True(map.ContainsKey(@"D:\Course\a.mp4"));
        Assert.True(map.ContainsKey(@"D:\Course\sub\b.mp4"));
    }

    [Fact]
    public void MarkWatched_works_for_unplayed_and_played_videos_and_Clear_resets()
    {
        _store.MarkWatched(@"D:\X\new.mp4");
        Assert.True(_store.GetProgress(@"D:\X\new.mp4")!.IsWatched);

        _store.SaveProgress(@"D:\X\part.mp4", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100));
        _store.MarkWatched(@"D:\X\part.mp4");
        var part = _store.GetProgress(@"D:\X\part.mp4")!;
        Assert.Equal(TimeSpan.FromSeconds(100), part.Position);
        Assert.True(part.IsWatched);

        _store.ClearProgress(@"D:\X\part.mp4");
        Assert.Null(_store.GetProgress(@"D:\X\part.mp4"));
    }

    [Fact]
    public void Settings_round_trip_and_overwrite()
    {
        Assert.Null(_store.GetSetting("activeTab"));
        _store.SetSetting("activeTab", "3");
        _store.SetSetting("activeTab", "4");
        Assert.Equal("4", _store.GetSetting("activeTab"));
    }

    [Fact]
    public void Data_survives_reopening_the_file()
    {
        var dir = Directory.CreateTempSubdirectory("spectra-db");
        try
        {
            var file = Path.Combine(dir.FullName, "sub", "library.db"); // folder is created on demand
            using (var first = new LibraryStore(file))
            {
                first.AddTab(@"D:\Physics");
                first.SaveProgress(@"D:\Physics\1.mp4", TimeSpan.FromSeconds(42), TimeSpan.FromSeconds(100));
            }
            using var second = new LibraryStore(file);
            Assert.Equal("Physics", second.GetTabs().Single().Title);
            Assert.Equal(TimeSpan.FromSeconds(42), second.GetProgress(@"D:\Physics\1.mp4")!.Position);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            dir.Delete(true);
        }
    }
}
