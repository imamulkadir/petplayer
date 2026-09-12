using System.Globalization;
using System.Windows.Data;
using PetPlayer.Models;

namespace PetPlayer.Helpers;

/// <summary>Segoe MDL2 Assets glyph for the play/pause button, based on playback state.</summary>
public sealed class PlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "" : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Segoe MDL2 Assets glyph for the volume/mute button.</summary>
public sealed class MuteGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "" : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a bool - used to collapse elements while media hasn't loaded yet.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value is true);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value is true);
}

/// <summary>Formats a TimeSpan as an SRT-style timestamp for the transcript panel.</summary>
public sealed class TranscriptTimestampConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan time ? TimeFormatter.FormatSrtTimestamp(time) : string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Reference-compares a transcript entry against the currently active one, for row highlighting.</summary>
public sealed class TranscriptActiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is TranscriptEntry item && ReferenceEquals(item, values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses an element when a bound integer count is zero (e.g. an empty-state message).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Extracts just the file name from a full path, for the title bar.</summary>
public sealed class FilePathToFileNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path && !string.IsNullOrEmpty(path) ? System.IO.Path.GetFileName(path) : string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Computes the proportional Star width of a slider's filled/unfilled track
/// segments from [Value, Minimum, Maximum], so the fill renders as a simple
/// two-column Grid rather than needing the track's own ActualWidth.
/// ConverterParameter "Invert" gives the remaining (unfilled) share.
/// </summary>
public sealed class SliderFillRatioConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length != 3 || values[0] is not double value || values[1] is not double min || values[2] is not double max)
        {
            return new System.Windows.GridLength(0, System.Windows.GridUnitType.Star);
        }

        var range = max - min;
        var fraction = range > 0 ? Math.Clamp((value - min) / range, 0.0, 1.0) : 0.0;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            fraction = 1.0 - fraction;
        }

        // A minimum share keeps a zero-width column from collapsing the Grid's rounding.
        return new System.Windows.GridLength(Math.Max(fraction, 0.0001), System.Windows.GridUnitType.Star);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
