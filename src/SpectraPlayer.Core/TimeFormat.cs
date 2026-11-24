namespace SpectraPlayer.Core;

public static class TimeFormat
{
    /// <summary>Formats as m:ss under an hour, h:mm:ss otherwise (always two-digit minutes/seconds).</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    /// <summary>
    /// A length for people to read: "42 s", "42 min", "8 h", "8 h 42 min". Rounded to the nearest minute from one minute up.
    /// </summary>
    public static string FormatTotal(TimeSpan time)
    {
        if (time < TimeSpan.FromMinutes(1)) return $"{Math.Max(0, (int)Math.Round(time.TotalSeconds))} s";

        var minutes = (long)Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero);
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0) return $"{rest} min";
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }

    /// <summary>The same length as a clock, always three parts: "08:42:15" (hours grow past two digits if they have to).</summary>
    public static string FormatClock(TimeSpan time)
    {
        var whole = TimeSpan.FromSeconds(Math.Max(0, Math.Round(time.TotalSeconds)));
        return $"{(long)whole.TotalHours:00}:{whole.Minutes:00}:{whole.Seconds:00}";
    }

    public static string Format(double seconds) =>
        Format(double.IsFinite(seconds) ? TimeSpan.FromSeconds(Math.Max(0, seconds)) : TimeSpan.Zero);
}
