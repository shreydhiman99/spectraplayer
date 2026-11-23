namespace SpectraPlayer.Core.Bookmarks;

/// <summary>Finds where a video's .pbf file lives and where we should write it.</summary>
public static class BookmarkLocator
{
    /// <summary>Same folder and name as the video, extension swapped. PotPlayer treats this like external chapters.</summary>
    public static string SidecarPath(string videoPath) => Path.ChangeExtension(videoPath, ".pbf");

    /// <summary>PotPlayer's own bookmark folders under %APPDATA% (used when it stores .pbf files centrally).</summary>
    public static IEnumerable<string> PotPlayerBookmarkFolders()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData)) yield break;
        yield return Path.Combine(appData, "PotPlayerMini64", "Bookmark");
        yield return Path.Combine(appData, "PotPlayerMini", "Bookmark");
    }

    public static IEnumerable<string> CandidatePaths(string videoPath, IEnumerable<string>? extraFolders = null)
    {
        yield return SidecarPath(videoPath);

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var fileName = Path.GetFileName(videoPath);
        foreach (var folder in (extraFolders ?? PotPlayerBookmarkFolders()))
        {
            yield return Path.Combine(folder, baseName + ".pbf");
            yield return Path.Combine(folder, fileName + ".pbf");
        }
    }

    public static string? FindExisting(string videoPath, IEnumerable<string>? extraFolders = null) =>
        CandidatePaths(videoPath, extraFolders).FirstOrDefault(File.Exists);
}
