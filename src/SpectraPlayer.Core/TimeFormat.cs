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

    public static string Format(double seconds) =>
        Format(double.IsFinite(seconds) ? TimeSpan.FromSeconds(Math.Max(0, seconds)) : TimeSpan.Zero);
}
