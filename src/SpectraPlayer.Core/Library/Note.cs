namespace SpectraPlayer.Core.Library;

/// <summary>
/// A note on a video, anchored to a time (or to a section when <see cref="End"/> is set).
/// <see cref="Source"/> is "manual" for notes typed by the user; later AI-generated notes can use another value.
/// </summary>
public sealed record Note(
    long Id,
    string VideoPath,
    TimeSpan Start,
    TimeSpan? End,
    string Text,
    string Source,
    DateTime CreatedUtc,
    DateTime UpdatedUtc)
{
    public const string ManualSource = "manual";

    /// <summary>"12:34" for a point in time, "12:34–15:10" for a section.</summary>
    public string TimeLabel => End is { } end && end > Start
        ? $"{TimeFormat.Format(Start)}–{TimeFormat.Format(end)}"
        : TimeFormat.Format(Start);

    /// <summary>First line of the note, shortened for tooltips and previews.</summary>
    public string Preview
    {
        get
        {
            var firstLine = Text.Split('\n', 2)[0].Trim();
            return firstLine.Length <= 60 ? firstLine : firstLine[..59] + "…";
        }
    }
}
