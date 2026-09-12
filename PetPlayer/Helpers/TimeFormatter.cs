namespace PetPlayer.Helpers;

public static class TimeFormatter
{
    /// <summary>Formats a duration as m:ss or h:mm:ss depending on magnitude.</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }

    public static string FormatMilliseconds(long milliseconds) =>
        Format(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)));

    /// <summary>Formats a subtitle/srt-style timestamp: 00:01:14,000</summary>
    public static string FormatSrtTimestamp(TimeSpan time) =>
        $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2},{time.Milliseconds:D3}";

    public static string FormatDelta(int milliseconds)
    {
        var sign = milliseconds >= 0 ? "+" : "-";
        return $"{sign}{Math.Abs(milliseconds)} ms";
    }
}
