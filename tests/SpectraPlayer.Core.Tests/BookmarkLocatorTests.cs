using SpectraPlayer.Core.Bookmarks;
using Xunit;

namespace SpectraPlayer.Core.Tests;

public class BookmarkLocatorTests
{
    [Fact]
    public void Sidecar_swaps_the_extension()
    {
        Assert.Equal(@"D:\Course\Lecture 01.pbf", BookmarkLocator.SidecarPath(@"D:\Course\Lecture 01.mkv"));
    }

    [Fact]
    public void Candidates_start_with_sidecar_then_potplayer_folders()
    {
        var c = BookmarkLocator.CandidatePaths(@"D:\v\a.mp4", [@"C:\pp\Bookmark"]).ToList();
        Assert.Equal([@"D:\v\a.pbf", @"C:\pp\Bookmark\a.pbf", @"C:\pp\Bookmark\a.mp4.pbf"], c);
    }

    [Fact]
    public void FindExisting_prefers_sidecar_and_returns_null_when_nothing_exists()
    {
        var dir = Directory.CreateTempSubdirectory("spectra-loc");
        try
        {
            var video = Path.Combine(dir.FullName, "a.mp4");
            Assert.Null(BookmarkLocator.FindExisting(video, []));

            var sidecar = Path.Combine(dir.FullName, "a.pbf");
            File.WriteAllText(sidecar, "[Bookmark]");
            Assert.Equal(sidecar, BookmarkLocator.FindExisting(video, []));
        }
        finally { dir.Delete(true); }
    }
}
