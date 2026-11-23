namespace SpectraPlayer.Core;

/// <summary>
/// A marked section of the current video (start and end). Marking two points in either order works: the earlier one
/// becomes the start. Everything is clamped to the video's length when that is known.
/// </summary>
public sealed class SectionSelection
{
    /// <summary>A section shorter than this isn't usable (nothing meaningful to transcribe).</summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromSeconds(1);

    public TimeSpan? Start { get; private set; }
    public TimeSpan? End { get; private set; }

    public bool IsEmpty => Start is null && End is null;

    /// <summary>Both ends set and at least <see cref="MinLength"/> apart.</summary>
    public bool IsComplete => Start is { } s && End is { } e && e - s >= MinLength;

    public TimeSpan Length => Start is { } s && End is { } e && e > s ? e - s : TimeSpan.Zero;

    public event EventHandler? Changed;

    public void SetStart(TimeSpan time, TimeSpan? duration = null)
    {
        time = Clamp(time, duration);
        if (End is not { } end || time < end) Apply(time, End);
        else if (time > end) Apply(end, time);    // marked past the end: the old end becomes the start
        else Apply(time, null);                   // same spot: only a start
    }

    public void SetEnd(TimeSpan time, TimeSpan? duration = null)
    {
        time = Clamp(time, duration);
        if (Start is not { } start || time > start) Apply(Start, time);
        else if (time < start) Apply(time, start); // marked before the start: swap
        else Apply(start, null);
    }

    /// <summary>Sets the section from two points in any order (used when dragging on the seek bar).</summary>
    public void Set(TimeSpan a, TimeSpan b, TimeSpan? duration = null)
    {
        a = Clamp(a, duration);
        b = Clamp(b, duration);
        if (a == b) Apply(a, null);
        else Apply(a < b ? a : b, a < b ? b : a);
    }

    /// <summary>The last <paramref name="span"/> up to <paramref name="position"/> (from the start of the video if it is earlier).</summary>
    public void SelectLast(TimeSpan span, TimeSpan position, TimeSpan duration)
    {
        var end = Clamp(position, duration);
        var start = TimeSpan.FromTicks(Math.Max(0, end.Ticks - span.Ticks));
        if (end - start < MinLength) // near the very start: take the first stretch instead
        {
            start = TimeSpan.Zero;
            end = duration > TimeSpan.Zero ? TimeSpan.FromTicks(Math.Min(span.Ticks, duration.Ticks)) : span;
        }
        Apply(start, end);
    }

    public void SelectWhole(TimeSpan duration)
    {
        if (duration > TimeSpan.Zero) Apply(TimeSpan.Zero, duration);
    }

    public void Clear() => Apply(null, null);

    private static TimeSpan Clamp(TimeSpan time, TimeSpan? duration)
    {
        if (time < TimeSpan.Zero) return TimeSpan.Zero;
        return duration is { } d && d > TimeSpan.Zero && time > d ? d : time;
    }

    private void Apply(TimeSpan? start, TimeSpan? end)
    {
        if (Start == start && End == end) return;
        Start = start;
        End = end;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
