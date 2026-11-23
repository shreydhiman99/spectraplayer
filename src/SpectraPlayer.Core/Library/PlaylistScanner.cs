namespace SpectraPlayer.Core.Library;

/// <param name="FullPath">Absolute path of the media file.</param>
/// <param name="RelativePath">Path relative to the scanned folder (used for ordering and for the subfolder label).</param>
public sealed record MediaEntry(string FullPath, string RelativePath)
{
    public string Name => Path.GetFileNameWithoutExtension(FullPath);

    /// <summary>The subfolder the file sits in, relative to the tab's folder; empty at the top level.</summary>
    public string Subfolder => Path.GetDirectoryName(RelativePath) ?? string.Empty;
}

public static class MediaExtensions
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".m2ts", ".mts",
        ".mpg", ".mpeg", ".3gp", ".ogv", ".vob",
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus",
    };

    public static bool IsMedia(string path) => Known.Contains(Path.GetExtension(path));
}

/// <summary>Turns a folder into a playlist: every media file below it, in natural order.</summary>
public static class PlaylistScanner
{
    public static IReadOnlyList<MediaEntry> Scan(string folder, bool recursive = true)
    {
        var root = Path.GetFullPath(folder);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var entries = Directory.EnumerateFiles(root, "*", options)
            .Where(MediaExtensions.IsMedia)
            .Select(f => new MediaEntry(f, Path.GetRelativePath(root, f)))
            .ToList();

        entries.Sort((a, b) => NaturalComparer.Instance.Compare(a.RelativePath, b.RelativePath));
        return entries;
    }
}
