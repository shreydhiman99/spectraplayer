namespace SpectraPlayer.Core.Transcription;

/// <summary>A stretch of a video, [Start, End).</summary>
public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Length => End > Start ? End - Start : TimeSpan.Zero;
    public bool IsEmpty => End <= Start;
}

/// <summary>Interval arithmetic used to work out which parts of a section still need transcribing.</summary>
public static class TimeRanges
{
    /// <summary>Gaps shorter than this are not worth a transcription run (and would only produce cut-off words).</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(1);

    /// <summary>Sorts the ranges and joins any that overlap or touch.</summary>
    public static List<TimeRange> Merge(IEnumerable<TimeRange> ranges)
    {
        var merged = new List<TimeRange>();
        foreach (var r in ranges.Where(r => !r.IsEmpty).OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End)
                merged[^1] = new TimeRange(merged[^1].Start, r.End > merged[^1].End ? r.End : merged[^1].End);
            else
                merged.Add(r);
        }
        return merged;
    }

    /// <summary>The parts of <paramref name="wanted"/> not covered by <paramref name="covered"/>, ignoring slivers under <see cref="MinGap"/>.</summary>
    public static List<TimeRange> Subtract(TimeRange wanted, IEnumerable<TimeRange> covered)
    {
        var gaps = new List<TimeRange>();
        var cursor = wanted.Start;

        foreach (var c in Merge(covered))
        {
            if (c.End <= cursor) continue;
            if (c.Start >= wanted.End) break;
            if (c.Start > cursor) gaps.Add(new TimeRange(cursor, c.Start));
            cursor = c.End > cursor ? c.End : cursor;
            if (cursor >= wanted.End) break;
        }
        if (cursor < wanted.End) gaps.Add(new TimeRange(cursor, wanted.End));

        return gaps.Where(g => g.Length >= MinGap).ToList();
    }
}
