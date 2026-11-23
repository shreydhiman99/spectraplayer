using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SpectraPlayer.Core.Library;

namespace SpectraPlayer.Core.Transcription;

public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>Reads SubRip (.srt) and WebVTT (.vtt) subtitle files into plain-text cues.</summary>
public static partial class SubtitleParser
{
    // 00:01:02,500 --> 00:01:05,000   (hours optional, comma or dot, anything after the end time is cue settings)
    [GeneratedRegex(@"^\s*(?:(?<h1>\d+):)?(?<m1>\d{1,2}):(?<s1>\d{2})[.,](?<f1>\d{1,3})\s*-->\s*(?:(?<h2>\d+):)?(?<m2>\d{1,2}):(?<s2>\d{2})[.,](?<f2>\d{1,3})")]
    private static partial Regex Timing();

    [GeneratedRegex(@"<[^>]*>|\{\\[^}]*\}")] // <i>, <font ...>, <c.yellow>, and ASS overrides like {\an8}
    private static partial Regex Markup();

    public static IReadOnlyList<SubtitleCue> ReadFile(string path) => Parse(Decode(File.ReadAllBytes(path)));

    /// <summary>Parses subtitle text. Tolerant: anything that is not a timed cue (headers, notes, numbers, junk) is skipped.</summary>
    public static IReadOnlyList<SubtitleCue> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cues = new List<SubtitleCue>();

        for (var i = 0; i < lines.Length; i++)
        {
            var m = Timing().Match(lines[i]);
            if (!m.Success) continue;

            var start = ToTime(m, "1");
            var end = ToTime(m, "2");

            var body = new List<string>();
            var j = i + 1;
            while (j < lines.Length && !string.IsNullOrWhiteSpace(lines[j]) && !Timing().IsMatch(lines[j]))
                body.Add(lines[j++]);
            i = j - 1;

            if (end <= start) continue;
            if (CleanText(body) is { } clean) cues.Add(new SubtitleCue(start, end, clean));
        }

        return cues.OrderBy(c => c.Start).ToList(); // stable: equal starts keep file order
    }

    private static TimeSpan ToTime(Match m, string n)
    {
        var hours = m.Groups["h" + n].Success ? int.Parse(m.Groups["h" + n].Value) : 0;
        var millis = int.Parse(m.Groups["f" + n].Value.PadRight(3, '0')); // ",5" means 500 ms
        return new TimeSpan(0, hours, int.Parse(m.Groups["m" + n].Value), int.Parse(m.Groups["s" + n].Value), millis);
    }

    private static string? CleanText(List<string> lines)
    {
        var joined = string.Join(" ", lines).Replace("\\N", " ").Replace("\\n", " ");
        joined = WebUtility.HtmlDecode(Markup().Replace(joined, string.Empty));
        return SegmentFilter.Clean(joined); // also drops [Music], (applause), ♪ lines
    }

    /// <summary>UTF-8/16 with or without a byte-order mark; files that are not valid UTF-8 are read as Windows-1252.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}

/// <summary>Finds the subtitle file that sits next to a video.</summary>
public static class SubtitleFinder
{
    /// <summary>Files Spectraplayer exports itself carry this suffix and are never treated as a source of subtitles.</summary>
    public const string ExportSuffix = ".transcript.srt";

    /// <summary>
    /// The best <c>.srt</c>/<c>.vtt</c> next to the video: <c>Name.srt</c> first, then English-tagged files
    /// (<c>Name.en.srt</c>, <c>Name.eng.vtt</c>…), then any other <c>Name.*.srt</c>.
    /// </summary>
    public static string? Find(string videoPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(videoPath));
        if (dir is null || !Directory.Exists(dir)) return null;

        var prefix = Path.GetFileNameWithoutExtension(videoPath) + ".";

