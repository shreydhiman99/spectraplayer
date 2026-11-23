using System.Text.RegularExpressions;

namespace SpectraPlayer.Core.Transcription;

/// <summary>One piece of recognised speech with absolute video times, as stored in the library.</summary>
public sealed record TranscriptSegment(long Id, string VideoPath, string Model, TimeSpan Start, TimeSpan End, string Text)
{
    public string TimeLabel => TimeFormat.Format(Start);
}

/// <summary>What the speech recogniser produces: times are relative to the start of the audio it was given.</summary>
public sealed record RecognizedSegment(TimeSpan Start, TimeSpan End, string Text);

/// <summary>Whisper labels silence and music with markers like [BLANK_AUDIO] or (music); those are not speech.</summary>
public static partial class SegmentFilter
{
    [GeneratedRegex(@"^\s*[\[\(\*].{0,40}[\]\)\*]\s*$")]
    private static partial Regex Marker();

    [GeneratedRegex(@"^[\s♪♫\-–—\.\,…]*$")]
    private static partial Regex OnlyMusicOrPunctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The text with surrounding space trimmed and inner runs of whitespace collapsed, or null if it isn't speech.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Whitespace().Replace(text.Trim(), " ");
        if (Marker().IsMatch(t) || OnlyMusicOrPunctuation().IsMatch(t)) return null;
        return t;
    }
}