        return Directory.EnumerateFiles(dir)
            .Select(f => (Path: f, Name: Path.GetFileName(f), Ext: Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => f.Ext is ".srt" or ".vtt"
                        && f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && !f.Name.EndsWith(ExportSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Rank(f.Name, prefix.Length, f.Ext))
            .ThenBy(f => f.Ext == ".srt" ? 0 : 1)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Path)
            .FirstOrDefault();
    }

    private static int Rank(string fileName, int prefixLength, string ext)
    {
        var baseNameLength = prefixLength - 1;                    // the prefix is "Name."
        var stem = fileName[..^ext.Length];                       // "Name" or "Name.en"
        var tag = stem.Length > baseNameLength ? stem[(baseNameLength + 1)..] : string.Empty; // "" or "en"
        if (tag.Length == 0) return 0;
        return tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }
}

public static class TranscriptSources
{
    /// <summary>Id under which text taken from a subtitle file is stored, next to the Whisper model ids.</summary>
    public const string SubtitlesId = "subtitles";
}

/// <summary>Copies the subtitle lines of a section into the library, so they behave like any other transcript.</summary>
public static class SubtitleImporter
{
    /// <returns>How many lines were added (0 if the section was already imported or the file has no text there).</returns>
    public static int Import(LibraryStore store, string videoPath, TimeRange section, IReadOnlyList<SubtitleCue> cues)
    {
        var gaps = TimeRanges.Subtract(section, store.GetTranscribedRanges(videoPath, TranscriptSources.SubtitlesId));
        var added = 0;

        foreach (var gap in gaps)
        {
            // A cue belongs to the gap whose range holds its centre, so neighbouring imports never store the same line twice.
            var kept = cues.Where(c =>
            {
                var centre = c.Start + (c.End - c.Start) / 2;
                return centre >= gap.Start && centre < gap.End;
            }).ToList();

            // A line that runs past the edge was stored with this gap, so coverage extends to its end.
            var end = kept.Count > 0 && kept.Max(c => c.End) > gap.End ? kept.Max(c => c.End) : gap.End;
            store.AddTranscribed(videoPath, TranscriptSources.SubtitlesId, new TimeRange(gap.Start, end),
                kept.Select(c => (c.Start, c.End, c.Text)).ToList());
            added += kept.Count;
        }
        return added;
    }
}

/// <summary>Turns transcript lines into a Markdown document or a SubRip subtitle file.</summary>
public static class TranscriptExport
{
    public static string ToMarkdown(string videoTitle, string sourceLabel, IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(videoTitle.Trim()).Append(" — transcript\n\n");
        if (segments.Count == 0) return sb.Append("_No text._\n").ToString();

        sb.Append('_').Append(segments.Count).Append(segments.Count == 1 ? " line" : " lines")
          .Append(" · ").Append(sourceLabel).Append("_\n\n");

        foreach (var s in segments.OrderBy(s => s.Start))
            sb.Append("**[").Append(TimeFormat.Format(s.Start)).Append("]** ").Append(s.Text.Trim()).Append("\n\n");

        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>SubRip with times in the video's own timeline, so it can be loaded next to the video as a subtitle file.</summary>
    public static string ToSrt(IReadOnlyList<TranscriptSegment> segments)
    {
        var ordered = segments.OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        var sb = new StringBuilder();

        for (var i = 0; i < ordered.Count; i++)
        {
            var start = ordered[i].Start;
            var end = ordered[i].End;

            // Lines found with context padding can overlap their neighbour a little; players want them in sequence.
            if (i + 1 < ordered.Count && ordered[i + 1].Start < end) end = ordered[i + 1].Start;
            if (end <= start) end = start + TimeSpan.FromMilliseconds(500);

            sb.Append(i + 1).Append("\r\n")
              .Append(SrtTime(start)).Append(" --> ").Append(SrtTime(end)).Append("\r\n")
              .Append(ordered[i].Text.Trim()).Append("\r\n\r\n");
        }
        return sb.ToString();
    }

    internal static string SrtTime(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
}
